"""Kill a real queued mapping worker before result commit; wait expiry and race recovery."""
import getpass,os,select,subprocess,time,uuid
from pathlib import Path
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parent.parent;database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);command=[dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')];child=None
    def sql(q):return subprocess.check_output(['psql','-Atc',q],env=env,text=True).strip()
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        subprocess.run(command+['mapping-job-seed'],env=env,check=True,stdout=subprocess.DEVNULL)
        child=subprocess.Popen(command+['mapping-job-crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if select.select([child.stdout],[],[],1)[0]:
                line=child.stdout.readline()
                if line.strip()=='BEFORE_COMMIT':break
                if child.poll() is not None:raise AssertionError('worker stopped before commit: '+line)
        else:raise AssertionError('mapping worker did not reach commit barrier')
        assert sql('SELECT (SELECT count(*) FROM "MappingRun"),(SELECT count(*) FROM "MappingSuggestion"),(SELECT count(*) FROM "MappingPreparation")')=='0|0|1'
        child.kill();child.wait(timeout=10);deadline=time.monotonic()+10
        while sql('SELECT count(*) FROM "BackgroundJob" WHERE "Type"=\'MappingSuggestions\' AND "Status"=\'Running\' AND "LeaseExpiresAt">clock_timestamp()')!='0':
            assert time.monotonic()<deadline,'lease did not actually expire';time.sleep(.1)
        subprocess.run(command+['mapping-job-recover'],env=env,check=True)
    finally:
        if child is not None and child.poll() is None:child.kill();child.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
