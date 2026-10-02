"""Actual launchd lifecycle against a disposable backup database and ephemeral service label."""
import fcntl,getpass,json,os,plistlib,secrets,signal,subprocess,sys,tempfile,time,uuid
from pathlib import Path

def main():
    root=Path(__file__).resolve().parent.parent;sys.path.insert(0,str(root/'scripts'))
    import daily_backup,install_backup_agent
    suffix=uuid.uuid4().hex[:12];database='learning_fault_'+suffix;env=os.environ.copy()
    env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,
               PLAN_TEST_USERNAME='agent-fixture-'+suffix,PLAN_TEST_PASSWORD=secrets.token_hex(20),
               PERSISTENCE_TEST_CONNECTION=f'Host=127.0.0.1;Port=55432;Database={database};Username={getpass.getuser()}')
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:/opt/homebrew/bin:'+env['PATH'];os.environ['PATH']=env['PATH']
    label='local.learning.backup.test-'+suffix;domain='gui/'+str(os.getuid());service=domain+'/'+label;loaded=False
    subprocess.run(['createdb',database],env=env,check=True)
    with tempfile.TemporaryDirectory(prefix='learning-agent-',dir='/private/tmp') as tmp:
        tmp=Path(tmp)
        try:
            subprocess.run([str(root/'.tools/dotnet/dotnet'),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'plan-boundary-seed'],env=env,check=True,stdout=subprocess.DEVNULL)
            key=tmp/'key';key.write_text(secrets.token_hex(32));key.chmod(0o600)
            for name in ['students.txt','families.txt']:(tmp/name).touch(mode=0o600)
            config={'backupDir':str(tmp/'archives'),'stateFile':str(tmp/'state.json'),'passphraseFile':str(key),'dataDir':str(tmp),
                    'deletionLedger':str(tmp/'students.txt'),'familyDeletionLedger':str(tmp/'families.txt'),
                    'postgres':{'host':'127.0.0.1','port':55432,'user':getpass.getuser(),'database':database}}
            configfile=tmp/'config.json';configfile.write_text(json.dumps(config));configfile.chmod(0o600)
            value=install_backup_agent.manifest(root,configfile,label);plist=tmp/'agent.plist';install_backup_agent.write_plist(plist,value)
            assert value['RunAtLoad'] and value['KeepAlive'] and value['ProgramArguments'][3]==str(configfile)
            assert key.read_text() not in plist.read_text() and config['postgres']['database'] not in plist.read_text()
            foreign=tmp/'foreign.plist';foreign.write_bytes(plistlib.dumps({'Label':'foreign','ProgramArguments':['unrelated']}))
            original=foreign.read_bytes()
            try:install_backup_agent.write_plist(foreign,value);raise AssertionError('foreign agent overwritten')
            except daily_backup.BackupError as error:assert str(error)=='AGENT_TARGET_OCCUPIED'
            assert foreign.read_bytes()==original
            print('PASS 启动参数使用绝对路径，无口令/数据库连接泄漏，拒绝覆盖无关服务配置',flush=True)
            subprocess.run(['launchctl','bootstrap',domain,str(plist)],check=True,stdout=subprocess.DEVNULL);loaded=True
            def wait_success(old_pid=None):
                deadline=time.monotonic()+40
                while time.monotonic()<deadline:
                    try:
                        state=json.loads((tmp/'state.json').read_text());beat=json.loads((tmp/'state.heartbeat.json').read_text())
                        if state['status']=='Succeeded' and beat['status']=='Waiting' and beat['pid']!=old_pid:
                            cmd=subprocess.check_output(['ps','-p',str(beat['pid']),'-o','command='],text=True)
                            if str(root/'scripts/daily_backup.py')+' --config '+str(configfile)+' --watch' in cmd:return state,beat
                    except (OSError,ValueError,subprocess.CalledProcessError):pass
                    time.sleep(.1)
                raise AssertionError('actual agent did not reach live successful state')
            state,beat=wait_success();archives=list((tmp/'archives').glob('*.pgdump.gpg'));assert len(archives)==1 and state['lastSuccess']['archiveVerified']
            active=subprocess.check_output(['launchctl','print',service],text=True);assert 'pid = '+str(beat['pid']) in active
            assert not Path(value['StandardOutPath']).stat().st_mode&0o077
            print('PASS 实际GUI服务域载入后自动启动，完成真实加密备份，系统PID与心跳一致',flush=True)
            with open(tmp/'state.lock','r+b') as lock:
                fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
                try:install_backup_agent.bootout_idle(service,config['stateFile']);raise AssertionError('active backup interrupted')
                except daily_backup.BackupError as error:assert str(error)=='BACKUP_RUNNING_RETRY_LATER'
                assert 'pid = '+str(beat['pid']) in subprocess.check_output(['launchctl','print',service],text=True)
            print('PASS 持有真实备份运行锁时拒绝更新卸载，原系统服务及PID保持运行',flush=True)
            os.kill(beat['pid'],signal.SIGTERM);restarted,new_beat=wait_success(beat['pid'])
            assert restarted['lastSuccess']==state['lastSuccess'] and len(list((tmp/'archives').glob('*.pgdump.gpg')))==1
            print('PASS 真正终止调度后由launchd重新拉起，PID改变，到期前不重复备份或改写原成功记录',flush=True)
        finally:
            if loaded:subprocess.run(['launchctl','bootout',service],check=True,stdout=subprocess.DEVNULL)
            assert subprocess.run(['launchctl','print',service],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode!=0
            subprocess.run(['dropdb','--force',database],env=env,check=True)
    print('PASS 临时服务已卸载、隔离库清除；未修改现有用户启动项，未声称实际重启已验',flush=True)

if __name__=='__main__':main()
