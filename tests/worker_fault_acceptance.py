"""Kill an isolated worker after projection writes, before commit; race two real consumers."""
import getpass
import os
from pathlib import Path
import select
import subprocess
import time
import uuid

def main():
    root=Path(__file__).resolve().parent.parent
    env=os.environ.copy();env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser())
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH']
    database='learning_fault_'+uuid.uuid4().hex[:12];env['PGDATABASE']=database
    env['PERSISTENCE_TEST_CONNECTION']=f"Host=127.0.0.1;Port=55432;Database={database};Username={env['PGUSER']}"
    sdk=root/'.tools/dotnet/dotnet';dotnet=str(sdk) if sdk.exists() else 'dotnet'
    command=[dotnet,str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')]
    child=None
    subprocess.run(['createdb',database],env=env,check=True)
    def sql(query):return subprocess.check_output(['psql','-Atc',query],env=env,text=True).strip()
    try:
        subprocess.run(command+['seed'],env=env,check=True,stdout=subprocess.DEVNULL)
        child=subprocess.Popen(command+['crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if select.select([child.stdout],[],[],1)[0]:
                line=child.stdout.readline()
                if line.strip()=='BEFORE_COMMIT':break
                if child.poll() is not None:raise AssertionError('worker stopped before commit: '+line)
        else:raise AssertionError('worker did not reach commit barrier')
        # Independent reader observes no half-written active generation before the process is killed.
        assert sql('SELECT COUNT(*) FROM "Evidence"')=='0'
        assert sql('SELECT COUNT(*) FROM "Students" WHERE "ActiveGenerationId" IS NOT NULL')=='0'
        child.kill();child.wait(timeout=10)
        subprocess.run(command+['recover'],env=env,check=True)
        subprocess.run(command+['builder'],env=env,check=True)
    finally:
        if child is not None and child.poll() is None:child.kill();child.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)

if __name__=='__main__':main()
