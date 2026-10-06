"""Strict mapping protocol and controlled local transport; no database or actual model calls."""
import subprocess
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
env=environment('learning_fault_mapping_protocol_unused')
subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'mapping-model-transport'],env=env,check=True,timeout=45)
