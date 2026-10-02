"""Kill a real Builder worker after durable call return, before candidate commit."""
import getpass,os,selectors,subprocess,time,uuid
from pathlib import Path

def main():
    root=Path(__file__).resolve().parent.parent;database='learning_fault_'+uuid.uuid4().hex[:12];env=dict(os.environ,PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,PERSISTENCE_TEST_CONNECTION=f'Host=127.0.0.1;Port=55432;Database={database};Username={getpass.getuser()}');env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH'];command=[str(root/'.tools/dotnet/dotnet'),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')];child=None
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        subprocess.run(command+['builder-call-crash-seed'],env=env,check=True)
        child=subprocess.Popen(command+['builder-call-crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,bufsize=1)
        selector=selectors.DefaultSelector();selector.register(child.stdout,selectors.EVENT_READ);deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            assert child.poll() is None,'worker terminated before commit barrier'
            if selector.select(.2):
                line=child.stdout.readline()
                if 'BEFORE_COMMIT' in line:break
        else:raise AssertionError('commit barrier was not reached')
        child.kill();child.wait(timeout=10);selector.close();subprocess.run(command+['builder-call-crash-recover'],env=env,check=True)
        subprocess.run(command+['builder-call-crash-seed'],env=env,check=True)
        child=subprocess.Popen(command+['builder-call-started-crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,bufsize=1)
        selector=selectors.DefaultSelector();selector.register(child.stdout,selectors.EVENT_READ);deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            assert child.poll() is None,'worker terminated before start barrier'
            if selector.select(.2) and 'CALL_STARTED' in child.stdout.readline():break
        else:raise AssertionError('start barrier was not reached')
        child.kill();child.wait(timeout=10);selector.close();subprocess.run(command+['builder-call-started-recover'],env=env,check=True)
    finally:
        if child and child.poll() is None:child.kill();child.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
