"""Owner budget changes and append-only reconciliation in a guarded disposable service."""
import io,json,re,subprocess,uuid,zipfile
import api_acceptance

def verify(c,env):
    assert re.fullmatch('learning_fault_openapi_[0-9a-f]{12}',env['PGDATABASE'])
    me=c.request('/me');family=me['family'];fid=str(uuid.UUID(family['id']));owner=str(uuid.UUID(family['ownerAccountId']))
    def sql(q):return subprocess.check_output(['psql','-Atc',q],env=env,text=True).strip()
    original=c.request('/builder/budget');assert original['window']=='UTC' and original['state']['costCommitted']==0
    body={'dailyCostLimit':.5,'dailyTokenLimit':1000,'perCallCostLimit':.2,'perCallTokenLimit':200,'maxConcurrentCalls':2,'reason':'受控接口预算验收'}
    saved=c.request('/builder/budget',body,method='PUT');assert saved['dailyCostLimit']==.5 and saved['maxConcurrentCalls']==2
    assert sql(f'''SELECT count(*) FROM "Audits" WHERE "FamilyId"='{fid}' AND "Action"='BuilderBudgetChanged' AND "Details"::jsonb->>'reason'='受控接口预算验收';''')=='1'
    c.request('/builder/budget',{**body,'perCallCostLimit':.6},method='PUT',expected=422);assert c.request('/builder/budget')['policy']==saved
    try:
        sql(f'''UPDATE "Families" SET "OwnerAccountId"=NULL WHERE "Id"='{fid}';''')
        c.request('/builder/budget',body,method='PUT',expected=403)
    finally:sql(f'''UPDATE "Families" SET "OwnerAccountId"='{owner}' WHERE "Id"='{fid}';''')
    print('PASS owner budget save/audit and non-owner/invalid changes reject without partial policy mutation')
    call=c.request('/builder/calls')['calls'][0];raw_id=str(uuid.UUID(call['id']))
    sql(f'''UPDATE "BuilderCall" SET "Status"='Started',"FinishedAt"=NULL,"BillingStatus"='Unknown',"ChargedCost"=NULL,"Currency"=NULL,"InputTokens"=NULL,"OutputTokens"=NULL,"BudgetState"='Unresolved' WHERE "Id"='{raw_id}';''')
    before=c.request('/builder/calls?runId='+call['runId']);raw=next(x for x in before['calls'] if x['id']==raw_id)
    evidence={'providerFinished':False,'chargedCost':0,'currency':None,'inputTokens':None,'outputTokens':None,'reason':'已核对受控本地进程结束','receiptReference':'disposable fault fixture only'}
    c.request('/builder/calls/'+raw_id+':reconcile',evidence,expected=422)
    decision=c.request('/builder/calls/'+raw_id+':reconcile',{**evidence,'providerFinished':True},expected=201);assert decision['source']=='ParentConfirmed' and decision['callId']==raw_id
    c.request('/builder/calls/'+raw_id+':reconcile',{**evidence,'providerFinished':True},expected=409)
    after=c.request('/builder/calls?runId='+call['runId']);assert next(x for x in after['calls'] if x['id']==raw_id)==raw and any(x['id']==decision['id'] for x in after['reconciliations'])
    assert c.request('/builder/budget')['state']['activeCalls']==0
    with c.http.open(api_acceptance.BASE+'/family/export') as response,zipfile.ZipFile(io.BytesIO(response.read())) as archive:
        exported=json.loads(archive.read('manifest.json'))['data']
    assert any(x['id']==decision['id'] and x['source']=='ParentConfirmed' for x in exported['BuilderBudgetReconciliation'])
    assert any(x['id']==saved['id'] for x in exported['BuilderBudgetPolicy'])
    assert next(x for x in exported['BuilderCall'] if x['id']==raw_id)==raw
    print('PASS unfinished call requires explicit finish/evidence; owner appends one reconciliation, releases slot without rewriting original call facts')
    partial=str(uuid.uuid4());execution=str(uuid.uuid4())
    sql(f'''INSERT INTO "BuilderCall" ("Id","FamilyId","RunId","ExecutionId","RetryRound","AttemptNumber","CallNumber","Repair","Provider","Model","InputHash","ModelConfigHash","Status","StartedAt","BillingStatus","BudgetDay","ReservedCost","ReservedTokens","QuotePayload","BudgetState","CreatedAt","InputTokens") SELECT '{partial}',"FamilyId","RunId",'{execution}',"RetryRound","AttemptNumber",1,false,"Provider","Model","InputHash","ModelConfigHash",'Returned',"StartedAt",'Unknown',"BudgetDay",0.1,50,'{{"maxCost":0.1,"maxTokens":50,"currency":"USD","localNoCharge":false}}','Unresolved',"CreatedAt",11 FROM "BuilderCall" WHERE "Id"='{raw_id}';''')
    missing_fee={'providerFinished':True,'chargedCost':.1,'currency':'USD','inputTokens':12,'outputTokens':3,'reason':'受控部分用量核对','receiptReference':'partial usage fixture only'}
    c.request('/builder/calls/'+partial+':reconcile',missing_fee,expected=422)
    c.request('/builder/calls/'+partial+':reconcile',{**missing_fee,'inputTokens':11},expected=201)
    original=next(x for x in c.request('/builder/calls')['calls'] if x['id']==partial)
    assert original['billingStatus']=='Unknown' and original['inputTokens']==11 and original['outputTokens'] is None and original['chargedCost'] is None
    print('PASS partial usage stays immutable even when fee is unknown; reconciliation fills only the missing facts')
