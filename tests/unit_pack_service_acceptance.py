"""Check the actual pack API against preserved original fields in an isolated service."""
import os,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance,unit_pack_acceptance
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);env['ConnectionStrings__Learning']=env['PERSISTENCE_TEST_CONNECTION'];env.pop('BackupConfigFile',None);service=None
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        with tempfile.TemporaryDirectory(prefix='unit-pack-service-') as directory:
            work=Path(directory);env.update(DeletionLedger=str(work/'students'),FamilyDeletionLedger=str(work/'families'),ExportDirectory=str(work/'exports'))
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            origin=f'http://127.0.0.1:{port}'
            with (work/'api.log').open('w+') as log:
                service=subprocess.Popen([dotnet(root),str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT);deadline=time.monotonic()+40
                while time.monotonic()<deadline:
                    assert service.poll() is None,'Disposable pack service stopped'
                    try:
                        with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('Pack service did not become healthy')
                api_acceptance.BASE=origin+'/api/v1';unit_pack_acceptance.main()
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
