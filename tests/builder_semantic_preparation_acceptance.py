"""Actual PostgreSQL semantic input preparation/revalidation, no provider/model calls."""
import subprocess,uuid
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name)
subprocess.run(['createdb',name],env=env,check=True)
try:subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'semantic-preparation'],env=env,check=True,timeout=150)
finally:subprocess.run(['dropdb','--force',name],env=env,check=True)
