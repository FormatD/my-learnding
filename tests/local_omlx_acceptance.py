"""Opt-in real local model run, using only a disposable family/database."""
import hashlib,json,os,time
from pathlib import Path

def verify(c):
    root=Path(__file__).resolve().parents[1]
    source=json.loads((root/'.local/textbook-workflow/source-input.json').read_text())
    if os.environ.get('OMLX_FULL_UNIT')!='1':source['text']=source['text'].split('\n')[0];source['title']+='·首个页面实际模型验收'
    s=c.request('/content/sources',source,expected=201)
    run=c.request('/builder/runs',{'sourceId':s['id'],'provider':'LocalOmlx'},expected=202)
    assert run['provider']=='LocalOmlx' and run['model']=='Qwen3.5-9B-MLX-4bit'
    frozen=json.loads(run['modelConfigPayload']);assert frozen['version']=='builder-config/4' and frozen['localTransport']['endpoint']=='http://127.0.0.1:8000/v1'
    assert 'apiKey' not in run['modelConfigPayload']
    deadline=time.monotonic()+250
    while time.monotonic()<deadline:
        data=c.request('/builder');finished=next(x for x in data['runs'] if x['id']==run['id'])
        if finished['status'] in ['Completed','Failed']:break
        time.sleep(1)
    else:raise AssertionError('local model run did not finish')
    assert finished['status']=='Completed',(finished['status'],finished['error'])
    candidates=[x for x in data['candidates'] if x['runId']==run['id']];assert candidates
    chunks={x['id']:x['text'] for x in data['chunks'] if x['sourceId']==s['id']}
    for candidate in candidates:
        p=json.loads(candidate['protocolPayload'])
        assert all(q in chunks[i] for i,q in zip(p['sourceChunkIds'],p['supportingQuotes']))
        assert candidate['status']=='Pending' and candidate['createdDraftId'] is None
    calls=c.request('/builder/calls?runId='+run['id'])['calls']
    actual=[x for x in calls if x['runId']==run['id']];assert actual and all(x['provider']=='LocalOmlx' and x['billingStatus']=='LocalMeasured' and x['budgetState']=='Settled' for x in actual)
    result={'scope':'DisposableDatabase','source':s,'run':finished,'candidates':candidates,'calls':actual,'humanReview':'Pending'}
    output=root/('.local/textbook-workflow/actual-model-full-unit-result.json' if os.environ.get('OMLX_FULL_UNIT')=='1' else '.local/textbook-workflow/actual-model-result.json');output.write_text(json.dumps(result,ensure_ascii=False,indent=2));output.chmod(0o600)
    print('PASS real local oMLX queued run, fixed model/transport snapshot, actual call ledger, exact source quotations and pending-only candidates:',len(candidates),'candidates;',len(actual),'calls')
