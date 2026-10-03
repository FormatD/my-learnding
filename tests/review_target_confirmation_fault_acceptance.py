"""Interrupt actual confirmed review-target consumption before commit, then recover after lease expiry."""
import select,subprocess,time,uuid
from pathlib import Path
from persistence_environment import environment,dotnet
def main():
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);env['REVIEW_CONFIRM_MODERN']='1';command=[dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')];child=None
    def sql(query):return subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',query],env=env,text=True).strip()
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        subprocess.run(command+['review-confirm-crash-seed'],env=env,check=True,stdout=subprocess.DEVNULL)
        tables=['ReviewTargetConfirmation','Attempts','Tasks','Gradings','Evidence','AssessmentContext','Reviews','Generations','Masteries','AssessmentCheckpoint','Students','Outbox','DomainEvent','ConsumerReceipt','AssessmentConsumerCursor','Audits','MappingSetRevision','MappingSetItem','ReleaseMappingSet','ContentReviewRecord']
        digest='SELECT md5(concat('+','.join(f"(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY \"Id\"),'[]'::jsonb)::text FROM \"{table}\" t)" for table in tables)+'))'
        before=sql(digest);decision=sql('SELECT row_to_json(c)::text FROM "ReviewTargetConfirmation" c');child=subprocess.Popen(command+['review-confirm-crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if select.select([child.stdout],[],[],1)[0]:
                line=child.stdout.readline()
                if line.strip()=='BEFORE_COMMIT':break
                if child.poll() is not None:raise AssertionError('confirmation consumer stopped before commit: '+line)
        else:raise AssertionError('actual confirmation consumer did not reach commit barrier')
        assert sql(digest)==before,'uncommitted confirmation result became visible'
        child.kill();child.wait(timeout=10);assert sql(digest)==before,'interrupted confirmation left partial result or changed producer/history bytes'
        print('PASS 真正终止确认来源结果提交前进程：20张来源/映射/投影/审计/回执表全部原字节保持，已提交家长确认不丢，零部分结果')
        deadline=time.monotonic()+10
        while sql('SELECT count(*) FROM "BackgroundJob" WHERE "Status"=\'Running\' AND "LeaseExpiresAt">clock_timestamp()')!='0':
            assert time.monotonic()<deadline,'lease did not actually expire';time.sleep(.1)
        subprocess.run(command+['review-confirm-recover'],env=env,check=True);assert sql('SELECT row_to_json(c)::text FROM "ReviewTargetConfirmation" c')==decision,'recovery rewrote the submitted parent confirmation'
    finally:
        if child is not None and child.poll() is None:child.kill();child.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
