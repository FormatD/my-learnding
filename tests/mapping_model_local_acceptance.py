"""Opt-in actual local model diagnostic on original unreviewed draft content.

This is protocol evidence, not formal mapping accuracy, household output or human gold.
"""
import os,subprocess,uuid
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
if os.environ.get('OMLX_MAPPING_SMOKE')!='1':
 print('SKIP actual model diagnostic; set OMLX_MAPPING_SMOKE=1 explicitly')
 raise SystemExit(0)
config=Path(os.environ.get('MAPPING_LOCAL_CONFIG',root/'.local/omlx.json')).resolve()
pack=Path(os.environ.get('MAPPING_LOCAL_PACK',root/'.local/textbook-workflow/division-original-draft/content-pack.json')).resolve()
key=Path(os.environ.get('MAPPING_LOCAL_KEY',root/'.local/omlx-api-key.txt')).resolve()
for path in [config,pack,key]:
 if not path.is_file():raise RuntimeError('Required local diagnostic input missing')
report=root/'.local/textbook-workflow/mapping-model-diagnostics'/('actual-'+uuid.uuid4().hex+'.json')
env=environment('learning_fault_mapping_protocol_unused')
env.update(MAPPING_LOCAL_CONFIG=str(config),MAPPING_LOCAL_PACK=str(pack),MAPPING_LOCAL_KEY=str(key),MAPPING_LOCAL_REPORT=str(report))
subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'mapping-model-live'],env=env,check=True,timeout=660)
print('Private diagnostic retained:',report.name)
