"""Real PostgreSQL family-write progress during an explicitly paused provider."""
import subprocess,sys,uuid
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
name='learning_fault_'+uuid.uuid4().hex[:12]
env=environment(name)
subprocess.run(['createdb',name],env=env,check=True)
try:
 modes=['builder-responsiveness-before'] if '--before' in sys.argv else ['builder-responsiveness','builder-responsiveness-changed-source','builder-worker-lanes']
 for mode in modes:
  subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),mode],env=env,check=True,timeout=90)
finally:subprocess.run(['dropdb','--force',name],env=env,check=True)
