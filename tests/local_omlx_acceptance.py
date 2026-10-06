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
    if os.environ.get('OMLX_RELIABILITY')=='1':
        regressions=[]
        for text in ['48 832 8 64 9/54 981 742 6 48 545 3','7 余数要比 除数','52-8=']:
            noisy=c.request('/content/sources',{'title':'未经校对的竖式OCR回归','text':'北师大版二年级下册，第一单元除法。未校对OCR：'+text,'allowExternalAI':False,'usageScope':'FamilyOnly'},expected=201)
            task=c.request('/builder/runs',{'sourceId':noisy['id'],'provider':'LocalOmlx'},expected=202)
            deadline=time.monotonic()+250
            while time.monotonic()<deadline:
                current=c.request('/builder');done=next(x for x in current['runs'] if x['id']==task['id'])
                if done['status'] in ['Completed','Failed']:break
                time.sleep(1)
            else:raise AssertionError('OCR regression task did not finish')
            extracted=[x for x in current['candidates'] if x['runId']==task['id']]
            assert done['status']=='Completed' and not extracted,('Unclear OCR produced unsupported candidates',text,done,extracted)
            ledger=c.request('/builder/calls?runId='+task['id'])['calls']
            assert ledger and all(x['status']=='Returned' and x['budgetState']=='Settled' and x['billingStatus']=='LocalMeasured' for x in ledger)
            regressions.append({'input':text,'run':done,'calls':ledger,'candidates':extracted})
        dest=root/'.local/textbook-workflow/actual-ocr-regression-result.json';dest.write_text(json.dumps(regressions,ensure_ascii=False,indent=2));dest.chmod(0o600)
        print('PASS real local oMLX: three unclear OCR fragments abstain without inventing mathematical interpretation')
