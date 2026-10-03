"""Actual private HTTP parent confirmation, background result, export and replanning."""
import argparse,json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--browser',action='store_true');args=parser.parse_args()
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);sdk=dotnet(root)
    env.update(REVIEW_CONFIRM_MODERN='1',REVIEW_CONFIRM_USERNAME='review-confirm-'+uuid.uuid4().hex[:12],REVIEW_CONFIRM_PASSWORD=secrets.token_hex(24),ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION'])
    subprocess.run(['createdb',database],env=env,check=True);child=None
    try:
        fixture=json.loads(subprocess.check_output([sdk,str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'review-confirm-api-seed'],env=env,text=True).strip().splitlines()[-1])
        with tempfile.TemporaryDirectory(prefix='review-confirm-api-') as directory:
            work=Path(directory);env.update(DeletionLedger=str(work/'students-deleted'),FamilyDeletionLedger=str(work/'families-deleted'),ExportDirectory=str(work/'exports'))
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            with (work/'api.log').open('w+') as log:
                child=subprocess.Popen([sdk,str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',f'http://127.0.0.1:{port}'],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT)
                deadline=time.monotonic()+30
                while time.monotonic()<deadline:
                    if child.poll() is not None:log.seek(0);raise AssertionError(log.read()[-1500:])
                    try:
                        with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('isolated API did not become ready')
                api_acceptance.BASE=f'http://127.0.0.1:{port}/api/v1';c=Client();c.request('/auth/login',{'userName':env['REVIEW_CONFIRM_USERNAME'],'password':env['REVIEW_CONFIRM_PASSWORD']});c.request('/me');sid=fixture['studentId'];aid=fixture['attemptId'];base='/students/'+sid+'/attempts/'+aid+'/review-target:'
                assert fixture['mappingMode']=='FixedContainer';original=c.request('/students/'+sid+'/export');changes=c.request('/students/'+sid+'/review-target-changes');assert len(changes)==1 and changes[0]['confirmation'] is None and changes[0]['measuredTargets'][0]['id']==fixture['actualTarget']
                def settle():
                    for _ in range(150):
                        status=c.request('/students/'+sid+'/mastery')
                        if status['pending']==0:return status
                        time.sleep(.1)
                    raise AssertionError('actual confirmation source did not settle')
                def confirm(action,target,reason):
                    input={'action':action,'targetId':target,'reason':reason};preview=c.request(base+'preview',input);row=c.request(base+'confirm',{**input,'previewHash':preview['previewHash']},expected=202);settle();return row
                kept=confirm('KeepOriginal',None,'家长明确保留原目标并另外安排');reviews=c.request('/students/'+sid+'/reviews');assert all(r['stage']=='R1' for r in reviews if r['targetType']=='KC' and r['targetId'] in (fixture['originalTarget'],fixture['actualTarget']))
                invalid={'action':'AdoptMeasuredTarget','targetId':fixture['originalTarget'],'reason':'原目标已不被测量'};c.request(base+'preview',invalid,expected=422)
                adopted=confirm('AdoptMeasuredTarget',fixture['actualTarget'],'家长明确按当前有效题目实际目标核对');reviews=c.request('/students/'+sid+'/reviews');assert next(r for r in reviews if r['targetId']==fixture['originalTarget'])['stage']=='R1' and next(r for r in reviews if r['targetId']==fixture['actualTarget'])['stage']=='LowFrequency'
                exported=c.request('/students/'+sid+'/export');assert exported['attempts']==original['attempts'] and exported['tasks']==original['tasks'] and exported['gradings']==original['gradings'] and exported['revisions']==original['revisions'] and exported['placements']==original['placements'];assert exported['reviewTargetConfirmations']==[kept,adopted] and all(r['mappingSetRevisionId'] for r in exported['reviewTargetConfirmations'])
                day=api_acceptance.TODAY;c.request('/students/'+sid+'/plans/'+day+':generate',{});plan=c.request('/students/'+sid+'/plans/'+day);task=next(t for t in plan['tasks'] if t['reviewTargetId']==fixture['originalTarget']);assert task['questionId']!=next(t for t in original['tasks'] if t['id']==fixture['taskId'])['questionId'];c.request('/plans/'+plan['revision']['id']+':publish',{'previewHash':plan['revision']['inputHash'],'confirmWarnings':True});after=c.request('/students/'+sid+'/export');assert next(t for t in after['tasks'] if t['id']==fixture['taskId'])==next(t for t in original['tasks'] if t['id']==fixture['taskId']);assert after['reviewTargetConfirmations']==[kept,adopted]
                print('PASS 实际HTTP两种家长确认均202保存原测量依据，经真实后台重算才生效；未测量目标422，导出原任务/判分/作答/发布修订/关联字节不改')
                print('PASS 确认后实际生成并发布新计划，为原目标安排当前正式题目，原发布任务与两次确认保留')
                child_client=Client();child_client.request('/auth/login',{'userName':env['REVIEW_CONFIRM_USERNAME'],'password':env['REVIEW_CONFIRM_PASSWORD']});child_client.request('/me');child_client.request('/students/'+sid+'/child-sessions',{});child_client.request('/students/'+sid+'/review-target-changes',expected=403);child_client.request(base+'preview',{'action':'KeepOriginal','targetId':None,'reason':'孩子不得确认'},expected=403)
                outsider=Client();outsider.request('/auth/register',{'userName':'outside-review-'+uuid.uuid4().hex[:10],'password':secrets.token_hex(24)},expected=201);outsider.request('/me');outsider.request('/students/'+sid+'/review-target-changes',expected=404);outsider.request(base+'preview',{'action':'KeepOriginal','targetId':None,'reason':'跨家庭不能查看'},expected=404);print('PASS 实际子会话确认拒绝、跨家庭读取与预览404')
                if args.browser:
                    browser_env={**env,'LEARNING_TEST_URL':f'http://127.0.0.1:{port}','REVIEW_CONFIRM_STUDENT':sid,'REVIEW_CONFIRM_ACTUAL':fixture['actualTarget'],'REVIEW_CONFIRM_ORIGINAL':fixture['originalTarget']}
                    subprocess.run(['npx','--prefix',str(root/'src/web'),'playwright','test','--config',str(root/'src/web/playwright.config.ts'),str(root/'src/web/tests/review-target-confirmation.spec.ts')],cwd=root,env=browser_env,check=True)
    finally:
        if child is not None:
            child.terminate()
            try:child.wait(timeout=10)
            except subprocess.TimeoutExpired:child.kill();child.wait()
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
