"""Actual isolated PostgreSQL mapping physical calls and shared Builder budgets."""
import subprocess,uuid
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name)
subprocess.run(['createdb',name],env=env,check=True)
try:subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'mapping-call-ledger'],env=env,check=True,timeout=90)
finally:subprocess.run(['dropdb','--force',name],env=env,check=True)
