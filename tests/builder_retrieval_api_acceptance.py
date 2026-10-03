"""Actually run frozen two-item and legacy jobs in a service configured for one item."""
import json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
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
                print('PASS 当前服务真实配置一项时，预先冻结两项任务实际后台仍返回两项，旧任务保留原片段方式；新请求真实使用一项且摘要不同，重复请求复用，原候选/配置字节不改')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
