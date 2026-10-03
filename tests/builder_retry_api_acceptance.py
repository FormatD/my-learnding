"""Bounded builder retries and manual frozen-input recovery, on an isolated database and API."""
import getpass,io,json,os,secrets,socket,subprocess,tempfile,time,urllib.request,uuid,zipfile
from pathlib import Path
import api_acceptance
from api_acceptance import Client

def main():
    root=Path(__file__).resolve().parent.parent;env=os.environ.copy();suffix=uuid.uuid4().hex[:12];database='learning_fault_'+suffix
    env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,PLAN_TEST_USERNAME='plan-fixture-'+suffix,PLAN_TEST_PASSWORD=secrets.token_hex(20))
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH'];connection=f'Host=127.0.0.1;Port=55432;Database={database};Username={env["PGUSER"]}';env['PERSISTENCE_TEST_CONNECTION']=connection;env['ConnectionStrings__Learning']=connection
    dotnet=str(root/'.tools/dotnet/dotnet');child=None
    subprocess.run(['createdb',database],env=env,check=True)
    with tempfile.TemporaryDirectory(prefix='learning-builder-retry-',dir='/private/tmp') as tmp:
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
                sid=fixture['studentId'];fid=fixture['familyId'];r1=fixture['releaseId'];original=next(r for r in c.request('/content')['releases'] if r['id']==r1);catalog=json.loads(original['payload'])
                def sql(query):return subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',query],env=env,text=True)
                def run_by_id(id):return next(r for r in c.request('/builder')['runs'] if r['id']==id)
                def publish(cat):
                    draft=c.request('/content/drafts',{'title':'保持原输入的重试验收','catalog':cat});c.request('/content/drafts/'+draft['id']+':review',{});preview=c.request('/content/drafts/'+draft['id']+'/preview');return c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':preview['hash']})
                source=c.request('/content/sources',{'title':'需要恢复的原输入','text':'先乘除后加减，独立核对先算哪一步。'},expected=201)
                sql(f'''UPDATE "Releases" SET "Payload"='invalid-json' WHERE "Id"='{r1}' AND "FamilyId"='{fid}';''');run=c.request('/builder/runs',{'sourceId':source['id']},expected=202);rid=run['id']
                # A valid newer library does not silently replace the frozen broken library.
                r2=publish(catalog);good_source=c.request('/content/sources',{'title':'其他可用输入','text':'同级运算从左到右，不包含小括号。'},expected=201);good=c.request('/builder/runs',{'sourceId':good_source['id']},expected=202)
                deadline=time.monotonic()+40
                while time.monotonic()<deadline:
                    records=c.request('/builder');failed=next(r for r in records['runs'] if r['id']==rid);healthy=next(r for r in records['runs'] if r['id']==good['id'])
                    if failed['status']=='Failed' and healthy['status']=='Completed':break
                    time.sleep(.2)
                else:raise AssertionError('bounded retries or other queue progress failed')
                attempts=[a for a in records['attempts'] if a['runId']==rid];assert len(attempts)==4 and [a['status'] for a in attempts]==['RetryScheduled']*3+['Failed'] and failed['retries']==3 and failed['nextAttemptAt'] is None and failed['error']=='BUILDER_PROCESSING_FAILED';assert not any(candidate['runId']==rid for candidate in records['candidates']) and len([candidate for candidate in records['candidates'] if candidate['runId']==good['id']])==1;assert all(json.loads(a['inputSnapshot'])['libraryReleaseId']==r1 for a in attempts)
                print('PASS 后台实际执行初次加3次退避重试后停止；其他任务继续，失败不留下候选，输入不会自动换成新版本')
                c.request('/builder/runs/'+rid+':retry',{'reason':''},expected=422)
                other=Client();other.request('/auth/register',{'userName':'builder-other-'+suffix,'password':env['PLAN_TEST_PASSWORD']},expected=201);other.request('/me');other.request('/builder/runs/'+rid+':retry',{'reason':'其他家庭不能处理'},expected=404)
                c.request('/me');c.request('/family/members',{'userName':'builder-parent-'+suffix,'password':env['PLAN_TEST_PASSWORD'],'roles':['Parent']},expected=201);parent=Client();parent.request('/auth/login',{'userName':'builder-parent-'+suffix,'password':env['PLAN_TEST_PASSWORD']});parent.request('/me');parent.request('/builder/runs/'+rid+':retry',{'reason':'只有家长无内容权限'},expected=403)
                c.request('/me');sql(f'''UPDATE "BuilderRuns" SET "Provider"='ExternalTest' WHERE "Id"='{rid}';''');c.request('/builder/runs/'+rid+':retry',{'reason':'不能借重试发送未授权内容'},expected=422);sql(f'''UPDATE "BuilderRuns" SET "Provider"='Mock' WHERE "Id"='{rid}';''')
                repaired=original['payload'].replace("'","''");sql(f'''UPDATE "Releases" SET "Payload"='{repaired}' WHERE "Id"='{r1}';''')
                updated=json.loads(original['payload']);updated['kcs']=[{**k,'name':k['name']+'（新名称）','revisionId':str(uuid.uuid4())} for k in updated['kcs']];publish(updated)
                key=str(uuid.uuid4());reason={'reason':'隔离验收已恢复原内容快照，重新处理原输入。'};
                if env.get('RUN_BUILDER_BROWSER')=='1':
                    browser_env=env.copy();result_file=Path(tmp)/'browser-result.json';browser_env.update(LEARNING_TEST_URL=f'http://127.0.0.1:{port}',BUILDER_TEST_USER=credentials['userName'],BUILDER_TEST_PASSWORD=credentials['password'],BUILDER_TEST_RUN=rid,BUILDER_BROWSER_RESULT=str(result_file));subprocess.run(['npm','run','test:e2e','--prefix','src/web','--','--grep','隔离建库失败恢复'],cwd=root,env=browser_env,check=True);browser_result=json.loads(result_file.read_text());key=browser_result['key'];reason=browser_result['body'];response=browser_result['response']
                else:response=c.request('/builder/runs/'+rid+':retry',reason,key=key,expected=202)
                if env.get('RUN_BUILDER_BROWSER')=='1':
                    request=urllib.request.Request(api_acceptance.BASE+'/builder/runs/'+rid+':retry',data=browser_result['rawBody'].encode(),method='POST',headers={'Content-Type':'application/json','X-Learning-Request':'1','Idempotency-Key':key,'If-Match':c.etag})
                    with c.http.open(request) as replay:assert replay.status==202;repeat=json.loads(replay.read())
                else:repeat=c.request('/builder/runs/'+rid+':retry',reason,key=key,expected=202)
                assert response==repeat and response['retryRound']==1 and response['libraryReleaseId']==r1 and response['inputHash']==run['inputHash']
                deadline=time.monotonic()+15
                while time.monotonic()<deadline:
                    recovered=c.request('/builder');row=next(r for r in recovered['runs'] if r['id']==rid)
                    if row['status']=='Completed':break
                    time.sleep(.1)
                else:raise AssertionError('manual retry did not complete')
                assert row['error'] is None and row['nextAttemptAt'] is None and row['retries']==0;history=[a for a in recovered['attempts'] if a['runId']==rid];assert len(history)==5 and history[-1]['retryRound']==1 and history[-1]['status']=='Completed'
                output=next(candidate for candidate in recovered['candidates'] if candidate['runId']==rid);matches=json.loads(output['matches']);assert matches and all(any(k['id']==m['kcId'] and k['name']==m['name'] for k in catalog['kcs']) for m in matches);assert len([candidate for candidate in recovered['candidates'] if candidate['runId']==rid])==1
                c.request('/builder/runs/'+rid+':retry',reason,expected=409)
                print('PASS 人工重试校验原因/权限/模型许可；幂等保留原输入和旧失败记录，恢复只生成一次候选，完成后不能再次生成')
                # Permanent missing-input failures are not requeued or fabricated.
                source3=c.request('/content/sources',{'title':'未知旧输入','text':'原输入缺失需要重新准备。'},expected=201);legacy=c.request('/builder/runs',{'sourceId':source3['id']},expected=202)
                sql(f'''UPDATE "BuilderRuns" SET "Status"='Failed',"InputVersion"='builder-input/1',"Error"='INPUT_SNAPSHOT_UNKNOWN' WHERE "Id"='{legacy['id']}';''');c.request('/builder/runs/'+legacy['id']+':retry',{'reason':'不能猜测补齐输入'},expected=422)
                with c.http.open(api_acceptance.BASE+'/family/export') as response:archive=zipfile.ZipFile(io.BytesIO(response.read()))
                manifest=json.loads(archive.read('manifest.json'));archive.close();exported=[a for a in manifest['data']['BuilderAttempt'] if a['runId']==rid];assert len(exported)==5 and all(json.loads(a['inputSnapshot'])['libraryReleaseId']==r1 for a in exported);assert any(call['runId']==rid and call['billingStatus']=='LocalNoCharge' for call in manifest['data']['BuilderCall'])
                tables=['BuilderCall','BuilderBudgetPolicy','BuilderBudgetReconciliation','BuilderRuns','BuilderAttempt','Candidates','Embedding','Sources','Chunks','Releases']
                query="SELECT json_build_object("+','.join("'"+name+"',(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY t.\"Id\"),'[]'::jsonb) FROM \""+name+"\" t WHERE t.\"FamilyId\"='"+fid+"')" for name in tables)+")"
                def rows(environment):return json.loads(subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',query],env=environment,text=True))
                original_rows=rows(env);backup_env=env.copy();backup_env['PATH']='/opt/homebrew/bin:'+backup_env['PATH'];backup_env.update(BACKUP_PASSPHRASE=secrets.token_hex(32),BACKUP_DIR=str(Path(tmp)/'backups'),GNUPGHOME=str(Path(tmp)/'gpg'),DELETION_LEDGER=env['DeletionLedger'],FAMILY_DELETION_LEDGER=env['FamilyDeletionLedger']);Path(backup_env['GNUPGHOME']).mkdir(mode=0o700);Path(backup_env['DELETION_LEDGER']).touch();output=subprocess.check_output(['sh','scripts/backup.sh'],cwd=root,env=backup_env,text=True);backup=output.strip().split('Backup saved: ')[-1];restored='learning_restore_'+suffix;restore_env=backup_env.copy();restore_env['PGDATABASE']=restored;subprocess.run(['createdb',restored],env=restore_env,check=True)
                try:
                    subprocess.run(['sh','scripts/restore.sh',backup],cwd=root,env=restore_env,check=True,stdout=subprocess.DEVNULL);assert rows(restore_env)==original_rows
                finally:subprocess.run(['dropdb','--force',restored],env=restore_env,check=True)
                print('PASS 全家导出保留各轮失败/成功快照；真实加密恢复后建库来源、原库、候选与尝试记录逐行一致')
                c.request('/students/'+sid+'/child-sessions',{});c.request('/builder',expected=403);c.request('/builder/runs/'+rid+':retry',reason,expected=403)
                print('PASS 未记录的旧输入明确拒绝直接重试，孩子不能读取建库记录或执行恢复')
                c.request('/auth/login',credentials);c.request('/me');preview=c.request('/family/delete-preview');c.request('/family:delete',{'previewHash':preview['previewHash'],'password':credentials['password'],'confirm':'永久删除家庭'});subprocess.run(['createdb',restored],env=restore_env,check=True)
                try:
                    subprocess.run(['sh','scripts/restore.sh',backup],cwd=root,env=restore_env,check=True,stdout=subprocess.DEVNULL);assert all(value==[] for value in rows(restore_env).values())
                finally:subprocess.run(['dropdb','--force',restored],env=restore_env,check=True)
                assert all(value==[] for value in rows(env).values());other.request('/me')
                print('PASS 删除家庭后原始输入、候选与重试历史清除；恢复旧加密备份不复活，其他家庭保留')

        finally:
            if child is not None and child.poll() is None:child.terminate();child.wait(timeout=10)
            subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
