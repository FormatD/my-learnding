"""Actual cancellation middleware, future-queued controlled jobs and role isolation."""
import hashlib,json,select,subprocess,time,uuid
from api_acceptance import Client

def verify(c,env):
    me=c.request('/me');family=me['family']['id'];account=me['actor']['accountId'];learner=c.request('/students',{'name':'取消接口受控队列'},expected=201);student=learner['id'];requests={}
    def sql(q):return subprocess.check_output(['psql','-At','-v','ON_ERROR_STOP=1','-c',q],env=env,text=True).strip()
    def job(kind='AssessmentRebuild',status='Queued'):
        id=str(uuid.uuid4());ref=str(uuid.uuid4());target=str(uuid.uuid4());payload='{}'
        if kind=='AssessmentRebuild' and status=='Queued':
            payload=json.dumps(dict(version='assessment-rebuild/1',inputMode='LatestCommittedUnderLock',familyId=family,studentId=student,requestedBy=account,targetGenerationId=target,timeZone=learner['timeZone'],inputVersion='assessment-input/2',evidenceRule='evidence/1.1',masteryModel='mastery/1.1',reviewRule='review/1',baseGenerationId=None,baseInputHash=None,baseCursor=None),separators=(',',':'));requests[id]=(ref,target,payload)
        hash=hashlib.sha256(payload.encode()).hexdigest()
        sql(f'''INSERT INTO "BackgroundJob" ("Id","FamilyId","CreatedAt","Type","StudentId","TargetGenerationId","InputRef","IdempotencyKey","InputPayload","InputHash","Status","AttemptCount","MaxAttempts","RetryRound","NextRunAt","LeaseSeconds","HeartbeatSeconds") VALUES ('{id}','{family}',clock_timestamp(),'{kind}','{student}','{target}','{ref}','cancel:{id}','{payload}','{hash}','{status}',0,4,0,clock_timestamp()+interval '1 hour',30,5)''')
        if id in requests:sql(f'''INSERT INTO "AssessmentRebuildRequest" ("Id","FamilyId","CreatedAt","StudentId","JobId","RequestedBy","TargetGenerationId","Snapshot","SnapshotHash","Reason") VALUES ('{ref}','{family}',clock_timestamp(),'{student}','{id}','{account}','{target}','{payload}','{hash}','controlled queued cancellation request')''')
        return id

    id=job();c.request('/students/'+student+'/mastery:full-rebuild',{'reason':'不能替换正在等待的普通请求'},expected=409);path='/background-jobs/'+id+':cancel';c.request(path,{'reason':''},expected=422)
    # Keep the real family work lock held; a cancellation must use its independent control transaction.
    lock=subprocess.Popen(['psql','-At','-v','ON_ERROR_STOP=1'],env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    try:
        lock.stdin.write(f"BEGIN; SELECT pg_advisory_xact_lock(hashtextextended('{family}',0));\n\\echo FAMILY_LOCK_HELD\nSELECT pg_sleep(10);\n");lock.stdin.flush();deadline=time.monotonic()+5
        while time.monotonic()<deadline:
            if select.select([lock.stdout],[],[],.2)[0] and lock.stdout.readline().strip()=='FAMILY_LOCK_HELD':break
        else:raise AssertionError('family work lock not confirmed')
        key=str(uuid.uuid4());reason={'reason':'受控队列取消依据'};started=time.monotonic();result=c.request(path,reason,key=key);assert time.monotonic()-started<2 and result['status']=='Cancelled' and result['executionStopConfirmed'] and not result['alreadyCancelled']
        assert c.request(path,reason,key=key)==result
        c.request(path,{'reason':'同键换依据'},key=key,expected=409)
        assert c.request(path,reason)['alreadyCancelled']
    finally:
        lock.terminate();lock.communicate(timeout=5)
        sql(f"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name='psql' AND datname=current_database() AND pid<>pg_backend_pid() AND query LIKE '%pg_sleep(10)%'")
    assert sql(f'''SELECT count(*) FROM "Audits" WHERE "FamilyId"='{family}' AND "Action"='BackgroundJobCancellationRequested' AND "Details"::jsonb->>'jobId'='{id}' ''')=='1'
    assert sql(f'''SELECT count(*) FROM "JobLeaseAttempt" WHERE "JobId"='{id}' ''')=='0'
    c.request('/background-jobs/'+job(status='Succeeded')+':cancel',reason,expected=409)
    c.request('/background-jobs/'+job('AssessmentProjection')+':cancel',reason,expected=422)
    other=Client();other.request('/auth/register',{'userName':'cancel-other-'+uuid.uuid4().hex[:12],'password':'test-'+uuid.uuid4().hex},expected=201);other.request(path,reason,expected=404)
    # Cached command must recheck the specific current role before returning an old response.
    sql(f'''UPDATE "FamilyMembership" SET "Roles"='ContentEditor' WHERE "FamilyId"='{family}' AND "AccountId"='{account}' ''');c.request(path,reason,key=key,expected=403)
    sql(f'''UPDATE "FamilyMembership" SET "Roles"='Parent,ContentEditor,Publisher' WHERE "FamilyId"='{family}' AND "AccountId"='{account}' ''')
    for kind in ['BuilderCandidates','ParsePDF','MappingSuggestions']:
        queued=job(kind);assert c.request('/background-jobs/'+queued+':cancel',reason)['executionStopConfirmed']
    ref,target,payload=requests[id];route='/students/'+student+'/rebuild-requests/'+ref
    resumed=c.request(route+':retry',{'reason':'明确恢复原取消请求'},expected=202);assert resumed['jobId']==id and resumed['targetGenerationId']==target and resumed['retryRound']==1
    for _ in range(100):
        current=c.request(route)
        if current['status']=='Succeeded':break
        assert current['status'] not in ['Failed','Cancelled'];time.sleep(.1)
    else:raise AssertionError('explicit cancelled rebuild retry did not settle')
    assert current['result']['generationId']==target
    exported=c.request('/students/'+student+'/export');assert len(exported['rebuildResults'])==1 and any(a['action']=='BackgroundJobCancellationRequested' and json.loads(a['details'])['reason']==reason['reason'] for a in exported['studentAudit'])
    assert sql(f'''SELECT "Snapshot" FROM "AssessmentRebuildRequest" WHERE "Id"='{ref}' ''')==payload
    c.request(path,reason,expected=409)
    c.request('/students/'+student+'/child-sessions',{});c.request(path,reason,expected=403)
    print('PASS actual cancel bypasses held family work lock, persisted idempotency/reason, zero fictitious claims, terminal/type/role/family isolation and cached role recheck; explicit retry keeps frozen target/input and exports cancellation reason')
