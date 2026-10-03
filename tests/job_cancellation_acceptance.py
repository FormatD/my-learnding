"""Cancel an actual builder while uncommitted candidates and the family lock are held."""
import getpass,os,select,subprocess,time,uuid
from pathlib import Path
def main():
 root=Path(__file__).resolve().parent.parent;name='learning_fault_'+uuid.uuid4().hex[:12];env=dict(os.environ,PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=name,PERSISTENCE_TEST_CONNECTION=f'Host=127.0.0.1;Port=55432;Database={name};Username={getpass.getuser()}');env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH'];cmd=[str(root/'.tools/dotnet/dotnet'),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')];child=None
 subprocess.run(['createdb',name],env=env,check=True)
 try:
  subprocess.run(cmd+['cancel-worker-seed'],env=env,check=True)
  child=subprocess.Popen(cmd+['cancel-worker'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);deadline=time.monotonic()+30
  while time.monotonic()<deadline:
   if select.select([child.stdout],[],[],1)[0]:
    line=child.stdout.readline()
    if line.strip()=='CANCEL_READY':break
    assert child.poll() is None,'worker stopped before cancellation boundary: '+line
  else:raise AssertionError('actual candidate boundary not reached')
  subprocess.run(cmd+['cancel-worker-request'],env=env,check=True,timeout=10)
  output=child.communicate(timeout=10)[0];assert child.returncode==0,output
  subprocess.run(cmd+['cancel-worker-verify'],env=env,check=True)
 finally:
  if child is not None and child.poll() is None:child.kill();child.wait(timeout=10)
  subprocess.run(['dropdb','--force',name],env=env,check=True)
if __name__=='__main__':main()
