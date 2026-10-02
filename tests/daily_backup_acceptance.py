"""Real encrypted archives, due policy, retention and restoration; all data is disposable."""
import copy
import fcntl
import getpass
import importlib.util
import json
import os
import secrets
import shutil
import socket
import sys
import subprocess
import tempfile
import time
import uuid
import urllib.request
import api_acceptance
from api_acceptance import Client
from datetime import timedelta
from pathlib import Path

def main():
    root=Path(__file__).resolve().parent.parent
    spec=importlib.util.spec_from_file_location('daily_backup',root/'scripts/daily_backup.py')
    job=importlib.util.module_from_spec(spec);spec.loader.exec_module(job)
    env=os.environ.copy();suffix=uuid.uuid4().hex[:12];database='learning_fault_'+suffix
    env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,
               PLAN_TEST_USERNAME='backup-fixture-'+suffix,PLAN_TEST_PASSWORD=secrets.token_hex(20),
               PERSISTENCE_TEST_CONNECTION=f'Host=127.0.0.1;Port=55432;Database={database};Username={getpass.getuser()}')
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:/opt/homebrew/bin:'+env['PATH']
    os.environ['PATH']=env['PATH']
    restores=[];subprocess.run(['createdb',database],env=env,check=True)
    with tempfile.TemporaryDirectory(prefix='learning-daily-backup-',dir='/private/tmp') as tmp:
        tmp=Path(tmp)
        try:
            fixture=json.loads(subprocess.check_output([str(root/'.tools/dotnet/dotnet'),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'plan-boundary-seed'],env=env,text=True).strip().splitlines()[-1])
            secret=secrets.token_hex(32);key=tmp/'key';key.write_text(secret);key.chmod(0o600)
            ledgers=[tmp/'students.txt',tmp/'families.txt']
            for ledger in ledgers:ledger.touch(mode=0o600)
            config={'backupDir':str(tmp/'archives'),'stateFile':str(tmp/'state.json'),'passphraseFile':str(key),
                    'dataDir':str(tmp),'deletionLedger':str(ledgers[0]),'familyDeletionLedger':str(ledgers[1]),
                    'postgres':{'host':'127.0.0.1','port':55432,'user':getpass.getuser(),'database':database}}
            configfile=tmp/'config.json';configfile.write_text(json.dumps(config));configfile.chmod(0o600)
            config=job.load_config(configfile)
            # Include an original private attachment in the dump, without touching any real family.
            private_id=str(uuid.uuid4());fid=fixture['familyId']
            subprocess.run(['psql','-v','ON_ERROR_STOP=1','-c',f'''INSERT INTO "PrivateFile" ("Id","FamilyId","CreatedAt","Name","MimeType","Bytes","Hash") VALUES ('{private_id}','{fid}',NOW(),'backup-test.png','image/png',decode('89504e470d0a1a0a','hex'),'daily-backup-private-fixture')'''],env=env,check=True,stdout=subprocess.DEVNULL)
            result=job.execute(config);assert result['outcome']=='Succeeded' and result['archiveVerified'] and not result['restoreVerified'] and not result['independentDisk']
            archive=Path(result['archive']);manifest=json.loads(archive.with_name(archive.name+'.json').read_text());state=json.loads((tmp/'state.json').read_text())
            assert manifest['sha256']==job.digest(archive) and manifest['tableCount']>=30 and state['lastSuccess']==manifest
            assert not archive.stat().st_mode&0o077 and not (tmp/'state.json').stat().st_mode&0o077
            assert secret not in json.dumps(state) and secret not in json.dumps(manifest)
            assert job.execute(config)['outcome']=='NotDue' and len(list((tmp/'archives').glob('*.pgdump.gpg')))==1
            print('PASS 真实AES256备份解密逐字节一致、全部数据库表可读，私有状态不含口令；23小时到期前不重复运行',flush=True)
            # Held advisory lock is authoritative, not a stale marker. It releases automatically on process exit.
            with open(Path(config['stateFile']).with_suffix('.lock'),'r+b') as lock:
                fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
                assert job.execute(config,force=True)['outcome']=='AlreadyRunning'
            print('PASS 独立文件锁拒绝并发运行；锁文件本身不被当作活跃任务证明',flush=True)
            # Age only disposable copies: no real history or system clock is changed.
            def historical(days):
                value=job.utc()-timedelta(days=days);name=f'learning-{value:%Y%m%dT%H%M%SZ}-{uuid.uuid4().hex[:12]}.pgdump.gpg'
                target=archive.parent/name;shutil.copyfile(archive,target);target.chmod(0o600)
                meta=manifest|{'archive':name,'snapshotStartedAt':job.stamp(value)}
                job.write_json(target.with_name(name+'.json'),meta);return target
            old=historical(31);kept=historical(29)
            unknown=archive.parent/'foreign-backup.pgdump.gpg';unknown.write_bytes(b'not managed')
            orphan=archive.parent/f'learning-20000101T000000Z-{uuid.uuid4().hex[:12]}.pgdump.gpg';orphan.write_bytes(b'no verified manifest')
            linked=archive.parent/f'learning-20000101T000000Z-{uuid.uuid4().hex[:12]}.pgdump.gpg';linked.symlink_to(unknown)
            removed=job.prune(archive.parent,job.utc());assert removed==[old.name] and not old.exists() and kept.exists() and orphan.exists() and linked.is_symlink() and unknown.exists() and archive.exists()
            print('PASS 30天保留只清理过期且摘要匹配的自有归档；近期、未知、缺少证明和符号链接均保留',flush=True)
            # Failure preserves last good snapshot and never prunes while execution is broken.
            expired=historical(31);bad=copy.deepcopy(config);bad['postgres']['port']=1
            try:job.execute(bad,force=True);raise AssertionError('bad database unexpectedly backed up')
            except job.BackupError:pass
            failed=json.loads((tmp/'state.json').read_text());assert failed['status']=='Failed' and failed['lastSuccess']==manifest and expired.exists() and not list(archive.parent.glob('*.partial'))
            assert job.execute(bad)['outcome']=='Backoff'
            bad=copy.deepcopy(config);bad['requireIndependentDisk']=True
            try:job.execute(bad,force=True);raise AssertionError('same disk treated as independent')
            except job.BackupError as error:assert str(error)=='INDEPENDENT_DISK_REQUIRED'
            key.chmod(0o644)
            try:job.execute(config,force=True);raise AssertionError('public passphrase file accepted')
            except job.BackupError as error:assert str(error)=='PRIVATE_FILE_REQUIRED'
            finally:key.chmod(0o600)
            print('PASS 失败保留最后成功记录，不清理旧备份；五分钟退避，不把同机文件系统当作独立磁盘',flush=True)
            good=job.execute(config,force=True);assert good['outcome']=='Succeeded' and not expired.exists()
            assert json.loads((tmp/'state.json').read_text())['status']=='Succeeded'
            watcher=subprocess.Popen(['python3',str(root/'scripts/daily_backup.py'),'--config',str(configfile),'--watch'],env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            try:
                heartbeat=Path(config['stateFile']).with_suffix('.heartbeat.json');deadline=time.monotonic()+10
                while time.monotonic()<deadline:
                    assert watcher.poll() is None,'watcher stopped unexpectedly'
                    if heartbeat.exists():
                        try:observed=json.loads(heartbeat.read_text())
                        except json.JSONDecodeError:observed={}
                        if observed.get('pid')==watcher.pid and observed.get('status')=='Waiting':break
                    time.sleep(.05)
                else:raise AssertionError('watcher did not complete a real due check')
                with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
                api_env=env|{'ConnectionStrings__Learning':env['PERSISTENCE_TEST_CONNECTION'],'BackupConfigFile':str(configfile),
                            'DeletionLedger':str(ledgers[0]),'FamilyDeletionLedger':str(ledgers[1]),'ExportDirectory':str(tmp/'exports')}
                with open(tmp/'api.log','w+') as log:
                    api=subprocess.Popen([str(root/'.tools/dotnet/dotnet'),str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',f'http://127.0.0.1:{port}'],cwd=root/'src/server',env=api_env,stdout=log,stderr=subprocess.STDOUT)
                    try:
                        deadline=time.monotonic()+30
                        while time.monotonic()<deadline:
                            assert api.poll() is None,'isolated API stopped'
                            try:
                                with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/health',timeout=1) as response:
                                    if response.status==200:break
                            except OSError:time.sleep(.1)
                        else:raise AssertionError('isolated API unavailable')
                        api_acceptance.BASE=f'http://127.0.0.1:{port}/api/v1';client=Client();credentials={'userName':env['PLAN_TEST_USERNAME'],'password':env['PLAN_TEST_PASSWORD']}
                        client.request('/auth/login',credentials);client.request('/me');status=client.request('/operations')['backup']
                        assert status['configured'] and status['available'] and status['schedulerActive'] and status['archiveVerified']
                        assert not status['verified'] and not status['restoreVerified'] and not status['independentDisk']
                        assert secret not in json.dumps(status) and str(key) not in json.dumps(status) and str(archive.parent) not in json.dumps(status)
                        if os.environ.get('RUN_BACKUP_BROWSER')=='1':
                            browser_env=api_env|{'LEARNING_TEST_URL':f'http://127.0.0.1:{port}','BACKUP_TEST_USER':credentials['userName'],'BACKUP_TEST_PASSWORD':credentials['password']}
                            subprocess.run(['npm','run','test:e2e','--','tests/backup_status.spec.ts','--workers=1'],cwd=root/'src/web',env=browser_env,check=True)
                        sys.path.insert(0,str(root/'scripts'));import restore_drill
                        restore_drill.execute(configfile)
                        exercised=client.request('/operations')['backup'];assert exercised['restoreVerified'] and exercised['restoreCheckedAt'] and exercised['restoreDrillSeconds']>=0 and not exercised['verified']
                        if os.environ.get('RUN_BACKUP_BROWSER')=='1':
                            subprocess.run(['npm','run','test:e2e','--','tests/backup_status.spec.ts','--workers=1'],cwd=root/'src/web',env=browser_env|{'BACKUP_TEST_RESTORED':'1'},check=True)
                        receipt_path=Path(config['stateFile']).with_suffix('.restore-drill.json');receipt=json.loads(receipt_path.read_text());wrong=copy.deepcopy(receipt);wrong['archiveSha256']='0'*64;job.write_json(receipt_path,wrong)
                        mismatched=client.request('/operations')['backup'];assert not mismatched['restoreVerified'] and mismatched['archiveVerified']
                        wrong=copy.deepcopy(receipt);wrong['seconds']='invalid';job.write_json(receipt_path,wrong)
                        malformed_receipt=client.request('/operations')['backup'];assert not malformed_receipt['restoreVerified'] and malformed_receipt['archiveVerified']
                        job.write_json(receipt_path,receipt)
                        client.request('/family/members',{'userName':'backup-parent-'+suffix,'password':secret,'roles':['Parent']},expected=201)
                        parent=Client();parent.request('/auth/login',{'userName':'backup-parent-'+suffix,'password':secret});parent.request('/me')
                        assert parent.request('/operations')['backup'] is None
                        statefile=Path(config['stateFile']);original_state=json.loads(statefile.read_text());aged=copy.deepcopy(original_state)
                        aged['lastSuccess']['snapshotStartedAt']=job.stamp(job.utc()-timedelta(hours=25));aged['status']='Failed';aged['errorCode']='TOOL_FAILED_PG_DUMP';job.write_json(statefile,aged)
                        observed=client.request('/operations');codes={s['code'] for s in observed['signals']};assert {'BackupFailed','BackupOverdue'}<=codes and observed['backup']['lastSnapshotAt'] and observed['backup']['archiveVerified']
                        deadline=time.monotonic()+12
                        while time.monotonic()<deadline:
                            log.seek(0);logs=log.read()
                            if 'Operational alert BackupFailed' in logs and 'Operational alert BackupOverdue' in logs:break
                            time.sleep(.1)
                        else:raise AssertionError('independent backup failure monitor did not log')
                        job.write_json(statefile,original_state)
                        deadline=time.monotonic()+12
                        while time.monotonic()<deadline:
                            log.seek(0);logs=log.read()
                            if 'Operational alert resolved BackupFailed' in logs and 'Operational alert resolved BackupOverdue' in logs:break
                            time.sleep(.1)
                        else:raise AssertionError('backup recovery not logged')
                        malformed=copy.deepcopy(original_state);malformed['lastSuccess']['snapshotStartedAt']='not-a-date';job.write_json(statefile,malformed)
                        unavailable=client.request('/operations');assert unavailable['backup']['status']=='Unavailable' and any(s['code']=='BackupUnavailable' for s in unavailable['signals'])
                        job.write_json(statefile,original_state)
                        original_bytes=Path(good['archive']).read_bytes();damaged=bytearray(original_bytes);damaged[-1]^=1;Path(good['archive']).write_bytes(damaged)
                        assert any(s['code']=='BackupArchiveUnavailable' for s in client.request('/operations')['signals'])
                        Path(good['archive']).write_bytes(original_bytes)
                        watcher.terminate();watcher.communicate(timeout=10)
                        stopped=client.request('/operations');assert not stopped['backup']['schedulerActive'] and any(s['code']=='BackupSchedulerStopped' for s in stopped['signals'])
                        client.request('/students/'+fixture['studentId']+'/child-sessions',{});client.request('/operations',expected=403)
                        print('PASS 负责人状态核对真实进程及摘要；超过24小时/失败/归档损坏/进程终止分别告警，非负责人不见本机备份，孩子拒绝',flush=True)
                    finally:
                        api.terminate();api.wait(timeout=10)
            finally:
                if watcher.poll() is None:watcher.terminate();watcher.communicate(timeout=10)
            print('PASS 实际独立调度进程完成到期检查并写原子心跳，正常等待不制造重复归档；口令文件宽权限拒绝',flush=True)
            # Prove an actual restore, beyond pg_restore --list archive inspection.
            restore_env=env|{'BACKUP_PASSPHRASE':secret,'GNUPGHOME':str(tmp/'gpg'),'DELETION_LEDGER':str(ledgers[0]),'FAMILY_DELETION_LEDGER':str(ledgers[1])}
            Path(restore_env['GNUPGHOME']).mkdir(mode=0o700)
            def sql(query, dbname):
                return subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',query],env=env|{'PGDATABASE':dbname},text=True).strip()
            restored='learning_restore_'+suffix;restores.append(restored);subprocess.run(['createdb',restored],env=env,check=True)
            subprocess.run(['sh',str(root/'scripts/restore.sh'),good['archive']],env=restore_env|{'PGDATABASE':restored},check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            for table in ['Students','Releases','Attempts','Gradings','Evidence','Reviews','PrivateFile']:
                query=f'''SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]'::jsonb) FROM "{table}" t'''
                assert sql(query,database)==sql(query,restored),f'{table} differs after actual restore'
            assert sql('SELECT COUNT(*) FROM "AuthSessions"',restored)=='0' and sql('SELECT COUNT(*) FROM "Commands"',restored)=='0'
            ledgers[1].write_text(fid+','+str(uuid.uuid4())+'\n')
            deleted='learning_restore_deleted_'+suffix;restores.append(deleted);subprocess.run(['createdb',deleted],env=env,check=True)
            subprocess.run(['sh',str(root/'scripts/restore.sh'),good['archive']],env=restore_env|{'PGDATABASE':deleted},check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            for table in ['Families','Students','Attempts','Evidence','Reviews','PrivateFile']:
                assert sql(f'SELECT COUNT(*) FROM "{table}"',deleted)=='0'
            print('PASS 真实新空库恢复逐行一致，原私有附件和复习保留；独立最新家庭删除清单防止旧备份复活',flush=True)
        finally:
            for name in restores+[database]:subprocess.run(['dropdb','--force',name],env=env,check=True)
            if (tmp/'gpg').exists():subprocess.run(['gpgconf','--homedir',str(tmp/'gpg'),'--kill','gpg-agent'],env=env,check=False,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)

if __name__=='__main__':main()
