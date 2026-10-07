"""Opt-in actual local semantic protocol diagnostic on an explicitly constructed math fixture.
Not extracted textbook content, published library, human review/gold or semantic quality evidence.
"""
import os,subprocess,uuid
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
if os.getenv('OMLX_SEMANTIC_SMOKE')!='1':raise SystemExit('Set OMLX_SEMANTIC_SMOKE=1 for one actual local diagnostic call')
folder=root/'.local/textbook-workflow/semantic-model-diagnostics';folder.mkdir(parents=True,exist_ok=True)
report=folder/f'actual-{uuid.uuid4().hex}.json'
env=environment('learning_fault_semantic_local_unused')
env.update(SEMANTIC_LOCAL_CONFIG=str(root/'.local/omlx.json'),SEMANTIC_LOCAL_KEY=str(root/'.local/omlx-api-key.txt'),SEMANTIC_LOCAL_REPORT=str(report))
subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'builder-semantic-live'],env=env,check=True,timeout=660)
print('Private diagnostic retained:',report)
