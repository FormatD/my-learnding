"""AT22/26/36 against a disposable PostgreSQL database and an isolated API listener."""
import argparse,copy,getpass,json,os,secrets,socket,subprocess,tempfile,time,urllib.request,uuid
from datetime import date,timedelta
from pathlib import Path
import api_acceptance
from api_acceptance import Client,TODAY
from review_reporting_acceptance import verify as verify_weekly_reviews

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--browser",action="store_true",help="Also verify weekly review presentation against the same disposable API.");args=parser.parse_args()
    root=Path(__file__).resolve().parent.parent;env=os.environ.copy();suffix=uuid.uuid4().hex[:12];database='learning_fault_'+suffix
    env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,PLAN_TEST_USERNAME='plan-fixture-'+suffix,PLAN_TEST_PASSWORD=secrets.token_hex(20))
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH'];connection=f'Host=127.0.0.1;Port=55432;Database={database};Username={env["PGUSER"]}';env['PERSISTENCE_TEST_CONNECTION']=connection;env['ConnectionStrings__Learning']=connection
    dotnet=str(root/'.tools/dotnet/dotnet');child=None
    subprocess.run(['createdb',database],env=env,check=True)
    with tempfile.TemporaryDirectory(prefix='learning-plan-') as tmp:
        env.update(DeletionLedger=str(Path(tmp)/'deleted-students.txt'),FamilyDeletionLedger=str(Path(tmp)/'deleted-families.txt'),ExportDirectory=str(Path(tmp)/'exports'))
        try:
            output=subprocess.check_output([dotnet,str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'plan-boundary-seed'],env=env,text=True);fixture=json.loads(output.strip().splitlines()[-1])
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            with open(Path(tmp)/'api.log','w+') as log:
                child=subprocess.Popen([dotnet,str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',f'http://127.0.0.1:{port}'],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT)
                deadline=time.monotonic()+30
                while time.monotonic()<deadline:
                    if child.poll() is not None:log.seek(0);raise AssertionError('isolated API stopped: '+log.read()[-1500:])
                    try:
                        with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('isolated API did not become ready')
                api_acceptance.BASE=f'http://127.0.0.1:{port}/api/v1';c=Client();credentials={'userName':env['PLAN_TEST_USERNAME'],'password':env['PLAN_TEST_PASSWORD']};c.request('/auth/login',credentials);c.request('/me')
                sid=fixture['studentId'];fid=fixture['familyId']
                def generate(student,day=TODAY):c.request('/students/'+student+'/plans/'+day+':generate',{});return c.request('/students/'+student+'/plans/'+day)
                def publish(view):c.request('/plans/'+view['revision']['id']+':publish',{'previewHash':view['revision']['inputHash'],'confirmWarnings':True})
                def settle(student):
                    for _ in range(100):
                        m=c.request('/students/'+student+'/mastery')
                        if m['pending']==0:return m
                        time.sleep(.1)
                    raise AssertionError('projection did not settle')
                def wrong():return next(r for r in c.request('/students/'+sid+'/reviews') if r['targetType']=='WrongQuestion')
                before=wrong();assert before['stage']=='R1' and before['dueDate']==TODAY and before['status']=='Pending'
                evidence=c.request('/students/'+sid+'/mastery/'+fixture['kcId'])['evidence'];original_generation=settle(sid)['generation']
                view=generate(sid);task=next(t for t in view['tasks'] if t['reasonCode']=='WRONG_DUE');publish(view);c.request('/tasks/'+task['id']+':transition',{'status':'Skipped','reason':'今天先跳过'})
                assert wrong()==before and settle(sid)['generation']==original_generation and len(c.request('/students/'+sid+'/attempts')['attempts'])==1
                view=generate(sid);retry=next(t for t in view['tasks'] if t['reasonCode']=='WRONG_DUE');assert retry['id']!=task['id'];publish(view);c.request('/tasks/'+retry['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+retry['id']+'/sessions',{});c.request('/sessions/'+session['sessionId']+'/hints',{'level':3});c.request('/tasks/'+retry['id']+':transition',{'status':'Abandoned','reason':'只核对了解析，未提交复测'})
                assert wrong()==before and c.request('/students/'+sid+'/mastery/'+fixture['kcId'])['evidence']==evidence and len(c.request('/students/'+sid+'/attempts')['attempts'])==1
                tomorrow=(date.fromisoformat(TODAY)+timedelta(days=1)).isoformat();assert any(t['reasonCode']=='WRONG_DUE' for t in generate(sid,tomorrow)['tasks'])
                print('PASS AT22 到期跳过或仅看解析不产生作答/证据，不晋阶段、不推迟日期，次日仍可安排')
                # A separate student has three school-current tasks: completed, running, and locked.
                s=c.request('/students',{'name':'任务保留和耗时验收'},expected=201);sid2=s['id'];c.request('/students/'+sid2+'/content/'+fixture['releaseId']+':bind',{});catalog=c.request('/students/'+sid2+'/catalog');c.request('/students/'+sid2+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT');view=generate(sid2);assert len(view['tasks'])==3
                completed,running,locked=view['tasks'];ids=[t['id'] for t in view['tasks']];c.request('/plans/'+view['revision']['id']+':adjust',{'taskIds':ids,'lockedIds':[locked['id']],'reason':'保留最后一项家长锁定任务'});view=c.request('/students/'+sid2+'/plans/'+TODAY);publish(view)
                c.request('/tasks/'+completed['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+completed['id']+'/sessions',{});answer=next(q['answer'] for q in catalog['questions'] if q['id']==completed['questionId']);c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':answer},expected=201);c.request('/tasks/'+completed['id']+':transition',{'status':'Completed','actualMinutes':8});settle(sid2)
                c.request('/tasks/'+running['id']+':transition',{'status':'InProgress'});running_session=c.request('/tasks/'+running['id']+'/sessions',{})
                # Seed elapsed timer fields only in the verified disposable database, without changing learning results.
                query=f'''UPDATE "Tasks" SET "TrackedSeconds"=120,"StartedAt"=NOW()-interval '6 minutes 5 seconds' WHERE "Id"='{running['id']}' AND "StudentId"='{sid2}' AND "FamilyId"='{fid}';'''
                subprocess.run(['psql','-v','ON_ERROR_STOP=1','-c',query],env=env,check=True,stdout=subprocess.DEVNULL)
                c.request('/students/'+sid2+'/availability/'+TODAY,{'minutes':15,'reserved':0},method='PUT');view=generate(sid2);assert [t['id'] for t in view['tasks']]==ids
                rows={t['id']:t for t in view['tasks']};assert rows[completed['id']]['status']=='Completed' and rows[completed['id']]['actualMinutes']==8;assert rows[running['id']]['status']=='InProgress' and rows[running['id']]['actualMinutes']==9 and rows[running['id']]['trackedSeconds']==120;assert rows[locked['id']]['status']=='Ready' and rows[locked['id']]['locked']
                assert view['revision']['ruleVersion']=='plan/2' and view['revision']['overflow']==7 and any('MANDATORY_OVERFLOW' in w for w in json.loads(view['revision']['warnings']))
                again=generate(sid2);assert again['revision']['id']==view['revision']['id'];c.request('/plans/'+view['revision']['id']+':adjust',{'taskIds':[completed['id'],locked['id']],'lockedIds':[locked['id']],'reason':'不得移除进行中任务'},expected=422)
                c.request('/plans/'+view['revision']['id']+':publish',{'previewHash':view['revision']['inputHash']},expected=422);publish(view)
                print('PASS AT26 三种固定任务身份及状态保留，已完成8/进行中9/锁定5共22分钟，预算15超载7且不加可选')
                # Two independently authenticated editors start from the same family/draft version.
                c.request('/me');editor_name='plan-editor-'+suffix;c.request('/family/members',{'userName':editor_name,'password':env['PLAN_TEST_PASSWORD'],'roles':['ContentEditor','Publisher']},expected=201);editor=Client();editor.request('/auth/login',{'userName':editor_name,'password':env['PLAN_TEST_PASSWORD']});editor.request('/me')
                draft=c.request('/content/drafts',{'title':'双编辑者冲突验收','catalog':catalog});c.request('/content/drafts/'+draft['id']+':review',{});old_preview=c.request('/content/drafts/'+draft['id']+'/preview');editor.request('/content');stale_etag=c.etag
                edited=copy.deepcopy(catalog);question=next(q for q in edited['questions'] if q['id']==running['questionId']);question['revisionId']=str(uuid.uuid4());question['stem']+='（第二位编辑者的新修订题面）';question['explanation']+=' 第二位编辑者复核。'
                editor.request('/content/drafts/'+draft['id'],{'title':draft['title'],'catalog':edited},method='PUT');c.etag=stale_etag;c.request('/content/drafts/'+draft['id'],{'title':'旧编辑覆盖尝试','catalog':catalog},method='PUT',expected=412)
                latest=next(d for d in c.request('/content')['drafts'] if d['id']==draft['id']);assert latest['version']==2 and latest['status']=='Draft' and latest['reviewedBy'] is None and json.loads(latest['payload'])==edited
                editor.request('/me');editor.request('/content/drafts/'+draft['id']+':review',{});c.request('/me');c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':old_preview['hash']},expected=412);fresh=c.request('/content/drafts/'+draft['id']+'/preview');assert fresh['hash']!=old_preview['hash'];updated_release=c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':fresh['hash']})
                print('PASS AT36 两位编辑者冲突返回412，后保存内容不被覆盖；重新审核后旧发布预览仍拒绝')
                c.request('/students/'+sid2+'/content/'+updated_release['id']+':bind',{});switched=generate(sid2);assert switched['revision']['releaseId']==updated_release['id'] and [t['id'] for t in switched['tasks']]==ids and all(t['releaseId']==fixture['releaseId'] for t in switched['tasks']);publish(switched)
                # The child's supplied duration is ignored; frozen running sessions still use their original content.
                child_client=Client();child_client.request('/auth/login',credentials);child_client.request('/me');child_client.request('/students/'+sid2+'/child-sessions',{});resumed=child_client.request('/tasks/'+running['id']+'/sessions',{});assert resumed['sessionId']==running_session['sessionId'] and resumed['stem']==running_session['stem'] and resumed['releaseId']==fixture['releaseId'] and resumed['stem']!=question['stem']
                answer=next(q['answer'] for q in catalog['questions'] if q['id']==running['questionId']);child_client.request('/sessions/'+resumed['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':answer},expected=201);done=child_client.request('/tasks/'+running['id']+':transition',{'status':'Completed','actualMinutes':599});assert done['actualMinutes']==9 and done['trackedSeconds']>=480 and done['startedAt'] is None
                print('PASS AT26 切换内容和重发布仍恢复原题/会话，进行中计时不中断，孩子无法伪造实际耗时')
                verify_weekly_reviews(c,fixture)
                if args.browser:
                    browser_env={**env,'LEARNING_TEST_URL':f'http://127.0.0.1:{port}','REVIEW_REPORT_STUDENT':sid}
                    subprocess.run(['npm','--prefix',str(root/'src/web'),'run','test:e2e','--','tests/review-report.spec.ts'],env=browser_env,check=True,timeout=120)
        finally:
            if child is not None and child.poll() is None:child.terminate();child.wait(timeout=10)
            subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
