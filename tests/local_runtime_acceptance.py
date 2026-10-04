"""Real disposable PG16 cluster and compiled API process recovery; never stops main services."""
import json,os,secrets,signal,socket,subprocess,sys,tempfile,time,uuid
from pathlib import Path
import api_acceptance
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'))
import local_runtime as runtime
import install_runtime_agent as installer
PG=Path(os.environ.get('RUNTIME_POSTGRES_BIN','/opt/homebrew/opt/postgresql@16/bin'))

def freeport():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
def wait(test,label,limit=55):
    end=time.monotonic()+limit
    while time.monotonic()<end:
        result=test()
        if result:return result
        time.sleep(.2)
    raise AssertionError(label)
def main():
    runner=None;foreign=None;config=None
    with tempfile.TemporaryDirectory(prefix='learning-runtime-') as temp:
        work=Path(temp);os.chmod(work,0o700);data=work/'postgres';state=work/'runtime-state.json';path=work/'config.json';user=subprocess.check_output(['id','-un'],text=True).strip()
        subprocess.run([str(PG/'initdb'),'-D',str(data),'-A','trust','--no-locale','-E','UTF8'],check=True,stdout=subprocess.DEVNULL)
        config={'root':str(root),'dotnet':str(root/'.tools/dotnet/dotnet'),'postgresBin':str(PG),'dataDir':str(data),'databasePort':freeport(),'database':'learning','user':user,'apiPort':freeport(),'stateFile':str(state),'backupConfig':None}
        path.write_text(json.dumps(config));path.chmod(0o600)
        # Prepare the DB explicitly in the test; the runtime never creates or replaces databases.
        subprocess.run([str(PG/'pg_ctl'),'-D',str(data),'-l',str(work/'initial-pg.log'),'-o','-h 127.0.0.1 -p '+str(config['databasePort']),'start','-w'],check=True,stdout=subprocess.DEVNULL)
        subprocess.run([str(PG/'createdb'),'-h','127.0.0.1','-p',str(config['databasePort']),'-U',user,'learning'],check=True)
        def stopdb():subprocess.run([str(PG/'pg_ctl'),'-D',str(data),'stop','-m','fast','-w'],check=True,stdout=subprocess.DEVNULL)
        stopdb()
        env={**os.environ,'DeletionLedger':str(work/'students'),'FamilyDeletionLedger':str(work/'families'),'ExportDirectory':str(work/'exports')}
        log=(work/'supervisor.log').open('w')
        def start():return subprocess.Popen([sys.executable,str(root/'scripts/local_runtime.py'),'--config',str(path)],env=env,stdout=log,stderr=subprocess.STDOUT)
        def current():
            try:return json.loads(state.read_text())
            except (OSError,ValueError):return {}
        def healthy(exclude=None):
            s=current();identity=s.get('apiProcess')
            return identity if s.get('status')=='Healthy' and identity and identity['pid']!=exclude and runtime.process(identity['pid'])==identity else None
        try:
            runner=start();first=wait(healthy,'Initial startup');runtime.database(runtime.load(path))
            api_acceptance.BASE='http://127.0.0.1:'+str(config['apiPort'])+'/api/v1';client=api_acceptance.Client();name='runtime-'+uuid.uuid4().hex[:12];password=secrets.token_hex(24)
            client.request('/auth/register',{'userName':name,'password':password},expected=201);client.request('/me');student=client.request('/students',{'name':'恢复验收学生'},expected=201)
            def retained():
                client.request('/me');assert any(s['id']==student['id'] for s in client.request('/students'))
            retained();print('PASS stopped owned PG16 cluster starts; actual API migration/login/student persisted',flush=True)
            duplicate=subprocess.run([sys.executable,str(root/'scripts/local_runtime.py'),'--config',str(path)],env=env,capture_output=True,text=True,timeout=8)
            assert duplicate.returncode==1 and 'RUNTIME_ALREADY_RUNNING' in duplicate.stdout and healthy()['pid']==first['pid']
            os.kill(first['pid'],signal.SIGKILL);second=wait(lambda:healthy(first['pid']),'API recovery');retained();print('PASS duplicate supervisor refuses; killed API restarts with persisted student',flush=True)
            before_db=healthy();oldpg=int((data/'postmaster.pid').read_text().splitlines()[0]);stopdb();wait(lambda:runtime.occupied(config['databasePort']),'PG restart');wait(lambda:(data/'postmaster.pid').exists() and int((data/'postmaster.pid').read_text().splitlines()[0])!=oldpg,'New PG process');wait(lambda:healthy(before_db['pid']),'API after DB recovery');retained();print('PASS stopped database restarts with actual data retained',flush=True)
            orphan=healthy();runner.kill();runner.wait(timeout=5);assert runtime.process(orphan['pid'])==orphan
            runner=start();third=wait(lambda:healthy(orphan['pid']),'Supervisor orphan recovery');retained();assert runtime.process(orphan['pid'])!=orphan;print('PASS killed supervisor restart reclaims only recorded orphan API',flush=True)
            runner.terminate();runner.wait(timeout=15);assert current()['status']=='Stopped' and runtime.process(third['pid'])!=third and runtime.occupied(config['databasePort'])
            config['apiPort']=freeport();path.write_text(json.dumps(config));foreign=socket.socket();foreign.bind(('127.0.0.1',config['apiPort']));foreign.listen();runner=start();wait(lambda:current().get('errorCode')=='API_PORT_OCCUPIED','Foreign API refusal');assert foreign.fileno()!=-1 and runner.poll() is None
            runner.terminate();runner.wait(timeout=15);foreign.close();foreign=None;print('PASS graceful stop retains PG; foreign API listener is refused',flush=True)
            wrong=work/'wrong';wrong.mkdir(mode=0o700);(wrong/'PG_VERSION').write_text('16');changed={**config,'dataDir':str(wrong)};path.write_text(json.dumps(changed));runner=start();wait(lambda:current().get('errorCode')=='DATABASE_IDENTITY_CONFLICT','Foreign DB refusal');assert runtime.occupied(config['databasePort']);runner.terminate();runner.wait(timeout=15)
            path.write_text(json.dumps(config));path.chmod(0o644)
            try:runtime.load(path);raise AssertionError('Public configuration accepted')
            except runtime.RuntimeErrorCode as ex:assert str(ex)=='CONFIG_NOT_PRIVATE'
            path.chmod(0o600);link=work/'link.json';link.symlink_to(path)
            try:runtime.load(link);raise AssertionError('Symlink accepted')
            except runtime.RuntimeErrorCode as ex:assert str(ex)=='CONFIG_SYMLINK'
            value=installer.manifest(path);target=work/'foreign.plist';target.write_bytes(__import__('plistlib').dumps({'Label':'unrelated'}))
            try:installer.write(target,value);raise AssertionError('Foreign manifest overwritten')
            except runtime.RuntimeErrorCode as ex:assert str(ex)=='AGENT_TARGET_OCCUPIED'
            print('PASS foreign DB identity, public/symlink config and unrelated manifest refused',flush=True)
        finally:
            if runner and runner.poll() is None:runner.terminate();runner.wait(timeout=20)
            s=current();runtime.stop_owned(s.get('apiProcess'))
            if foreign:foreign.close()
            if (data/'postmaster.pid').exists():stopdb()
            log.close()
if __name__=='__main__':main()
