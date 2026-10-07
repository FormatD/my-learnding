"""HTTP privacy/owner/export gates using explicit synthetic ledger rows, not model quality evidence."""
import copy,hashlib,io,json,re,subprocess,uuid,zipfile
import api_acceptance

def verify(c,env,other=None,child=None):
 assert re.fullmatch(r'learning_fault_openapi_[0-9a-f]{12}',env['PGDATABASE'])
 me=c.request('/me');fid=str(uuid.UUID(me['family']['id']));owner=str(uuid.UUID(me['family']['ownerAccountId']))
 library=next(r for r in reversed(c.request('/content')['releases']) if not r['withdrawn'])
 source=c.request('/content/drafts',{'title':'受控映射账本接口夹具','catalog':json.loads(library['payload'])});catalog=json.loads(source['payload']);question=catalog['questions'][0]
 prep=c.request('/builder/mapping-preparations',{'draftId':source['id'],'libraryReleaseId':library['id'],'provider':'Mock','owners':[{'ownerType':'Question','ownerId':question['id'],'ownerRevisionId':question['revisionId']}]},expected=202)
 call_id=str(uuid.uuid4());execution=str(uuid.uuid4());pid=str(uuid.UUID(prep['id']))
 def sql(q):return subprocess.check_output(['psql','-v','ON_ERROR_STOP=1','-Atc',q],env=env,text=True).strip()
 def literal(value):return "'"+value.replace("'","''")+"'"
 payload=json.dumps({'scope':'Explicit controlled API fixture; not an actual model request'})
 digest=hashlib.sha256(payload.encode()).hexdigest()
 sql(f'''INSERT INTO "MappingModelCall" ("Id","FamilyId","PreparationId","ExecutionId","RetryRound","AttemptNumber","CallNumber","Model","InputHash","ModelConfigHash","ModelConfigPayload","InputPayload","Status","StartedAt","FinishedAt","ElapsedMilliseconds","BillingStatus","BudgetDay","BudgetSnapshot","BudgetState","CreatedAt","InputTokens") VALUES ('{call_id}','{fid}','{pid}','{execution}',0,1,1,'controlled-api-fixture','{digest}','{digest}',{literal(payload)},{literal(payload)},'Failed',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,0,'Unknown',CURRENT_DATE,'{{}}','Unresolved',CURRENT_TIMESTAMP,11);''')
 window=c.request('/builder/mapping-calls?preparationId='+pid);raw=next(x for x in window['calls'] if x['id']==call_id);assert raw['inputTokens']==11 and raw['outputTokens'] is None and raw['chargedCost'] is None
 c.request('/builder/mapping-calls?page=0',expected=422);c.request('/builder/mapping-calls?pageSize=51',expected=422);c.request('/builder/mapping-calls?preparationId='+str(uuid.uuid4()),expected=404)
 assert c.request('/builder/budget')['state']['activeCalls']==1
 evidence={'providerFinished':True,'inputTokens':11,'outputTokens':None,'reason':'仅核对受控接口夹具，非真实模型质量结果','receiptReference':'controlled synthetic row fixture'}
 if other is not None:
  assert other.request('/builder/mapping-calls')['total']==0
  other.request('/builder/mapping-calls?preparationId='+pid,expected=404);other.request('/builder/mapping-calls/'+call_id+':reconcile',evidence,expected=404)
 if child is not None:
  # Earlier role-change regression intentionally revoked child sessions; create a fresh actual child session.
  child.jar.clear()
  for cookie in c.jar:child.jar.set_cookie(copy.copy(cookie))
  child.etag=c.etag;student=c.request('/students')[0];child.request('/students/'+student['id']+'/child-sessions',{})
  child.request('/builder/mapping-calls',expected=403);child.request('/builder/mapping-calls/'+call_id+':reconcile',evidence,expected=403)
 c.request('/builder/mapping-calls/'+call_id+':reconcile',{**evidence,'providerFinished':False},expected=422)
 c.request('/builder/mapping-calls/'+call_id+':reconcile',{**evidence,'inputTokens':12},expected=422)
 try:
  sql(f'''UPDATE "Families" SET "OwnerAccountId"=NULL WHERE "Id"='{fid}';''')
  c.request('/builder/mapping-calls/'+call_id+':reconcile',evidence,expected=403)
 finally:sql(f'''UPDATE "Families" SET "OwnerAccountId"='{owner}' WHERE "Id"='{fid}';''')
 decision=c.request('/builder/mapping-calls/'+call_id+':reconcile',evidence,expected=201);assert decision['callId']==call_id and decision['inputTokens']==11 and decision['outputTokens'] is None
 c.request('/builder/mapping-calls/'+call_id+':reconcile',evidence,expected=409)
 after=c.request('/builder/mapping-calls?preparationId='+pid);assert next(x for x in after['calls'] if x['id']==call_id)==raw and any(x['id']==decision['id'] for x in after['reconciliations']);assert c.request('/builder/budget')['state']['activeCalls']==0
 with c.http.open(api_acceptance.BASE+'/family/export') as response,zipfile.ZipFile(io.BytesIO(response.read())) as archive:data=json.loads(archive.read('manifest.json'))['data']
 assert next(x for x in data['MappingModelCall'] if x['id']==call_id)==raw and any(x['id']==decision['id'] for x in data['MappingCallReconciliation'])
 print('PASS mapping ledger HTTP page/input/owner gates, append-only reconciliation with immutable real-known fields and unknown gaps, shared budget and full family export; explicitly synthetic API rows')
