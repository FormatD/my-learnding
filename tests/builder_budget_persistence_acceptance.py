"""Atomic per-family budgets, concurrent reservation and unknown outcomes on a disposable database."""
import getpass,os,subprocess,uuid
from pathlib import Path

def main():
    root=Path(__file__).resolve().parent.parent;env=os.environ.copy();database='learning_fault_'+uuid.uuid4().hex[:12]
    env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,PERSISTENCE_TEST_CONNECTION=f'Host=127.0.0.1;Port=55432;Database={database};Username={getpass.getuser()}')
    env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH'];subprocess.run(['createdb',database],env=env,check=True)
    try:subprocess.run([str(root/'.tools/dotnet/dotnet'),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'builder-budget'],env=env,check=True)
    finally:subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
