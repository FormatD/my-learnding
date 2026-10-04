#!/usr/bin/env python3
"""Supervise this project's existing private cluster and compiled loopback API."""
import argparse,fcntl,hashlib,json,os,re,signal,socket,subprocess,threading,time,urllib.request,stat
from datetime import datetime,timezone
from pathlib import Path

class RuntimeErrorCode(Exception):pass

def private(path,flags):
    fd=os.open(path,flags|os.O_NOFOLLOW,0o600);os.fchmod(fd,0o600);return fd

def load(path):
    path=Path(path).absolute()
    if path.is_symlink():raise RuntimeErrorCode('CONFIG_SYMLINK')
    if not stat.S_ISREG(path.stat().st_mode):raise RuntimeErrorCode('CONFIG_INVALID')
    if path.stat().st_uid!=os.getuid() or path.stat().st_mode&0o077:raise RuntimeErrorCode('CONFIG_NOT_PRIVATE')
    c=json.loads(path.read_text());required={'root','dotnet','postgresBin','dataDir','databasePort','database','user','apiPort','stateFile','backupConfig'}
    if set(c)!=required:raise RuntimeErrorCode('CONFIG_INVALID')
    for key in ['root','dotnet','postgresBin','dataDir','stateFile']:
        if not isinstance(c[key],str) or not Path(c[key]).is_absolute():raise RuntimeErrorCode('CONFIG_INVALID')
    root=Path(c['root']).resolve();data=Path(c['dataDir'])
    if data.is_symlink() or data.stat().st_uid!=os.getuid():raise RuntimeErrorCode('CLUSTER_OWNER_INVALID')
    if data.is_symlink() or (data/'PG_VERSION').read_text().strip()!='16':raise RuntimeErrorCode('CLUSTER_VERSION_INVALID')
    for key in ['database','user']:
        if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*',c[key]):raise RuntimeErrorCode('CONFIG_INVALID')
    if any(type(c[k])!=int or not 1024<=c[k]<=65535 for k in ['databasePort','apiPort']) or c['databasePort']==c['apiPort']:raise RuntimeErrorCode('CONFIG_INVALID')
    c['dll']=str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll');c['cwd']=str(root/'src/server');c['url']='http://127.0.0.1:'+str(c['apiPort'])
    if not Path(c['dll']).is_file() or not (root/'src/server/wwwroot/index.html').is_file() or not os.access(c['dotnet'],os.X_OK):raise RuntimeErrorCode('BUILD_UNAVAILABLE')
    for tool in ['psql','pg_ctl']:
        if not os.access(Path(c['postgresBin'])/tool,os.X_OK):raise RuntimeErrorCode('POSTGRES_TOOLS_UNAVAILABLE')
    if c['backupConfig'] is not None and not Path(c['backupConfig']).is_file():raise RuntimeErrorCode('BACKUP_CONFIG_UNAVAILABLE')
    parent=Path(c['stateFile']).parent
    if parent.is_symlink():raise RuntimeErrorCode('STATE_DIRECTORY_INVALID')
    parent.mkdir(parents=True,exist_ok=True,mode=0o700)
    if parent.stat().st_uid!=os.getuid() or parent.stat().st_mode&0o077:raise RuntimeErrorCode('STATE_DIRECTORY_NOT_PRIVATE')
    if Path(c['stateFile']).is_symlink():raise RuntimeErrorCode('STATE_FILE_INVALID')
    c['hash']=hashlib.sha256(path.read_bytes()).hexdigest();return c

def process(pid):
    try:
        stamp=subprocess.check_output(['ps','-p',str(pid),'-o','lstart='],text=True,env={**os.environ,'LC_ALL':'C'}).strip()
        command=subprocess.check_output(['ps','-p',str(pid),'-o','command='],text=True).strip()
        uid=subprocess.check_output(['ps','-p',str(pid),'-o','uid='],text=True).strip()
        return {'pid':pid,'start':stamp,'command':command,'uid':int(uid)} if stamp else None
    except (subprocess.CalledProcessError,ValueError):return None

def occupied(port):
    with socket.socket() as s:s.settimeout(.2);return s.connect_ex(('127.0.0.1',port))==0

def owns_listener(pid,port):
    result=subprocess.run(['lsof','-nP','-a','-p',str(pid),'-iTCP:'+str(port),'-sTCP:LISTEN','-t'],capture_output=True,text=True)
    return result.returncode==0 and str(pid) in result.stdout.split()

def stop_owned(identity):
    if identity is None or process(identity['pid'])!=identity or identity['uid']!=os.getuid():return
    try:os.kill(identity['pid'],signal.SIGTERM)
    except ProcessLookupError:return
    end=time.monotonic()+10
    while time.monotonic()<end and process(identity['pid'])==identity:time.sleep(.1)
    if process(identity['pid'])==identity:
        try:os.kill(identity['pid'],signal.SIGKILL)
        except ProcessLookupError:pass

def database(c):
    def query():
        return subprocess.run([str(Path(c['postgresBin'])/'psql'),'-h','127.0.0.1','-p',str(c['databasePort']),'-U',c['user'],'-d','postgres','-At','-v','ON_ERROR_STOP=1','-c',"SELECT current_setting('data_directory'),current_setting('port'),pg_postmaster_start_time()"],capture_output=True,text=True,timeout=4,env={**os.environ,'PGCONNECT_TIMEOUT':'2'})
    if not occupied(c['databasePort']):
        # Never initialize, replace or delete a cluster on login/retry.
        os.close(private(Path(c['stateFile']).parent/'runtime-postgres.log',os.O_WRONLY|os.O_CREAT|os.O_APPEND))
        result=subprocess.run([str(Path(c['postgresBin'])/'pg_ctl'),'-D',c['dataDir'],'-l',str(Path(c['stateFile']).parent/'runtime-postgres.log'),'-o','-h 127.0.0.1 -p '+str(c['databasePort']),'start','-w','-t','20'],capture_output=True,text=True,timeout=25)
        if result.returncode:raise RuntimeErrorCode('DATABASE_START_FAILED')
    result=query()
    if result.returncode:raise RuntimeErrorCode('DATABASE_UNAVAILABLE')
    fields=result.stdout.strip().split('|')
    if len(fields)!=3 or Path(fields[0]).resolve()!=Path(c['dataDir']).resolve() or fields[1]!=str(c['databasePort']):raise RuntimeErrorCode('DATABASE_IDENTITY_CONFLICT')
    return fields[2]

def write_state(c,value):
    target=Path(c['stateFile']);pending=target.with_suffix('.pending');value={**value,'format':'learning-runtime/1','checkedAt':datetime.now(timezone.utc).isoformat(),'supervisorPid':os.getpid(),'configHash':c['hash']}
    fd=private(pending,os.O_WRONLY|os.O_CREAT|os.O_TRUNC)
    with os.fdopen(fd,'w') as output:json.dump(value,output);output.flush();os.fsync(output.fileno())
    os.replace(pending,target)

def run(c):
    lock=private(Path(c['stateFile']).with_suffix('.lock'),os.O_RDWR|os.O_CREAT)
    try:fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
    except BlockingIOError:os.close(lock);raise RuntimeErrorCode('RUNTIME_ALREADY_RUNNING') from None
    stopping=threading.Event()
    for sig in [signal.SIGTERM,signal.SIGINT]:signal.signal(sig,lambda *_:stopping.set())
    previous={}
    if Path(c['stateFile']).exists():
        with os.fdopen(private(c['stateFile'],os.O_RDONLY)) as saved:previous=json.load(saved)
    command=[c['dotnet'],c['dll'],'--urls',c['url']];identity=previous.get('apiProcess');child=None;restarts=0;error=None;database_start=None
    if identity and process(identity.get('pid'))==identity:
        if previous.get('configHash')!=c['hash'] or identity.get('uid')!=os.getuid() or identity.get('command')!=' '.join(command):raise RuntimeErrorCode('RUNTIME_OWNER_CONFLICT')
        stop_owned(identity)
    app_log=Path(c['stateFile']).parent/'runtime-api.log';log=os.fdopen(private(app_log,os.O_WRONLY|os.O_CREAT|os.O_APPEND),'a')
    env={**os.environ,'DOTNET_ROOT':str(Path(c['dotnet']).parent),'DOTNET_CLI_TELEMETRY_OPTOUT':'1','ConnectionStrings__Learning':f"Host=127.0.0.1;Port={c['databasePort']};Database={c['database']};Username={c['user']}"}
    env.pop('BackupConfigFile',None)
    if c['backupConfig']:env['BackupConfigFile']=c['backupConfig']
    try:
        while not stopping.is_set():
            try:
                current_database_start=database(c)
                if database_start is not None and database_start!=current_database_start:
                    stop_owned(identity)
                    if child is not None:child.wait(timeout=3)
                    child=None;identity=None
                database_start=current_database_start
                if child is None or child.poll() is not None:
                    if occupied(c['apiPort']):raise RuntimeErrorCode('API_PORT_OCCUPIED')
                    child=subprocess.Popen(command,cwd=c['cwd'],env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True);identity=process(child.pid);restarts+=1;started=time.monotonic()
                    if identity is None:raise RuntimeErrorCode('API_START_FAILED')
                    write_state(c,{'status':'Starting','apiProcess':identity,'starts':restarts})
                healthy=False
                if owns_listener(child.pid,c['apiPort']):
                    try:
                        with urllib.request.urlopen(c['url']+'/api/health',timeout=2) as response:healthy=response.status==200 and json.load(response).get('status')=='ok'
                    except (OSError,ValueError):pass
                if not healthy and time.monotonic()-started>60:raise RuntimeErrorCode('API_START_TIMEOUT')
                error=None;write_state(c,{'status':'Healthy' if healthy else 'Starting','apiProcess':identity,'starts':restarts})
                stopping.wait(2)
            except (RuntimeErrorCode,OSError,ValueError,subprocess.SubprocessError) as ex:
                error=str(ex) if isinstance(ex,RuntimeErrorCode) else 'RUNTIME_CHECK_FAILED'
                stop_owned(identity)
                if child is not None:
                    try:child.wait(timeout=3)
                    except subprocess.TimeoutExpired:pass
                child=None;identity=None;write_state(c,{'status':'Unavailable','errorCode':error,'apiProcess':None,'starts':restarts});stopping.wait(5)
    finally:
        stop_owned(identity)
        if child is not None:
            try:child.wait(timeout=3)
            except subprocess.TimeoutExpired:pass
        write_state(c,{'status':'Stopped','apiProcess':None,'starts':restarts,'errorCode':error});log.close();os.close(lock)

def main():
    os.umask(0o077);parser=argparse.ArgumentParser();parser.add_argument('--config',required=True);args=parser.parse_args()
    try:run(load(args.config))
    except (RuntimeErrorCode,OSError,ValueError,KeyError,subprocess.SubprocessError) as ex:
        print(json.dumps({'status':'Unavailable','errorCode':str(ex) if isinstance(ex,RuntimeErrorCode) else 'RUNTIME_CONFIGURATION_FAILED'}));raise SystemExit(1)
if __name__=='__main__':main()
