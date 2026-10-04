"""Exercise the structured Builder gate through a real disposable service."""
import hashlib,json,time

def verify(c):
    source=c.request('/content/sources',{'title':'结构协议验收','text':'先乘除后加减。\n遇到括号先算括号里面。'},expected=201)
    run=c.request('/builder/runs',{'sourceId':source['id']},expected=202)
    assert run['inputVersion']=='builder-input/4' and hashlib.sha256(run['modelConfigPayload'].encode()).hexdigest()==run['modelConfigHash']
    config=json.loads(run['modelConfigPayload']);assert config['version']=='builder-config/3' and config['promptVersion']=='kc-candidate/2' and config['limits']['timeoutMilliseconds']==30000 and config['limits']['repairAttempts']==1
    def finished(id):
        for _ in range(150):
            state=c.request('/builder');item=next(r for r in state['runs'] if r['id']==id)
            if item['status']!='Queued':return state,item
            time.sleep(.05)
        raise AssertionError('Builder did not reach terminal state')
    state,item=finished(run['id']);assert item['status']=='Completed'
    candidates=[x for x in state['candidates'] if x['runId']==run['id']];assert len(candidates)==2
    chunks={x['id']:x for x in state['chunks'] if x['sourceId']==source['id']}
    for candidate in candidates:
        payload=json.loads(candidate['protocolPayload']);assert payload['sourceChunkIds']==[candidate['chunkId']] and payload['supportingQuotes']==[candidate['quote']] and candidate['quote'] in chunks[candidate['chunkId']]['text'] and payload['gradeMin']==1 and payload['gradeMax']==12 and payload['modelScore']==0
    attempts=[x for x in state['attempts'] if x['runId']==run['id']];assert len(attempts)==1
    result=json.loads(attempts[0]['protocolResult']);assert result['calls']==1 and result['repaired'] is False and result['output']['schemaVersion']=='kc-candidate/2'
    calls=c.request('/builder/calls?runId='+run['id']);assert calls['total']==1;call=calls['calls'][0];assert call['status']=='Returned' and call['billingStatus']=='LocalNoCharge' and call['chargedCost']==0 and call['inputTokens'] is None and call['outputTokens'] is None and call['currency'] is None and call['modelConfigHash']==run['modelConfigHash'] and call['outputHash'] and call['elapsedMilliseconds']>=0
    c.request('/builder/calls?page=0',expected=422);c.request('/builder/calls?pageSize=51',expected=422)
    assert c.request('/builder/runs',{'sourceId':source['id']})['id']==run['id']
    state=c.request('/builder');assert len([x for x in state['attempts'] if x['runId']==run['id']])==1
    print('PASS real Builder validates and persists complete structured candidates with actual local calls, source pairs and reuse; no invented quality score')
    source=c.request('/content/sources',{'title':'片段超限验收','text':'\n'.join('独立片段'+str(i) for i in range(101))},expected=201)
    run=c.request('/builder/runs',{'sourceId':source['id']},expected=202);state,item=finished(run['id'])
    assert item['status']=='Failed' and item['error']=='BUILDER_INPUT_LIMIT' and item['retries']==0 and not any(x['runId']==run['id'] for x in state['candidates'])
    attempt=next(x for x in state['attempts'] if x['runId']==run['id']);assert attempt['status']=='Failed' and attempt['protocolResult'] is None
    c.request('/builder/runs/'+run['id']+':retry',{'reason':'超过输入上限不能借重试绕过'},expected=422)
    print('PASS 101 fragments explicitly fail without silently skipping source, partial candidates or automatic/manual retry bypass')
