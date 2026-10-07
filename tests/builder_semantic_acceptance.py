"""Four-action semantic protocol/controlled local HTTP; no actual model/database calls."""
import subprocess
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
env=environment('learning_fault_semantic_protocol_unused')
subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'builder-semantic'],env=env,check=True,timeout=45)
