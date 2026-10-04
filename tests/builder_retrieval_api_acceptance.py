"""Actually run frozen two-item and legacy jobs in a service configured for one item."""
import os,json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);sdk=dotnet(root)
    env.update(RETRIEVAL_USER='retrieval-'+uuid.uuid4().hex[:12],RETRIEVAL_PASSWORD=secrets.token_hex(24),Builder__RetrievalTopK='1',ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION']);env.pop('BackupConfigFile',None);subprocess.run(['createdb',database],env=env,check=True);service=None
    try:
        fixture=json.loads(subprocess.check_output([sdk,str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'builder-retrieval-api-seed'],env=env,text=True).strip().splitlines()[-1])
        with tempfile.TemporaryDirectory(prefix='builder-retrieval-') as temp:
            work=Path(temp);env.update(DeletionLedger=str(work/'students'),FamilyDeletionLedger=str(work/'families'),ExportDirectory=str(work/'exports'))
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            origin=f'http://127.0.0.1:{port}'
            with (work/'api.log').open('w+') as log:
                service=subprocess.Popen([sdk,str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT);deadline=time.monotonic()+40
                while time.monotonic()<deadline:
                    assert service.poll() is None,'Disposable service stopped'
                    try:
                        with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('No health response')
                api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/login',{'userName':fixture['userName'],'password':env['RETRIEVAL_PASSWORD']});c.request('/me')
                def settled(ids):
                    for _ in range(100):
                        data=c.request('/builder');runs=[r for r in data['runs'] if r['id'] in ids]
                        if len(runs)==len(ids) and all(r['status']=='Completed' for r in runs):return data
                        assert all(r['status']=='Queued' or r['status']=='Completed' for r in runs),runs;time.sleep(.1)
                    raise AssertionError('Jobs did not complete')
                state=settled([fixture['legacyId'],fixture['modernId']]);frozen=next(x for x in state['candidates'] if x['runId']==fixture['modernId']);old=next(x for x in state['candidates'] if x['runId']==fixture['legacyId'])
                assert len(json.loads(frozen['matches']))==2 and len(json.loads(old['matches']))==fixture['kcCount']
                assert next(r for r in state['runs'] if r['id']==fixture['modernId'])['modelConfigPayload']==fixture['modernPayload'] and next(r for r in state['runs'] if r['id']==fixture['legacyId'])['modelConfigPayload']==fixture['legacyPayload']
                fresh=c.request('/builder/runs',{'sourceId':fixture['sourceId']},expected=202);assert fresh['inputVersion']=='builder-input/4' and json.loads(fresh['modelConfigPayload'])['retrieval']['topK']==1 and fresh['id']!=fixture['modernId']
                final=settled([fresh['id']]);new=next(x for x in final['candidates'] if x['runId']==fresh['id']);assert len(json.loads(new['matches']))==1 and next(x for x in final['candidates'] if x['id']==frozen['id'])==frozen and next(x for x in final['candidates'] if x['id']==old['id'])==old
                assert c.request('/builder/runs',{'sourceId':fixture['sourceId']})['id']==fresh['id']
                context=c.request('/builder/candidates/'+new['id']+'/review-context');assert all(m['definition']['type']==context['protocol']['kcType'] for m in context['matches'])
                assert all(m['match']['retrievalEvidence']['keywordPolicy']==json.loads(fresh['modelConfigPayload'])['retrieval']['keywordPolicy'] for m in context['matches'])
                target=context['matches'][0]['match']['kcId']
                c.request('/builder/candidates/'+new['id']+':decide',{'decision':'LinkExisting','reason':'隔离测试经实际审核接口确认别名来源','existingKCId':target,'name':new['name']})
                aliased=c.request('/builder/runs',{'sourceId':fixture['sourceId']},expected=202);profile=json.loads(aliased['modelConfigPayload'])['retrieval'];assert profile['version']=='builder-retrieval/3' and len(profile['aliases'])==1 and profile['aliases'][0]['candidateId']==new['id'] and aliased['modelConfigHash']!=fresh['modelConfigHash'] and aliased['id']!=fresh['id']
                alias_state=settled([aliased['id']]);alias_candidate=next(x for x in alias_state['candidates'] if x['runId']==aliased['id']);hits=json.loads(alias_candidate['matches']);assert hits[0]['kcId']==target and hits[0]['matchedAliases']==profile['aliases']
                alias_context=c.request('/builder/candidates/'+alias_candidate['id']+'/review-context');assert alias_context['matches'][0]['match']['matchedAliases']==profile['aliases']
                assert next(r for r in alias_state['runs'] if r['id']==fresh['id'])['modelConfigPayload']==fresh['modelConfigPayload']
                if os.environ.get('RETRIEVAL_BROWSER'):
                    browser_env=env.copy();browser_env.update(LEARNING_TEST_URL=origin,BUILDER_ALIAS_USER=fixture['userName'],BUILDER_ALIAS_PASSWORD=env['RETRIEVAL_PASSWORD'],BUILDER_ALIAS_ID=alias_candidate['id'],BUILDER_ALIAS_NAME=alias_candidate['name'],BUILDER_ALIAS_SOURCE=new['id'])
                    subprocess.run(['npm','run','test:e2e','--','tests/builder-alias-review.spec.ts','--workers=1'],cwd=root/'src/web',env=browser_env,check=True)
                    # Explicit controlled compatibility/corruption fixtures, not historical program upgrades.
                    subprocess.run(['psql','-v','ON_ERROR_STOP=1','-c',f'UPDATE "Candidates" SET "ReviewReason"=\'\', "ReviewedBy"=NULL, "ReviewedAt"=NULL WHERE "Id"=\'{new["id"]}\''],env=env,check=True,stdout=subprocess.DEVNULL)
                    browser_env['BUILDER_ALIAS_CASE']='legacy';subprocess.run(['npm','run','test:e2e','--','tests/builder-alias-review.spec.ts','--workers=1'],cwd=root/'src/web',env=browser_env,check=True)
                    subprocess.run(['psql','-v','ON_ERROR_STOP=1','-c',f'UPDATE "Candidates" SET "ExistingKCId"=NULL WHERE "Id"=\'{new["id"]}\''],env=env,check=True,stdout=subprocess.DEVNULL)
                    browser_env['BUILDER_ALIAS_CASE']='mismatch';subprocess.run(['npm','run','test:e2e','--','tests/builder-alias-review.spec.ts','--workers=1'],cwd=root/'src/web',env=browser_env,check=True)

                print('PASS 实际人工审核接口创建别名，新请求固定来源和摘要，实际后台命中同类型别名，审核上下文保留来源；原请求不改')
                print('PASS 当前服务真实配置一项时，预先冻结两项任务实际后台仍返回两项，旧任务保留原片段方式；新请求真实使用一项且摘要不同，重复请求复用，原候选/配置字节不改')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
