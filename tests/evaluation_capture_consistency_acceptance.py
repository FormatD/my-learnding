#!/usr/bin/env python3
"""Force a commit between actual HTTP response queries using disposable PG relation locks."""
import argparse,concurrent.futures,hashlib,json,queue,secrets,socket,subprocess,tempfile,threading,time,uuid,urllib.request,urllib.parse
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet
ROOT=Path(__file__).resolve().parents[1]
def literal(value):return "'"+str(value).replace("'","''")+"'"
def query(env,sql):return subprocess.check_output(['psql','-X','-At','-v','ON_ERROR_STOP=1','-c',sql],env=env,text=True).strip()
class Writer:
    def __init__(self,env,table,work):
        assert table in ('Candidates','MappingReviewDecision')
        self.error=(work/('writer-'+table+'.log')).open('w');self.lines=queue.Queue();self.proc=subprocess.Popen(['psql','-X','-Atq','-v','ON_ERROR_STOP=1'],env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.error,text=True,bufsize=1)
        def drain():
            for line in self.proc.stdout:self.lines.put(line.strip())
            self.lines.put(None)
        self.thread=threading.Thread(target=drain,daemon=True);self.thread.start();self.send('BEGIN; SELECT pg_backend_pid();');self.pid=int(self.next());self.send(f'LOCK TABLE "{table}" IN ACCESS EXCLUSIVE MODE; SELECT \'LOCK_READY\';');self.until('LOCK_READY')
    def send(self,sql):self.proc.stdin.write(sql+'\n');self.proc.stdin.flush()
    def next(self):
        value=self.lines.get(timeout=15);assert value is not None,'Controlled writer terminated';return value
    def until(self,token):
        while self.next()!=token:pass
    def commit(self,sql):self.send(sql+"; COMMIT; SELECT 'COMMIT_DONE';");self.until('COMMIT_DONE')
    def close(self):
        if self.proc.poll() is None:
            self.send('ROLLBACK; \\q');self.proc.stdin.close();self.proc.wait(timeout=10)
        self.thread.join(timeout=5);self.proc.stdout.close();self.error.close()
def blocked(env,writer,table,future):
    deadline=time.monotonic()+15
    while time.monotonic()<deadline:
        assert not future.done(),'HTTP read did not reach the relation barrier'
        sql=f"SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND {writer.pid}=ANY(pg_blocking_pids(pid)) AND query LIKE '%FROM \"{table}\"%'"
        if query(env,sql)=='1':return
        time.sleep(.05)
    raise AssertionError('Actual API query did not block at the controlled relation')
def aborted_read(client,env,origin,path,table,work):
    writer=Writer(env,table,work);wire=None
    try:
        request=urllib.request.Request(origin+'/api/v1'+path);client.jar.add_cookie_header(request);cookie=request.get_header('Cookie');assert cookie,'Controlled session cookie unavailable'
        address=urllib.parse.urlparse(origin);wire=socket.create_connection((address.hostname,address.port),timeout=10)
        wire.sendall(('GET /api/v1'+path+' HTTP/1.1\r\nHost: '+address.netloc+'\r\nCookie: '+cookie+'\r\nX-Learning-Request: 1\r\nConnection: close\r\n\r\n').encode())
        deadline=time.monotonic()+15;pid=None
        while time.monotonic()<deadline:
            result=query(env,f"SELECT pid FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND {writer.pid}=ANY(pg_blocking_pids(pid)) AND query LIKE '%FROM \"{table}\"%'")
            if result.isdigit():pid=int(result);break
            time.sleep(.05)
        assert pid is not None,'Actual abort probe did not reach the relation barrier'
        wire.shutdown(socket.SHUT_RDWR);wire.close();wire=None;deadline=time.monotonic()+10
        while time.monotonic()<deadline:
            if query(env,f"SELECT count(*) FROM pg_stat_activity WHERE pid={pid} AND xact_start IS NOT NULL")=='0':break
            time.sleep(.05)
        else:raise AssertionError('Aborted HTTP snapshot kept its database transaction while the writer lock remained held')
    finally:
        if wire is not None:wire.close()
        writer.close()
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--expect-mixed',action='store_true');args=parser.parse_args();database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);env['ConnectionStrings__Learning']=env['PERSISTENCE_TEST_CONNECTION'];env.pop('BackupConfigFile',None);env.update(REPAIR_REPORT_USER='capture-'+uuid.uuid4().hex[:12],REPAIR_REPORT_PASSWORD=secrets.token_hex(24));service=None
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        fixture=json.loads(subprocess.check_output([dotnet(ROOT),str(ROOT/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'builder-repair-report-seed'],env=env,text=True).splitlines()[-1])
        with tempfile.TemporaryDirectory(prefix='capture-consistency-') as directory:
            work=Path(directory);env.update(DeletionLedger=str(work/'students'),FamilyDeletionLedger=str(work/'families'),ExportDirectory=str(work/'exports'))
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            origin=f'http://127.0.0.1:{port}'
            with (work/'api.log').open('w') as log:
                service=subprocess.Popen([dotnet(ROOT),str(ROOT/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=ROOT/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT);deadline=time.monotonic()+40
                while time.monotonic()<deadline:
                    assert service.poll() is None,'Disposable service stopped'
                    try:
                        with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('Service not healthy')
                api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/login',{'userName':fixture['userName'],'password':env['REPAIR_REPORT_PASSWORD']});c.request('/me');before=c.request('/builder');candidate=next(r for r in before['candidates'] if r['id']==fixture['candidateId']);run=next(r for r in before['runs'] if r['id']==fixture['successRunId']);source=next(r for r in before['sources'] if r['id']==run['sourceId']);marker='受控同事务提交后标记，非正式审核';old_builder_version=c.etag
                # Controlled metadata transaction, not a claimed human review or historical fixture.
                writer=Writer(env,'Candidates',work)
                try:
                    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
                        future=executor.submit(c.request,'/builder');blocked(env,writer,'Candidates',future)
                        writer.commit('UPDATE "Sources" SET "Title"='+literal(marker)+' WHERE "Id"='+literal(source['id'])+'; UPDATE "Candidates" SET "ReviewReason"='+literal(marker)+' WHERE "Id"='+literal(candidate['id'])+'; UPDATE "Families" SET "Version"="Version"+1 WHERE "Id"='+literal(run['familyId']))
                        captured=future.result(timeout=15)
                finally:writer.close()
                assert c.etag==old_builder_version,'Builder version moved beyond captured snapshot'
                captured_source=next(r for r in captured['sources'] if r['id']==source['id']);captured_candidate=next(r for r in captured['candidates'] if r['id']==candidate['id']);assert captured_source['title']==source['title'];assert captured_candidate['reviewReason']==(marker if args.expect_mixed else candidate['reviewReason'])
                after=c.request('/builder');assert c.etag!=old_builder_version;assert next(r for r in after['sources'] if r['id']==source['id'])['title']==marker and next(r for r in after['candidates'] if r['id']==candidate['id'])['reviewReason']==marker
                m=Client();m.request('/auth/register',{'userName':'capture-mapping-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);me=m.request('/me');draft=m.request('/content/unit-pack',{});m.request('/content/drafts/'+draft['id']+':review',{});preview=m.request('/content/drafts/'+draft['id']+'/preview');release=m.request('/content/drafts/'+draft['id']+':publish',{'previewHash':preview['hash']});question=json.loads(draft['payload'])['questions'][0]
                prepared=m.request('/builder/mapping-preparations',{'draftId':draft['id'],'libraryReleaseId':release['id'],'owners':[{'ownerType':'Question','ownerId':question['id'],'ownerRevisionId':question['revisionId']}],'provider':'Mock'},expected=202)
                for _ in range(150):
                    state=m.request('/builder/mapping-preparations/'+prepared['id'])
                    if state['status']=='Succeeded':break
                    assert state['status'] in ('Queued','Running');time.sleep(.1)
                else:raise AssertionError('Mapping not ready')
                path='/builder/mapping-runs/'+state['runId'];detail=m.request(path);suggestion=detail['suggestions'][0];old_mapping_version=m.etag;assert suggestion['status']=='Pending' and detail['decisions']==[];writer=Writer(env,'MappingReviewDecision',work)
                original='{"evidencePolicy":'+json.dumps(suggestion['evidencePolicy'])+',"items":'+suggestion['suggestedItems']+'}';original_hash=hashlib.sha256(original.encode()).hexdigest()
                try:
                    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
                        future=executor.submit(m.request,path);blocked(env,writer,'MappingReviewDecision',future)
                        values=[literal(uuid.uuid4()),literal(suggestion['familyId']),'now()',literal(suggestion['id']),literal('Reject'),literal(me['actor']['accountId']),'now()',literal(marker),literal(original_hash),literal(''),literal(''),'NULL']
                        writer.commit('UPDATE "MappingSuggestion" SET "Status"=\'Rejected\',"Version"="Version"+1 WHERE "Id"='+literal(suggestion['id'])+'; INSERT INTO "MappingReviewDecision" ("Id","FamilyId","CreatedAt","SuggestionId","Decision","ReviewerId","ReviewedAt","Reason","OriginalPayloadHash","CorrectedPayload","CorrectedPayloadHash","CreatedDraftId") VALUES ('+','.join(values)+'); UPDATE "Families" SET "Version"="Version"+1 WHERE "Id"='+literal(suggestion['familyId']))
                        captured=future.result(timeout=15)
                finally:writer.close()
                assert m.etag==old_mapping_version,'Mapping version moved beyond captured snapshot'
                assert captured['suggestions'][0]['status']=='Pending';assert len(captured['decisions'])==(1 if args.expect_mixed else 0);assert captured['quality']['pending']==1 and captured['quality']['rejected']==(1 if args.expect_mixed else 0)
                after=m.request(path);assert m.etag!=old_mapping_version;assert after['suggestions'][0]['status']=='Rejected' and len(after['decisions'])==1 and after['quality']['pending']==0 and after['quality']['rejected']==1
                if not args.expect_mixed:
                    aborted_read(c,env,origin,'/builder','Candidates',work);aborted_read(m,env,origin,path,'MappingReviewDecision',work)
                    print('PASS actual client disconnect cancels both blocked reads and disposes database transactions before relation locks are released')
                print('PASS actual relation-barrier HTTP captures: '+('old read-committed service mixed pre/post transaction rows' if args.expect_mixed else 'Builder and mapping detail retain pre-commit rows; subsequent reads see complete committed state'))
                print('PASS controlled temporary metadata/reject transactions, not real human quality evidence; writer locks released, no product model calls')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
