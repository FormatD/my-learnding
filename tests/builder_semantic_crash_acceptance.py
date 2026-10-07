"""SIGKILL a controlled in-process provider, verify actual lease expiry/unknown budget hold.
Temporary database only; explicit observed fixture-provider death, no claims about external oMLX.
"""
import subprocess,uuid,time,signal
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name);child=None
command=[dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')]
def sql(query):return subprocess.check_output(['psql','-At','-c',query],env=env,text=True).strip()
subprocess.run(['createdb',name],env=env,check=True)
try:
 subprocess.run(command+['semantic-ledger-crash-seed'],env=env,check=True,timeout=60)
 child=subprocess.Popen(command+['semantic-ledger-crash'],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 deadline=time.monotonic()+20
 while time.monotonic()<deadline:
  assert child.poll() is None,'controlled process ended before start committed'
  if sql('SELECT count(*) FROM "BuilderSemanticCall" WHERE "Status"=\'Started\'')=='1':break
  time.sleep(.1)
 else:raise AssertionError('controlled start not committed')
 child.kill();code=child.wait(timeout=10);assert code==-signal.SIGKILL
 env['SEMANTIC_CRASH_RECEIPT']=f'Owned controlled in-process provider PID {child.pid}, observed wait exit SIGKILL {code}; external oMLX was not involved'
 deadline=time.monotonic()+15
 while time.monotonic()<deadline:
  if sql('SELECT count(*) FROM "BackgroundJob" WHERE "Status"=\'Running\' AND "LeaseExpiresAt"<=clock_timestamp()')=='1':break
  time.sleep(.2)
 else:raise AssertionError('actual lease did not expire')
 subprocess.run(command+['semantic-ledger-crash-check'],env=env,check=True,timeout=45)
finally:
 if child is not None and child.poll() is None:child.kill();child.wait(timeout=10)
 subprocess.run(['dropdb','--force',name],env=env,check=True)
