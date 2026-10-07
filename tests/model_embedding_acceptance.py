"""Embedding protocol/controlled localhost transport. No real model or database operations."""
import subprocess
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
env=environment('learning_fault_embedding_protocol_unused')
subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'model-embeddings'],env=env,check=True,timeout=45)
