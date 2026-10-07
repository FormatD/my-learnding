"""Kill the semantic queue worker with controlled provider before result commit; retain returned call and race recovery."""
import select,signal,subprocess,time,uuid
from pathlib import Path
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parent.parent;database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);command=[dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll')];child=None
    def sql(q):return subprocess.check_output(['psql','-Atc',q],env=env,text=True).strip()
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        subprocess.run(command+['semantic-result-seed'],env=env,check=True,stdout=subprocess.DEVNULL)
        child=subprocess.Popen(command+['semantic-result-crash'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if select.select([child.stdout],[],[],1)[0]:
                line=child.stdout.readline()
                if line.strip()=='SEMANTIC_BEFORE_RESULT_COMMIT':break
                if child.poll() is not None:raise AssertionError('worker stopped before commit: '+line)
        else:raise AssertionError('semantic worker did not reach commit barrier')
        assert sql('SELECT (SELECT count(*) FROM "BuilderSemanticResponse"),(SELECT count(*) FROM "BuilderSemanticSuggestion"),(SELECT count(*) FROM "BuilderSemanticPreparation")')=='1|0|1'
        assert sql('SELECT count(*) FROM "BuilderSemanticCall" WHERE "Status"=\'Returned\' AND "InputTokens"=17 AND "OutputTokens"=9 AND "BudgetState"=\'Settled\'')=='1'
        child.kill();assert child.wait(timeout=10)==-signal.SIGKILL;deadline=time.monotonic()+10
        while sql('SELECT count(*) FROM "BackgroundJob" WHERE "Type"=\'BuilderSemanticDecision\' AND "Status"=\'Running\' AND "LeaseExpiresAt">clock_timestamp()')!='0':
            assert time.monotonic()<deadline,'lease did not actually expire';time.sleep(.1)
        subprocess.run(command+['semantic-result-recover'],env=env,check=True)
    finally:
        if child is not None and child.poll() is None:child.kill();child.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
