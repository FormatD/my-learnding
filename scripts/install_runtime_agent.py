#!/usr/bin/env python3
"""Prepare or install only this project's macOS user-login runtime service."""
import argparse,hashlib,os,plistlib,subprocess,sys,tempfile
from pathlib import Path
import local_runtime

def manifest(config_path):
    c=local_runtime.load(config_path);root=Path(c['root']);label='local.learning.runtime.'+hashlib.sha256(str(root).encode()).hexdigest()[:12]
    return {'Label':label,'ProgramArguments':[sys.executable,str(root/'scripts/local_runtime.py'),'--config',str(Path(config_path).absolute())],
        'WorkingDirectory':str(root),'RunAtLoad':True,'KeepAlive':True,'ThrottleInterval':10,'ProcessType':'Background','Umask':0o077,
        'EnvironmentVariables':{'PATH':c['postgresBin']+':/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin'},
        'StandardOutPath':str(Path(c['stateFile']).parent/'runtime-agent.log'),'StandardErrorPath':str(Path(c['stateFile']).parent/'runtime-agent.log')}

def check_target(path,value):
    if path.is_symlink():raise local_runtime.RuntimeErrorCode('AGENT_TARGET_OCCUPIED')
    if path.exists():
        old=plistlib.loads(path.read_bytes())
        if path.stat().st_uid!=os.getuid() or old.get('Label')!=value['Label'] or old.get('ProgramArguments',[])[:2]!=value['ProgramArguments'][:2]:raise local_runtime.RuntimeErrorCode('AGENT_TARGET_OCCUPIED')

def write(path,value):
    check_target(path,value)
    with tempfile.NamedTemporaryFile(dir=path.parent,prefix='.runtime-agent-',delete=False) as output:
        pending=Path(output.name)
        try:output.write(plistlib.dumps(value));output.flush();os.fsync(output.fileno());os.replace(pending,path)
        finally:pending.unlink(missing_ok=True)

def main():
    os.umask(0o077);parser=argparse.ArgumentParser();parser.add_argument('--config',required=True);parser.add_argument('--install',action='store_true');args=parser.parse_args()
    try:
        value=manifest(args.config);root=Path(value['WorkingDirectory']);directory=root/'.local/launchagents';directory.mkdir(mode=0o700,exist_ok=True)
        if directory.is_symlink():raise local_runtime.RuntimeErrorCode('AGENT_DIRECTORY_INVALID')
        prepared=directory/(value['Label']+'.plist');write(prepared,value)
        if not args.install:print('Prepared: '+str(prepared));return
        if sys.platform!='darwin':raise local_runtime.RuntimeErrorCode('MACOS_REQUIRED')
        directory=Path.home()/'Library/LaunchAgents'
        if directory.is_symlink():raise local_runtime.RuntimeErrorCode('AGENT_DIRECTORY_INVALID')
        directory.mkdir(parents=True,exist_ok=True);target=directory/prepared.name;check_target(target,value)
        domain='gui/'+str(os.getuid());service=domain+'/'+value['Label'];current=subprocess.run(['launchctl','print',service],capture_output=True,text=True)
        if current.returncode==0:
            if not target.exists() or 'path = '+str(target) not in [s.strip() for s in current.stdout.splitlines()]:raise local_runtime.RuntimeErrorCode('AGENT_TARGET_OCCUPIED')
            subprocess.run(['launchctl','bootout',service],check=True)
        write(target,value);os.close(local_runtime.private(value['StandardOutPath'],os.O_CREAT|os.O_WRONLY|os.O_APPEND))
        subprocess.run(['launchctl','bootstrap',domain,str(target)],check=True)
        print('Installed: '+value['Label']+'; starts after current user login.')
    except (local_runtime.RuntimeErrorCode,OSError,ValueError,subprocess.SubprocessError) as ex:
        print('Failed: '+(str(ex) if isinstance(ex,local_runtime.RuntimeErrorCode) else 'AGENT_SETUP_FAILED'));raise SystemExit(1)
if __name__=='__main__':main()
