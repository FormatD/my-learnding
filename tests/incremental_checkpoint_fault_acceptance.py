"""Kill a real incremental suffix before commit; original state remains and recovery processes one suffix."""
import getpass,os,select,subprocess,time,uuid
from pathlib import Path

def main():
    root=Path(__file__).resolve().parent.parent;database='learning_fault_'+uuid.uuid4().hex[:12];env=os.environ.copy();env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE=database,PERSISTENCE_TEST_CONNECTION=f'Host=127.0.0.1;Port=55432;Database={database};Username={getpass.getuser()}');env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH'];command=[str(root/'.tools/dotnet/dotnet'),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')];child=None
    def sql(q):return subprocess.check_output(['psql','-Atc',q],env=env,text=True).strip()
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        subprocess.run(command+['incremental-seed'],env=env,check=True,stdout=subprocess.DEVNULL)
        child=subprocess.Popen(command+['incremental-crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if select.select([child.stdout],[],[],1)[0]:
                line=child.stdout.readline()
                if line.strip()=='BEFORE_COMMIT':break
                if child.poll() is not None:raise AssertionError('worker stopped before commit: '+line)
        else:raise AssertionError('rebuild worker did not reach commit barrier')
        old_hash=sql('SELECT md5(string_agg("Payload",\'\' ORDER BY "Id")) FROM "AssessmentCheckpoint"')
        uncommitted='SELECT (SELECT count(*) FROM "Generations"),(SELECT count(*) FROM "AssessmentCheckpoint"),(SELECT count(*) FROM "Evidence"),(SELECT count(*) FROM "AssessmentContext"),(SELECT count(*) FROM "AssessmentRebuildResult"),(SELECT count(*) FROM "Generations" WHERE "Status"=\'Active\')'
        assert sql(uncommitted)=='1|1|2|2|0|1','incremental results visible before commit'
        child.kill();child.wait(timeout=10)
        assert sql(uncommitted)=='1|1|2|2|0|1','killed result transaction left partial output'
        assert sql('SELECT md5(string_agg("Payload",\'\' ORDER BY "Id")) FROM "AssessmentCheckpoint"')==old_hash,'original checkpoint changed'
        deadline=time.monotonic()+10
        while sql('SELECT count(*) FROM "BackgroundJob" WHERE "Type"=\'AssessmentRebuild\' AND "Status"=\'Running\' AND "LeaseExpiresAt">clock_timestamp()')!='0':
            assert time.monotonic()<deadline,'lease did not actually expire';time.sleep(.1)
        subprocess.run(command+['incremental-recover'],env=env,check=True)
    finally:
        if child is not None and child.poll() is None:child.kill();child.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
