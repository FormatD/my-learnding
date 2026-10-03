"""Kill a real incremental suffix before commit; original state remains and recovery processes one suffix."""
import getpass,os,select,subprocess,time,uuid
from pathlib import Path
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parent.parent;database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);command=[dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')];child=None
    def sql(q):return subprocess.check_output(['psql','-Atc',q],env=env,text=True).strip()
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        subprocess.run(command+['cursor-seed'],env=env,check=True,stdout=subprocess.DEVNULL)
        projection_tables=['Students','Generations','Masteries','Reviews','Evidence','AssessmentContext','AssessmentCheckpoint','ConsumerReceipt','Outbox','AssessmentConsumerCursor']
        digest_query='SELECT md5(concat('+','.join(f"(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY \"Id\"),'[]'::jsonb)::text FROM \"{table}\" t)" for table in projection_tables)+'))'
        before_projection=sql(digest_query)
        child=subprocess.Popen(command+['cursor-crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if select.select([child.stdout],[],[],1)[0]:
                line=child.stdout.readline()
                if line.strip()=='BEFORE_COMMIT':break
                if child.poll() is not None:raise AssertionError('worker stopped before commit: '+line)
        else:raise AssertionError('rebuild worker did not reach commit barrier')
        old_hash=sql('SELECT md5(string_agg("Payload",\'\' ORDER BY "Id")) FROM "AssessmentCheckpoint"')
        uncommitted='SELECT (SELECT count(*) FROM "Generations"),(SELECT count(*) FROM "AssessmentCheckpoint"),(SELECT count(*) FROM "Evidence"),(SELECT count(*) FROM "AssessmentContext"),(SELECT count(*) FROM "ConsumerReceipt"),(SELECT count(*) FROM "Generations" WHERE "Status"=\'Active\')'
        assert sql(uncommitted)=='1|2|3|3|3|1','online results visible before commit'
        assert sql(digest_query)==before_projection,'uncommitted current generation/mastery/review/receipts became visible'
        child.kill();child.wait(timeout=10)
        assert sql(uncommitted)=='1|2|3|3|3|1','killed result transaction left partial output'
        assert sql(digest_query)==before_projection,'killed online append changed original projection columns'
        assert sql('SELECT md5(string_agg("Payload",\'\' ORDER BY "Id")) FROM "AssessmentCheckpoint"')==old_hash,'original checkpoint changed'
        deadline=time.monotonic()+10
        while sql('SELECT count(*) FROM "BackgroundJob" WHERE "Type"=\'AssessmentProjection\' AND "Status"=\'Running\' AND "LeaseExpiresAt">clock_timestamp()')!='0':
            assert time.monotonic()<deadline,'lease did not actually expire';time.sleep(.1)
        subprocess.run(command+['cursor-recover'],env=env,check=True)
    finally:
        if child is not None and child.poll() is None:child.kill();child.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
