"""Actual durable resource reports do not mutate tasks or produce learning evidence."""
import datetime,json,uuid

def verify(c):
    release=next(r for r in c.request('/content')['releases'] if not r['withdrawn'])
    student=c.request('/students',{'name':'资源报告接口验收'},expected=201)
    c.request('/students/'+student['id']+'/content/'+release['id']+':bind',{})
    day=datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8))).date().isoformat()
    plan=c.request('/students/'+student['id']+'/plans/'+day+':generate',{})
    task=c.request('/plans/'+plan['id']+'/tasks',{'title':'资源报告验收','minutes':5,'resourceRef':'受控纸质材料','type':'Resource'})
    endpoint='/tasks/'+task['id']+'/resource-issues'
    c.request(endpoint,{'reason':'未发布'},expected=409)
    view=c.request('/students/'+student['id']+'/plans/'+day)
    c.request('/plans/'+plan['id']+':publish',{'previewHash':view['revision']['inputHash'],'confirmWarnings':True})
    before=c.request('/students/'+student['id']+'/export')
    for reason in ('', ' '*5,'x'*501):c.request(endpoint,{'reason':reason},expected=422)
    key=str(uuid.uuid4());payload={'reason':'材料缺页'}
    first=c.request(endpoint,payload,key=key,expected=201);assert c.request(endpoint,payload,key=key,expected=201)==first
    c.request(endpoint,{'reason':'改变原请求'},key=key,expected=409)
    rows=c.request('/students/'+student['id']+'/resource-issues');assert len(rows)==1 and rows[0]==first and rows[0]['issue']['resourceRef']=='受控纸质材料'
    after=c.request('/students/'+student['id']+'/export')
    for field in ('tasks','plans','revisions','evidence','attempts','mastery'):assert before[field]==after[field],field
    audits=[a for a in after['studentAudit'] if a['action']=='ResourceIssueReported'];assert len(audits)==1 and json.loads(audits[0]['details'])==first['issue']
    c.request('/tasks/'+str(uuid.uuid4())+'/resource-issues',payload,expected=404)
    c.request('/students/'+str(uuid.uuid4())+'/resource-issues',expected=404)
    import copy
    from api_acceptance import Client
    def child_for(id):
        child=Client()
        for cookie in c.jar:child.jar.set_cookie(copy.copy(cookie))
        child.etag=c.etag;child.request('/students/'+id+'/child-sessions',{});return child
    other=c.request('/students',{'name':'资源报告隔离学生'},expected=201)
    wrong=child_for(other['id']);wrong.request(endpoint,payload,expected=404)
    child=child_for(student['id']);child.request('/students/'+student['id']+'/resource-issues',expected=403)
    child.request(endpoint,{'reason':'孩子真实报告权限验收'},expected=201)
    result=c.request('/students/'+student['id']+'/export');assert len([a for a in result['studentAudit'] if a['action']=='ResourceIssueReported'])==2 and result['tasks']==before['tasks'] and result['evidence']==before['evidence']
    c.request('/me')
    print('PASS actual resource report audit/parent read/private export; duplicate key one stored report, changed body409, blank/oversized422, inactive409, unknown404, child own report201/read403 and other student404; complete task/plan/evidence state unchanged')
