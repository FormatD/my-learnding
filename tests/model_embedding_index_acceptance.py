"""Actual disposable PG complete immutable index; controlled vectors, no model calls."""
import subprocess,uuid,sys
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name)
subprocess.run(['createdb',name],env=env,check=True)
try:
    subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'model-embedding-index'],env=env,check=True,timeout=90)
    if '--export-schema' in sys.argv:
        subprocess.run(['python3',str(root/'scripts/schema_dictionary.py')],env=env,check=True,timeout=45)
finally:subprocess.run(['dropdb','--force',name],env=env,check=True)
