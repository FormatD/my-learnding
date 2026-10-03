"""Real queued/cancelled rebuild HTTP routes require explicit job controls."""
import json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parent.parent;database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);sdk=dotnet(root)
    env.update(REBUILD_COMPAT_USER='rebuild-compat-'+uuid.uuid4().hex[:12],REBUILD_COMPAT_PASSWORD=secrets.token_hex(24),ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION'])
    env.pop('BackupConfigFile',None);subprocess.run(['createdb',database],env=env,check=True);service=None
    try:
        with tempfile.TemporaryDirectory(prefix='learning-rebuild-compat-') as tmp:
            env.update(DeletionLedger=str(Path(tmp)/'students'),FamilyDeletionLedger=str(Path(tmp)/'families'),ExportDirectory=str(Path(tmp)/'exports'))
            fixture=json.loads(subprocess.check_output([sdk,str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'rebuild-compat-api-seed'],env=env,text=True).strip().splitlines()[-1])
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            origin=f'http://127.0.0.1:{port}'
            with open(Path(tmp)/'api.log','w+') as log:
                service=subprocess.Popen([sdk,str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT)
                until=time.monotonic()+40
                while time.monotonic()<until:
                    assert service.poll() is None,'Disposable API stopped'
                    try:
                        with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('Disposable API did not start')
                api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/login',{'userName':fixture['userName'],'password':env['REBUILD_COMPAT_PASSWORD']});c.request('/me');path='/students/'+fixture['studentId'];rid=fixture['requestId'];jid=fixture['jobId']
                def export():
                    data=c.request(path+'/export');data.pop('exportedAt');return data
                queued=c.request(path+'/rebuild-requests/'+rid);assert queued['status']=='Queued' and queued['attemptCount']==0 and queued['result'] is None
                # Mandatory answer synchronization may independently finish; the optional request stays queued.
                for _ in range(100):
                    pending=c.request(path+'/assessment-consumption')
                    if not pending['pendingModern'] and not pending['pendingLegacy']:break
                    time.sleep(.1)
                else:raise AssertionError('Required results did not settle')
                before=export();conflict=c.request(path+':rebuild',{},expected=409);assert conflict['code']=='REBUILD_JOB_REQUIRED';assert export()==before and c.request(path+'/rebuild-requests/'+rid)==queued
                canceled=c.request('/background-jobs/'+jid+':cancel',{'reason':'明确取消尚未开始的可选重建'});assert canceled['status']=='Cancelled' and canceled['executionStopConfirmed']
                before=export();assert c.request(path+':rebuild',{},expected=409)['code']=='REBUILD_JOB_REQUIRED';assert export()==before
                assert c.request(path+'/rebuild-requests/'+rid)['status']=='Cancelled'
                fresh=c.request(path+'/mastery:rebuild',{'reason':'取消后明确核对并重新准备'},expected=202);assert fresh['id']!=rid
                for _ in range(100):
                    actual=c.request(path+'/rebuild-requests/'+fresh['id'])
                    if actual['status']=='Succeeded':break
                    assert actual['status'] not in ('Failed','Cancelled'),actual;time.sleep(.1)
                else:raise AssertionError('Explicit new request did not settle')
                before=export();reused=c.request(path+':rebuild',{});assert reused['generation']==actual['result']['generationId'];assert export()==before and c.request(path+'/rebuild-requests/'+rid)['status']=='Cancelled'
                print('PASS 实际HTTP排队与真实取消后旧重建均409、导出原字节保持；明确新请求202经后台成功后同输入200复用，原取消记录保留')
                other=Client();other.request('/auth/register',{'userName':'rebuild-isolation-'+uuid.uuid4().hex,'password':secrets.token_hex(24)},expected=201);other.request('/me');other.request(path+':rebuild',{},expected=404)
                c.request(path+'/child-sessions',{});c.request(path+':rebuild',{},expected=403)
                print('PASS 旧重建入口保持跨家庭404、孩子403，不借兼容读取他人任务或计算结果')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
