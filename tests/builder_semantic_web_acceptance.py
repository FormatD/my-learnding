"""Browser mapping regression against an automatically removed PostgreSQL database/API.
Uses existing installed Chromium; actual Mock/Manual review fixtures and controlled Local UI fixtures.
"""
import os,socket,subprocess,tempfile,time,uuid,urllib.request,json
from pathlib import Path
from persistence_environment import dotnet,environment
root=Path(__file__).resolve().parent.parent;name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name);child=None;created=False
if not env.get('CHROMIUM_PATH'):
    chrome=Path('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome')
    if chrome.is_file():env['CHROMIUM_PATH']=str(chrome)
try:
    with tempfile.TemporaryDirectory(prefix='learning-mapping-browser-') as directory:
        temp=Path(directory);env.update(ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION'],DeletionLedger=str(temp/'deleted-students'),FamilyDeletionLedger=str(temp/'deleted-families'),ExportDirectory=str(temp/'exports'));env.pop('BackupConfigFile',None)
        subprocess.run(['createdb',name],env=env,check=True);created=True
        with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
        origin=f'http://127.0.0.1:{port}';env['LEARNING_TEST_URL']=origin
        with (temp/'process.log').open('w+') as log:
            child=subprocess.Popen([dotnet(root),str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=root/'src/server',env=env,stdout=log,stderr=log)
            deadline=time.monotonic()+40
            while time.monotonic()<deadline:
                assert child.poll() is None,'temporary mapping browser API stopped'
                try:
                    with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                        if json.load(response)['status']=='ok':break
                except OSError:time.sleep(.1)
            else:raise AssertionError('temporary mapping browser API did not become ready')
            subprocess.run(['npm','run','test:e2e','--','tests/builder-semantic.spec.ts','--workers=1'],cwd=root/'src/web',env=env,check=True,timeout=300)
finally:
    if child is not None and child.poll() is None:child.terminate();child.wait(timeout=15)
    if created:subprocess.run(['dropdb','--force',name],env=env,check=True)
