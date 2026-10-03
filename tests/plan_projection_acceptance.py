"""Real migration, conservative planning and captured projection provenance in a disposable database."""
import subprocess,uuid
from pathlib import Path
from persistence_environment import environment,dotnet
def main():
    root=Path(__file__).resolve().parent.parent;database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database)
    subprocess.run(['createdb',database],env=env,check=True)
    try:subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'plan-projection'],env=env,check=True)
    finally:subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
