"""Page genuine committed learning events/receipts while a new attempt is consumed."""
import base64,copy,datetime,json,time,uuid
from urllib.parse import quote
import api_acceptance

def verify(c):
    release=next(r for r in c.request('/content')['releases'] if not r['withdrawn'] and any(q['type']=='Numeric' for q in json.loads(r['payload'])['questions']))
    student=c.request('/students',{'name':'事件回执游标验收'},expected=201);sid=student['id'];other=c.request('/students',{'name':'事件筛选隔离'},expected=201)
    c.request('/students/'+sid+'/content/'+release['id']+':bind',{})
    day=api_acceptance.TODAY;plan=c.request('/students/'+sid+'/plans/'+day+':generate',{});q=next(q for q in json.loads(release['payload'])['questions'] if q['type']=='Numeric')
    task=c.request('/plans/'+plan['id']+'/tasks',{'title':'实际事件回执验收','minutes':5,'resourceRef':'原创验收题','type':'Practice','questionId':q['id']})
    view=c.request('/students/'+sid+'/plans/'+day);c.request('/plans/'+plan['id']+':publish',{'previewHash':view['revision']['inputHash'],'confirmWarnings':True});c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+task['id']+'/sessions',{})
    def append():return c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':q['answer']},expected=201)
    def settle():
        for _ in range(200):
            if c.request('/students/'+sid+'/mastery')['pending']==0:return
            time.sleep(.05)
        raise AssertionError('Actual assessment worker did not settle')
    for _ in range(3):append();settle()
    baseline=c.request('/students/'+sid+'/export');assert len(baseline['consumerReceipts'])==3
    event_url='/domain-events/window?studentId='+sid+'&pageSize=1';receipt_url='/students/'+sid+'/consumer-receipts/window?pageSize=1'
    first_events=c.request(event_url);first_receipts=c.request(receipt_url);assert first_events['nextCursor'] and first_receipts['nextCursor']
    append();settle();after=c.request('/students/'+sid+'/export');assert len(after['consumerReceipts'])==4
    def pages(url,first,field):
        rows=[];page=first
        while True:
            rows.extend(page[field])
            if not page['nextCursor']:return rows
            page=c.request(url+'&cursor='+quote(page['nextCursor'],safe=''))
    for url,first,field,original in [(event_url,first_events,'events',baseline['domainEvents']),(receipt_url,first_receipts,'receipts',baseline['consumerReceipts'])]:
        rows=pages(url,first,field);expected=sorted(original,key=lambda r:(-datetime.datetime.fromisoformat(r['createdAt'].replace('Z','+00:00')).timestamp(),r['id']))
        assert rows==expected and len({r['id'] for r in rows})==len(original)
        fresh=pages(url,c.request(url),field);new_ids={r['id'] for r in after['domainEvents' if field=='events' else 'consumerReceipts']}-{r['id'] for r in original};assert {r['id'] for r in fresh}=={r['id'] for r in original}|new_ids
        token=quote(first['nextCursor'],safe='');c.request(url.replace('pageSize=1','pageSize=2')+'&cursor='+token,expected=422)
        decoded=json.loads(base64.b64decode(first['nextCursor']));decoded['scope']='wrong/1';bad=quote(base64.b64encode(json.dumps(decoded).encode()).decode(),safe='');c.request(url+'&cursor='+bad,expected=422)
        c.request(url+'&cursor=broken',expected=422)
    c.request(event_url+'&cursor='+quote(first_receipts['nextCursor'],safe=''),expected=422);c.request(receipt_url+'&cursor='+quote(first_events['nextCursor'],safe=''),expected=422)
    c.request('/domain-events/window?pageSize=1&cursor='+quote(first_events['nextCursor'],safe=''),expected=422)
    c.request('/domain-events/window?studentId='+other['id']+'&pageSize=1&cursor='+quote(first_events['nextCursor'],safe=''),expected=422)
    c.request('/students/'+other['id']+'/consumer-receipts/window?pageSize=1&cursor='+quote(first_receipts['nextCursor'],safe=''),expected=422)
    assert c.request('/students/'+other['id']+'/consumer-receipts/window')['receipts']==[]
    for url in ('/domain-events/window?studentId='+str(uuid.uuid4()),'/students/'+str(uuid.uuid4())+'/consumer-receipts/window'):c.request(url,expected=404)
    c.request('/domain-events/window?pageSize=51',expected=422);c.request('/students/'+sid+'/consumer-receipts/window?pageSize=51',expected=422)
    assert c.request('/domain-events?studentId='+sid)['page']==1 and c.request('/students/'+sid+'/consumer-receipts')['page']==1
    final=c.request('/students/'+sid+'/export')
    for field in ('domainEvents','consumerReceipts','attempts','gradings','evidence','mastery','generations','assessmentCheckpoints'):
        assert sorted(final[field],key=lambda r:r['id'])==sorted(after[field],key=lambda r:r['id']),field
    child=api_acceptance.Client()
    for cookie in c.jar:child.jar.set_cookie(copy.copy(cookie))
    child.etag=c.etag;child.request('/students/'+sid+'/child-sessions',{});child.request(event_url,expected=403);child.request(receipt_url,expected=403);c.request('/me')
    print('PASS actual events and receipts cursor: three committed attempts then genuine fourth worker consumption; original pages exactly once/in order, refresh includes new facts, full original records preserved, scope/size/student/role guards and legacy pages; no synthetic receipts or source sequence changes')
