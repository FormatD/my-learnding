"""Actual worker termination after a durable mapping call start, never simulated expiry."""
import selectors,subprocess,time,uuid
from pathlib import Path
from persistence_environment import environment,dotnet
root=Path(__file__).resolve().parent.parent
name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name)
cmd=[dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')];worker=None
subprocess.run(['createdb',name],env=env,check=True)
try:
 subprocess.run(cmd+['mapping-call-crash-seed'],env=env,check=True)
 worker=subprocess.Popen(cmd+['mapping-call-crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,bufsize=1)
 selector=selectors.DefaultSelector();selector.register(worker.stdout,selectors.EVENT_READ);deadline=time.monotonic()+20
 while time.monotonic()<deadline:
  assert worker.poll() is None,'worker ended before physical-call barrier'
  if selector.select(.2) and 'MAPPING_CALL_STARTED' in worker.stdout.readline():break
 else:raise AssertionError('durable start barrier not reached')
 worker.kill();worker.wait(timeout=10);selector.close();deadline=time.monotonic()+10
 while subprocess.check_output(['psql','-Atc','SELECT count(*) FROM "BackgroundJob" WHERE "Status"=\'Running\' AND "LeaseExpiresAt">clock_timestamp();'],env=env,text=True).strip()!='0':
  assert time.monotonic()<deadline,'actual lease did not expire'
  time.sleep(.1)
 subprocess.run(cmd+['mapping-call-crash-recover'],env=env,check=True,timeout=30)
finally:
 if worker is not None and worker.poll() is None:worker.kill();worker.wait(timeout=10)
 subprocess.run(['dropdb','--force',name],env=env,check=True)
