#!/usr/bin/env python3
"""Prepare or install this project's user-login backup agent; never overwrites unrelated jobs."""
import argparse
import fcntl
import hashlib
import os
import plistlib
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
import daily_backup


def manifest(root, config_path, label=None):
    root=Path(root).resolve();config_path=Path(config_path).resolve()
    config=daily_backup.load_config(config_path)
    for key in ['passphraseFile','deletionLedger','familyDeletionLedger']:
        daily_backup.private_file(config[key])
    daily_backup.private_dir(Path(config['stateFile']).parent)
    if not (root/'scripts/daily_backup.py').is_file():raise daily_backup.BackupError('RUNNER_UNAVAILABLE')
    tool_dirs=[]
    for tool in ['pg_dump','pg_restore','psql','gpg','gpgconf']:
        found=shutil.which(tool)
        if not found:raise daily_backup.BackupError('BACKUP_TOOLS_UNAVAILABLE')
        directory=str(Path(found).parent)
        if directory not in tool_dirs:tool_dirs.append(directory)
    label=label or 'local.learning.backup.'+hashlib.sha256(str(root).encode()).hexdigest()[:12]
    log=Path(config['stateFile']).parent/'daily-backup-agent.log'
    return {'Label':label,'ProgramArguments':[sys.executable,str(root/'scripts/daily_backup.py'),'--config',str(config_path),'--watch'],
            'WorkingDirectory':str(root),'RunAtLoad':True,'KeepAlive':True,'ThrottleInterval':10,
            'ProcessType':'Background','Umask':0o077,'EnvironmentVariables':{'PATH':':'.join(tool_dirs+['/usr/bin','/bin','/usr/sbin','/sbin'])},
            'StandardOutPath':str(log),'StandardErrorPath':str(log)}


def write_plist(path, value):
    path=Path(path)
    if path.is_symlink():raise daily_backup.BackupError('AGENT_TARGET_OCCUPIED')
    if path.exists():
        existing=plistlib.loads(path.read_bytes())
        if existing.get('Label')!=value['Label'] or existing.get('ProgramArguments',[])[:2]!=value['ProgramArguments'][:2]:
            raise daily_backup.BackupError('AGENT_TARGET_OCCUPIED')
    with tempfile.NamedTemporaryFile(dir=path.parent,prefix='.learning-agent-',delete=False) as target:
        pending=Path(target.name)
        try:
            target.write(plistlib.dumps(value));target.flush();os.fsync(target.fileno());os.replace(pending,path)
        finally:pending.unlink(missing_ok=True)


def bootout_idle(service, state_file):
    lock_fd=os.open(Path(state_file).with_suffix('.lock'),os.O_CREAT|os.O_RDWR|os.O_NOFOLLOW,0o600)
    try:
        try:fcntl.flock(lock_fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
        except BlockingIOError:raise daily_backup.BackupError('BACKUP_RUNNING_RETRY_LATER') from None
        subprocess.run(['launchctl','bootout',service],check=True,stdout=subprocess.DEVNULL)
    finally:os.close(lock_fd)


def main():
    os.umask(0o077)
    parser=argparse.ArgumentParser();parser.add_argument('--config',required=True);parser.add_argument('--install',action='store_true');args=parser.parse_args()
    root=Path(__file__).resolve().parent.parent
    try:
        value=manifest(root,args.config);prepared=daily_backup.private_dir(root/'.local/launchagents')/(value['Label']+'.plist')
        write_plist(prepared,value)
        if not args.install:
            print('Prepared: '+str(prepared));return
        if sys.platform!='darwin':raise daily_backup.BackupError('MACOS_REQUIRED')
        directory=Path.home()/'Library/LaunchAgents'
        if directory.is_symlink():raise daily_backup.BackupError('AGENT_DIRECTORY_INVALID')
        directory.mkdir(parents=True,exist_ok=True)
        target=directory/prepared.name
        # Check ownership before unloading or replacing anything.
        if target.exists() or target.is_symlink():
            if target.is_symlink():raise daily_backup.BackupError('AGENT_TARGET_OCCUPIED')
            existing=plistlib.loads(target.read_bytes())
            if existing.get('Label')!=value['Label'] or existing.get('ProgramArguments',[])[:2]!=value['ProgramArguments'][:2]:raise daily_backup.BackupError('AGENT_TARGET_OCCUPIED')
        domain='gui/'+str(os.getuid());service=domain+'/'+value['Label']
        current=subprocess.run(['launchctl','print',service],stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
        loaded=current.returncode==0
        if loaded:
            if not target.exists() or 'path = '+str(target) not in [line.strip() for line in current.stdout.splitlines()]:raise daily_backup.BackupError('AGENT_TARGET_OCCUPIED')
            config=daily_backup.load_config(args.config)
            bootout_idle(service,config['stateFile'])
        write_plist(target,value)
        log_fd=os.open(value['StandardOutPath'],os.O_CREAT|os.O_WRONLY|os.O_NOFOLLOW,0o600)
        try:os.fchmod(log_fd,0o600)
        finally:os.close(log_fd)
        subprocess.run(['launchctl','bootstrap',domain,str(target)],check=True,stdout=subprocess.DEVNULL)
        print('Installed: '+value['Label']+'; starts at this user login, not before login.')
    except (daily_backup.BackupError,OSError,ValueError,subprocess.CalledProcessError) as error:
        print('Failed: '+(str(error) if isinstance(error,daily_backup.BackupError) else 'AGENT_SETUP_FAILED'));raise SystemExit(1)


if __name__=='__main__':main()
