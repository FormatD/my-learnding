"""Check actual repaired and unrepaired terminal jobs and their durable call ledger."""
import os,json,secrets,socket,subprocess,sys,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);env['ConnectionStrings__Learning']=env['PERSISTENCE_TEST_CONNECTION'];env.pop('BackupConfigFile',None);env.update(REPAIR_REPORT_USER='repair-report-'+uuid.uuid4().hex[:12],REPAIR_REPORT_PASSWORD=secrets.token_hex(24));service=None
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        fixture=json.loads(subprocess.check_output([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'builder-repair-report-seed'],env=env,text=True).splitlines()[-1])
        with tempfile.TemporaryDirectory(prefix='builder-report-service-') as directory:
            work=Path(directory);env.update(DeletionLedger=str(work/'students'),FamilyDeletionLedger=str(work/'families'),ExportDirectory=str(work/'exports'))
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            origin=f'http://127.0.0.1:{port}'
            with (work/'api.log').open('w+') as log:
                service=subprocess.Popen([dotnet(root),str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT);deadline=time.monotonic()+40
                while time.monotonic()<deadline:
                    assert service.poll() is None,'Disposable Builder service stopped'
                    try:
                        with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('Builder service did not become healthy')
                api_acceptance.BASE=origin+'/api/v1';c=api_acceptance.Client();c.request('/auth/login',{'userName':fixture['userName'],'password':env['REPAIR_REPORT_PASSWORD']});c.request('/me')
                for id in (fixture['successRunId'],fixture['failedRunId'],fixture['disabledRunId']):
                    ledger=c.request('/builder/calls?runId='+id);calls=sorted(ledger['calls'],key=lambda r:r['callNumber']);assert ledger['total']==(1 if id==fixture['disabledRunId'] else 2) and [r['callNumber'] for r in calls]==([1] if id==fixture['disabledRunId'] else [1,2]) and [r['repair'] for r in calls]==([False] if id==fixture['disabledRunId'] else [False,True]) and len({r['executionId'] for r in calls})==1 and all(r['status']=='Returned' and r['billingStatus']=='LocalNoCharge' and r['chargedCost']==0 and r['budgetState']=='Settled' for r in calls)
                context=c.request('/builder/candidates/'+fixture['candidateId']+'/review-context');assert context['protocol']['name']=='受控修复后的能力' and context['candidate']['status']=='Pending'
                sys.path.insert(0,str(root/'scripts'));import k1_builder_report
                state=c.request('/builder');ids=[r['id'] for r in state['runs']];result=k1_builder_report.report(state,ids)
                assert result['counts']['completed']==1 and result['counts']['failed']==2 and result['counts']['queued']==0 and result['counts']['candidates']==1 and result['counts']['completedProtocolUnknown']==0
                assert result['firstSchemaSuccess']=={'numerator':0,'denominator':1,'rate':0} and result['repairedSchemaSuccess']=={'numerator':1,'denominator':1,'rate':1} and result['sourceTraceability']=={'numerator':1,'denominator':1,'rate':1} and not result['formalV1ExitProven'] and result['semanticQuality']=='NotEvaluated'
                capture=work/'capture.json';output=work/'report.json';capture.write_text(json.dumps(state));args=['python3',str(root/'scripts/k1_builder_report.py'),'--builder',str(capture),'--output',str(output)]
                for id in ids:args+=['--run-id',id]
                subprocess.run(args,check=True,capture_output=True);assert json.loads(output.read_text())==result
                again=subprocess.run(args,capture_output=True);assert again.returncode!=0 and json.loads(output.read_text())==result
                failed=next(r for r in state['runs'] if r['id']==fixture['failedRunId']);assert failed['error']=='BUILDER_NEEDS_REPAIR' and failed['retries']==0 and not any(r['runId']==failed['id'] for r in state['candidates'])
                assert len(state['attempts'])==3 and next(a for a in state['attempts'] if a['runId']==failed['id'])['protocolResult'] is None
                other=api_acceptance.Client();other.request('/auth/register',{'userName':'repair-other-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);other.request('/builder/candidates/'+fixture['candidateId']+'/review-context',expected=404)
                student=c.request('/students',{'name':'受控修复验收'},expected=201);export=c.request('/students/'+student['id']+'/export');assert export['evidence']==[] and export['mastery']==[];c.request('/students/'+student['id']+'/child-sessions',{});c.request('/builder/candidates/'+fixture['candidateId']+'/review-context',expected=403)
                print('PASS actual queued worker repair: exactly two calls, same fixed schema and invalid first response; repaired candidate/attempt retained, unrepaired failure has zero candidates and no further retry')
                print('PASS actual API report first=0/1 repair=1/1 trace=1/1 and failed=2; frozen zero-repair run calls only once; returned call ledger is not schema success; CLI, family404, child403 and zero evidence checked; controlled fixture is not model quality')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
