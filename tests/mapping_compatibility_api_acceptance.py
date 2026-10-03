"""Scheduled real mapping jobs cannot be bypassed through the legacy HTTP route."""
import json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet
def main():
    root=Path(__file__).resolve().parent.parent;database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);sdk=dotnet(root)
    env.update(MAPPING_COMPAT_USER='mapping-compat-'+uuid.uuid4().hex[:12],MAPPING_COMPAT_PASSWORD=secrets.token_hex(24),ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION'])
    env.pop('BackupConfigFile',None);subprocess.run(['createdb',database],env=env,check=True);service=None
    try:
        with tempfile.TemporaryDirectory(prefix='learning-mapping-compat-') as tmp:
            env.update(DeletionLedger=str(Path(tmp)/'students'),FamilyDeletionLedger=str(Path(tmp)/'families'),ExportDirectory=str(Path(tmp)/'exports'))
            seeded=subprocess.check_output([sdk,str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'mapping-compat-api-seed'],env=env,text=True);fixtures=json.loads(seeded.strip().splitlines()[-1])
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
                api_acceptance.BASE=origin+'/api/v1'
                for fixture in fixtures:
                    c=Client();c.request('/auth/login',{'userName':fixture['userName'],'password':env['MAPPING_COMPAT_PASSWORD']});c.request('/me');pid=fixture['id'];jid=fixture['jobId'];request=fixture['input']
                    queued=c.request('/builder/mapping-preparations/'+pid);assert queued['status']=='Queued' and queued['attemptCount']==0
                    conflict=c.request('/builder/mapping-runs',request,expected=409);assert conflict['code']=='MAPPING_JOB_REQUIRED' and not c.request('/builder/mapping-runs');assert c.request('/builder/mapping-preparations/'+pid)==queued
                    canceled=c.request('/background-jobs/'+jid+':cancel',{'reason':'真实HTTP取消延后执行的原准备请求'});assert canceled['status']=='Cancelled' and canceled['executionStopConfirmed']
                    conflict=c.request('/builder/mapping-runs',request,expected=409);assert conflict['code']=='MAPPING_JOB_REQUIRED' and not c.request('/builder/mapping-runs');stopped=c.request('/builder/mapping-preparations/'+pid);assert stopped['status']=='Cancelled' and stopped['attemptCount']==0
                    c.request('/builder/mapping-preparations/'+pid+':retry',{'reason':'明确恢复原固定输入，经后台执行'},expected=202)
                    for _ in range(100):
                        state=c.request('/builder/mapping-preparations/'+pid)
                        if state['status']=='Succeeded':break
                        assert state['status'] not in ('Failed','Cancelled'),state;time.sleep(.1)
                    else:raise AssertionError('Explicit recovery did not settle')
                    assert state['runId']==fixture['runId'] and state['retryRound']==1
                    actual=c.request('/builder/mapping-runs/'+fixture['runId']);reused=c.request('/builder/mapping-runs',request);assert reused==actual['run'];assert c.request('/builder/mapping-runs/'+fixture['runId'])==actual
                    print('PASS '+request['provider']+' actual HTTP409 preserves queued work, real cancellation cannot be bypassed, explicit retry executes the original fixed run once and legacy reuse preserves completed result')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
