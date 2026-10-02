"""Manual split/merge/redirect workflow and AT34, on an isolated database and API."""
import copy,getpass,io,json,os,secrets,socket,subprocess,tempfile,time,urllib.request,uuid,zipfile
from datetime import date,timedelta
from pathlib import Path
import api_acceptance
from api_acceptance import Client,TODAY

def main():
    root=Path(__file__).resolve().parent.parent;env=os.environ.copy();suffix=uuid.uuid4().hex[:12];database='learning_fault_'+suffix
    env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,PLAN_TEST_USERNAME='plan-fixture-'+suffix,PLAN_TEST_PASSWORD=secrets.token_hex(20))
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH'];connection=f'Host=127.0.0.1;Port=55432;Database={database};Username={env["PGUSER"]}';env['PERSISTENCE_TEST_CONNECTION']=connection;env['ConnectionStrings__Learning']=connection
    dotnet=str(root/'.tools/dotnet/dotnet');child=None
    subprocess.run(['createdb',database],env=env,check=True)
    with tempfile.TemporaryDirectory(prefix='learning-kc-changes-') as tmp:
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
                sid=fixture['studentId'];old_id=fixture['kcId'];r1=fixture['releaseId'];catalog=c.request('/students/'+sid+'/catalog')
                before=c.request('/students/'+sid+'/mastery');old_evidence=c.request('/students/'+sid+'/mastery/'+old_id);old_attempts=c.request('/students/'+sid+'/attempts');old_reviews=c.request('/students/'+sid+'/reviews')
                rev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});view=c.request('/students/'+sid+'/plans/'+TODAY);task=next(t for t in view['tasks'] if t['reasonCode']=='WRONG_DUE');c.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash'],'confirmWarnings':True});c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});old_session=c.request('/tasks/'+task['id']+'/sessions',{})
                def publish(cat):
                    draft=c.request('/content/drafts',{'title':'能力变更验收','catalog':cat});c.request('/content/drafts/'+draft['id']+':review',{});p=c.request('/content/drafts/'+draft['id']+'/preview');return c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':p['hash']})
                def node(name,behavior,boundary):return {'id':str(uuid.uuid4()),'revisionId':str(uuid.uuid4()),'code':'MATH.TEST.'+uuid.uuid4().hex,'name':name,'behavior':behavior,'boundary':boundary,'type':'Procedure'}
                def q(k,stem,answer):return {'id':str(uuid.uuid4()),'revisionId':str(uuid.uuid4()),'stem':stem,'answer':answer,'explanation':'先乘除后加减。','type':'Numeric','difficulty':'Medium','policy':'SingleKC','coverage':'Basic','mappings':[{'kcId':k['id'],'role':'Primary','share':1,'mode':'WholeItem'}]}
                def replace(cat,old_ids,nodes,questions):
                    result=copy.deepcopy(cat);result['kcs']=[k for k in result['kcs'] if k['id'] not in old_ids]+nodes
                    result['questions']=[question for question in result['questions'] if not any(m['kcId'] in old_ids for m in question['mappings'])]+questions
                    for lesson in result['lessons']:
                        if any(i in old_ids for i in lesson['kcIds']):lesson['kcIds']=[i for i in lesson['kcIds'] if i not in old_ids]+[n['id'] for n in nodes];lesson['revisionId']=str(uuid.uuid4())
                    for resource in result['resources']:resource['kcIds']=[i for i in resource['kcIds'] if i not in old_ids]+([n['id'] for n in nodes] if any(i in old_ids for i in resource['kcIds']) else [])
                    result['resources']=[r for r in result['resources'] if r['kcIds']];result['lessons']=[l for l in result['lessons'] if l['kcIds']]
                    result['relations']=[r for r in result['relations'] if r['from'] not in old_ids and r['to'] not in old_ids];return result
                a=node('先乘后加减','独立先算乘法再算加减','不含除法与括号');b=node('先除后加减','独立先算除法再算加减','不含乘法与括号');qa=q(a,'3 + 4 × 2 = ?','11');qb=q(b,'18 − 12 ÷ 3 = ?','14');split_catalog=replace(catalog,[old_id],[a,b],[qa,qb]);r2=publish(split_catalog)['id']
                payload={'proposalType':'Split','rationale':'原能力拆为先乘和先除，各自使用独立测量题。','fromReleaseId':r1,'effectiveReleaseId':r2,'fromKCIds':[old_id],'targets':[{'kcId':a['id'],'weight':.6},{'kcId':b['id'],'weight':.4}]}
                c.request('/content/kc-changes',{**payload,'targets':[{'kcId':a['id']}]},expected=422);c.request('/content/kc-changes',{**payload,'targets':[{'kcId':a['id'],'weight':.0000001},{'kcId':b['id']}]},expected=422)
                p=c.request('/content/kc-changes',payload,expected=201);pid=p['id'];assert p['status']=='Draft';assert c.request('/content/kc-changes')['migrations']==[]
                edited={**payload,'rationale':payload['rationale']+' 人工复核范围。'};c.request('/content/kc-changes/'+pid,edited,method='PUT');assert len(c.request('/content/kc-changes/'+pid)['events'])==2
                c.request('/content/kc-changes/'+pid+'/preview',expected=422);c.request('/content/kc-changes/'+pid+':apply',{'previewHash':'none','confirm':'记录能力变更'},expected=422);c.request('/content/kc-changes/'+pid+':submit',{});c.request('/content/kc-changes/'+pid,payload,method='PUT',expected=409)
                c.request('/content/kc-changes/'+pid+':decide',{'decision':'Approved','reason':'确认题目分别测量两个独立范围。'});preview=c.request('/content/kc-changes/'+pid+'/preview')
                c.request('/students',{'name':'验证旧预览失效'},expected=201);c.request('/content/kc-changes/'+pid+':apply',{'previewHash':preview['previewHash'],'confirm':'记录能力变更'},expected=412);preview=c.request('/content/kc-changes/'+pid+'/preview');c.request('/content/kc-changes/'+pid+':apply',{'previewHash':preview['previewHash'],'confirm':'错误文本'},expected=422)
                key=str(uuid.uuid4());command={'previewHash':preview['previewHash'],'confirm':'记录能力变更'};result=c.request('/content/kc-changes/'+pid+':apply',command,key=key);again=c.request('/content/kc-changes/'+pid+':apply',command,key=key);assert result==again
                history=c.request('/content/kc-changes/'+pid);assert history['proposal']['status']=='Applied' and len(history['migrations'])==2 and len(history['events'])==5;assert all(m['evidencePolicy']=='PreserveHistoryNoTransfer' and m['fromKCId']==old_id and m['effectiveReleaseId']==r2 for m in history['migrations']);assert sorted(m['weight'] for m in history['migrations'])==[.4,.6]
                identities=c.request('/content/kc-changes')['identities'];assert next(k for k in identities if k['id']==old_id)['status']=='Deprecated';assert all(next(k for k in identities if k['id']==i)['status']=='Active' for i in [a['id'],b['id']])
                assert c.request('/students/'+sid+'/mastery')==before and c.request('/students/'+sid+'/mastery/'+old_id)==old_evidence and c.request('/students/'+sid+'/attempts')==old_attempts and c.request('/students/'+sid+'/reviews')==old_reviews
                assert next(s for s in c.request('/students') if s['id']==sid)['activeReleaseId']==r1
                for target in [a,b]:assert c.request('/students/'+sid+'/mastery/'+target['id'])=={'mastery':None,'evidence':[]}
                c.request('/content/kc-changes/'+pid+':apply',command,expected=422)
                c.request('/content/kc-changes',payload,expected=422)
                draft=c.request('/content/drafts',{'title':'禁止重新发布停用身份','catalog':catalog});c.request('/content/drafts/'+draft['id']+':review',{});old_preview=c.request('/content/drafts/'+draft['id']+'/preview');c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':old_preview['hash']},expected=422)
                print('PASS AT34 人工拆分审核和确认；权重仅记录，旧证据/作答/复习不改、新能力未知、幂等不重复、旧能力不可重新发布')
                c.request('/students/'+sid+'/content/'+r2+':bind',{});resumed=c.request('/tasks/'+task['id']+'/sessions',{});assert resumed['sessionId']==old_session['sessionId'] and resumed['stem']==old_session['stem'] and resumed['releaseId']==r1
                rev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});new_task=c.request('/plans/'+rev['id']+'/tasks',{'title':'新能力独立诊断','minutes':3,'resourceRef':'独立测量先乘后加减','type':'Practice','questionId':qa['id']});view=c.request('/students/'+sid+'/plans/'+TODAY);c.request('/plans/'+rev['id']+':publish',{'previewHash':view['revision']['inputHash'],'confirmWarnings':True});c.request('/tasks/'+new_task['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+new_task['id']+'/sessions',{});c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'11'},expected=201)
                for _ in range(100):
                    current=c.request('/students/'+sid+'/mastery')
                    if current['pending']==0:break
                    time.sleep(.1)
                else:raise AssertionError('projection did not settle')
                independent=c.request('/students/'+sid+'/mastery/'+a['id']);assert (independent['mastery']['alpha'],independent['mastery']['beta'])==(3,2) and len(independent['evidence'])==1
                old_now=c.request('/students/'+sid+'/mastery/'+old_id);assert (old_now['mastery']['alpha'],old_now['mastery']['beta'])==(old_evidence['mastery']['alpha'],old_evidence['mastery']['beta'])
                assert c.request('/students/'+sid+'/mastery/'+b['id'])=={'mastery':None,'evidence':[]}
                print('PASS AT34 绑定新版本后原会话继续原题；独立诊断只创建目标能力证据，不分摊或改写旧概率')
                merged=node('合并后的运算优先级','独立先算乘除再算加减','不含小括号');qm=q(merged,'3 + 4 × 2 = ?','11');merge_catalog=replace(split_catalog,[a['id'],b['id']],[merged],[qm]);r3=publish(merge_catalog)['id']
                def record(kind,old,new,release_from,release_to):
                    p=c.request('/content/kc-changes',{'proposalType':kind,'rationale':'人工确认独立身份和测量范围，历史不转移。','fromReleaseId':release_from,'effectiveReleaseId':release_to,'fromKCIds':old,'targets':[{'kcId':i} for i in new]},expected=201);c.request('/content/kc-changes/'+p['id']+':submit',{});c.request('/content/kc-changes/'+p['id']+':decide',{'decision':'Approved','reason':'逐项复核能力和题目边界。'});v=c.request('/content/kc-changes/'+p['id']+'/preview');c.request('/content/kc-changes/'+p['id']+':apply',{'previewHash':v['previewHash'],'confirm':'记录能力变更'});return c.request('/content/kc-changes/'+p['id'])
                merge=record('Merge',[a['id'],b['id']],[merged['id']],r2,r3);assert len(merge['migrations'])==2 and all(m['toKCId']==merged['id'] for m in merge['migrations']);assert c.request('/students/'+sid+'/mastery/'+merged['id'])=={'mastery':None,'evidence':[]} and c.request('/students/'+sid+'/mastery/'+a['id'])==independent
                print('PASS 多来源合并记录两条不可转移关系，新身份无继承概率，来源能力自己的证据保留')
                replacement=node('整理后的优先级能力','独立先算乘除再算加减','不含小括号');qr=q(replacement,'18 − 12 ÷ 3 = ?','14');append=copy.deepcopy(merge_catalog);append['kcs'].append(replacement);append['questions'].append(qr);r4=publish(append)['id'];redirect_catalog=replace(append,[merged['id']],[],[]);r5=publish(redirect_catalog)['id'];redirect=record('Redirect',[merged['id']],[replacement['id']],r3,r5);assert len(redirect['migrations'])==1 and redirect['migrations'][0]['toKCId']==replacement['id']
                print('PASS 替换可指向已有独立身份，保留自身状态，仍须人工审核与明确确认')
                reject_payload={**payload,'fromKCIds':[replacement['id']],'fromReleaseId':r4,'effectiveReleaseId':r5,'targets':[{'kcId':replacement['id']}]};c.request('/content/kc-changes',reject_payload,expected=422)
                # A submitted alternate redirect can be explicitly rejected without changing identities.
                extra=node('待拒绝候选','独立先算乘除再算加减','不含括号');qe=q(extra,'3 + 4 × 2 = ?','11');extra_catalog=replace(redirect_catalog,[replacement['id']],[extra],[qe]);r6=publish(extra_catalog)['id'];reject=c.request('/content/kc-changes',{'proposalType':'Redirect','rationale':'验证拒绝保留范围','fromReleaseId':r5,'effectiveReleaseId':r6,'fromKCIds':[replacement['id']],'targets':[{'kcId':extra['id']}]},expected=201);c.request('/content/kc-changes/'+reject['id']+':submit',{});c.request('/content/kc-changes/'+reject['id']+':decide',{'decision':'Rejected','reason':'暂不确认范围替换。'});assert next(k for k in c.request('/content/kc-changes')['identities'] if k['id']==replacement['id'])['status']=='Active';c.request('/content/kc-changes/'+reject['id']+'/preview',expected=422)
                owner_name=credentials['userName'];editor_name='change-editor-'+suffix;c.request('/family/members',{'userName':editor_name,'password':env['PLAN_TEST_PASSWORD'],'roles':['ContentEditor']},expected=201);editor=Client();editor.request('/auth/login',{'userName':editor_name,'password':env['PLAN_TEST_PASSWORD']});editor.request('/me');editor.request('/content/kc-changes/'+pid);editor.request('/content/kc-changes/'+pid+':decide',{'decision':'Approved','reason':'不能审核'},expected=403);editor.request('/content/kc-changes/'+pid+'/preview',expected=403)
                other=Client();other.request('/auth/register',{'userName':'change-other-'+suffix,'password':env['PLAN_TEST_PASSWORD']},expected=201);other.request('/me');other.request('/content/kc-changes/'+pid,expected=404);other.request('/content/kc-changes',payload,expected=404)
                exported=c.request('/students/'+sid+'/export');assert len(exported['knowledgeChanges']['proposals'])==3 and len(exported['knowledgeChanges']['migrations'])==5;assert all(p['status']=='Applied' for p in exported['knowledgeChanges']['proposals'])
                with c.http.open(api_acceptance.BASE+'/family/export') as response:archive=zipfile.ZipFile(io.BytesIO(response.read()))
                manifest=json.loads(archive.read('manifest.json'));assert len(manifest['data']['KCChangeProposal'])==4 and len(manifest['data']['KnowledgeMigration'])==5 and len(manifest['data']['KCProposalEvent'])==16;archive.close()
                print('PASS 学生导出包含已应用关系与审核历史；全家导出同时保留拒绝提案和全部变更事件')
                # Real encrypted round trip preserves new relationships and all old learning rows.
                tables=['KCChangeProposal','KCChangeProposalItem','KnowledgeMigration','KCProposalEvent','ContentIdentity','ContentRevision','Evidence','Masteries','Reviews','Attempts','Sessions','Tasks']
                query="SELECT json_build_object("+','.join("'"+name+"',(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY t.\"Id\"),'[]'::jsonb) FROM \""+name+"\" t WHERE t.\"FamilyId\"='"+fixture['familyId']+"')" for name in tables)+")"
                def rows(environment):return json.loads(subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',query],env=environment,text=True))
                original_rows=rows(env);backup_env=env.copy();backup_env['PATH']='/opt/homebrew/bin:'+backup_env['PATH'];backup_env.update(BACKUP_PASSPHRASE=secrets.token_hex(32),BACKUP_DIR=str(Path(tmp)/'backups'),GNUPGHOME=str(Path(tmp)/'gpg'),DELETION_LEDGER=env['DeletionLedger'],FAMILY_DELETION_LEDGER=env['FamilyDeletionLedger']);Path(backup_env['GNUPGHOME']).mkdir(mode=0o700);Path(backup_env['DELETION_LEDGER']).touch();backup_output=subprocess.check_output(['sh','scripts/backup.sh'],cwd=root,env=backup_env,text=True);backup=backup_output.strip().split('Backup saved: ')[-1]
                restored='learning_restore_'+suffix;restore_env=backup_env.copy();restore_env['PGDATABASE']=restored;subprocess.run(['createdb',restored],env=restore_env,check=True)
                try:
                    subprocess.run(['sh','scripts/restore.sh',backup],cwd=root,env=restore_env,check=True,stdout=subprocess.DEVNULL);assert rows(restore_env)==original_rows
                finally:subprocess.run(['dropdb','--force',restored],env=restore_env,check=True)
                print('PASS 真实加密备份恢复后，拆分/合并/替换关系、审核快照和历史学习记录逐行保持一致')
                c.request('/me');c.request('/students/'+sid+'/child-sessions',{});c.request('/content/kc-changes',expected=403);c.request('/content/kc-changes/'+pid+':apply',command,expected=403)
                print('PASS 拒绝不改变正式身份；编辑者无确认权限、孩子禁用、跨家庭提案与正式版本隔离')
                c.request('/auth/login',credentials);c.request('/me');delete_preview=c.request('/family/delete-preview');c.request('/family:delete',{'previewHash':delete_preview['previewHash'],'password':credentials['password'],'confirm':'永久删除家庭'})
                table_names=subprocess.check_output(['psql','-At','-c',"SELECT table_name FROM information_schema.columns WHERE table_schema='public' AND column_name='FamilyId' ORDER BY table_name"],env=env,text=True).splitlines()
                def family_counts(environment):
                    checks=["SELECT '"+name+"',count(*) FROM \""+name+"\" WHERE \"FamilyId\"='"+fixture['familyId']+"'" for name in table_names]
                    return subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',' UNION ALL '.join(checks)],env=environment,text=True).splitlines()
                assert all(line.endswith('|0') for line in family_counts(env))
                other.request('/me');assert other.request('/content/kc-changes')['proposals']==[]
                subprocess.run(['createdb',restored],env=restore_env,check=True)
                try:
                    subprocess.run(['sh','scripts/restore.sh',backup],cwd=root,env=restore_env,check=True,stdout=subprocess.DEVNULL);assert all(line.endswith('|0') for line in family_counts(restore_env))
                finally:subprocess.run(['dropdb','--force',restored],env=restore_env,check=True)
                print('PASS 全家删除及旧加密备份恢复后，全部家庭私有表零行，能力变更和审核历史不复活，其他家庭保留')

        finally:
            if child is not None and child.poll() is None:child.terminate();child.wait(timeout=10)
            subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
