"""Published task denominator and genuine parent deferral evidence in weekly reports."""
import json
import uuid
from datetime import date,timedelta
from api_acceptance import Client,TODAY


def verify(document,parent):
    student=parent.request('/students',{'name':'预算执行隔离验收'},expected=201);sid=student['id']
    empty=parent.request('/students/'+sid+'/weekly-summary')['budgetExecutability']
    assert empty['rate'] is None and empty['publishedTasks']==0 and empty['tasks']==[]
    release=parent.request('/content')['releases'][0];parent.request('/students/'+sid+'/content/'+release['id']+':bind',{})
    rev=parent.request('/students/'+sid+'/plans/'+TODAY+':generate',{})
    definitions=[('按预计完成',True),('超出预计完成',True),('家长明确顺延',True),('尚未执行',True),('孩子自行延期',False)]
    tasks=[]
    for title,mandatory in definitions:
        tasks.append(parent.request('/plans/'+rev['id']+'/tasks',{'title':title,'minutes':5,'resourceRef':'纸上阅读原创例题','mandatory':mandatory,'type':'Resource'}))
    view=parent.request('/students/'+sid+'/plans/'+TODAY)
    total=len(view['tasks'])
    assert total==5
    draft=parent.request('/students/'+sid+'/weekly-summary')['budgetExecutability']
    assert draft['publishedTasks']==0 and draft['rate'] is None
    parent.request('/plans/'+rev['id']+':publish',{'previewHash':view['revision']['inputHash']})
    for task,minutes in [(tasks[0],4),(tasks[1],6)]:
        parent.request('/tasks/'+task['id']+':transition',{'status':'InProgress'})
        parent.request('/tasks/'+task['id']+':transition',{'status':'Completed','actualMinutes':minutes})
    child=Client()
    for cookie in parent.jar:child.jar.set_cookie(cookie)
    child.request('/me');child.request('/students/'+sid+'/child-sessions',{})
    child.request('/tasks/'+tasks[4]['id']+':transition',{'status':'Deferred','reason':'孩子自行选择稍后做'})
    before=parent.request('/students/'+sid+'/weekly-summary')['budgetExecutability']
    assert before['publishedTasks']==5 and before['completedWithinEstimate']==1 and before['eligibleTasks']==1 and before['rate']==.2
    assert not next(t for t in before['tasks'] if t['taskId']==tasks[4]['id'])['eligible']
    print('PASS draft excluded, task duration overrun and child deferral not counted, fixed published denominator')

    target=(date.fromisoformat(TODAY)+timedelta(days=1)).isoformat();reason='学校作业较多，家长确认延期 <script>window.__budgetUnsafe=1</script>'
    key=str(uuid.uuid4());body={'date':target,'reason':reason}
    parent.request('/tasks/'+tasks[2]['id']+':defer',body,key=key);parent.request('/tasks/'+tasks[2]['id']+':defer',body,key=key)
    after=parent.request('/students/'+sid+'/weekly-summary')['budgetExecutability']
    assert after['publishedTasks']==5 and after['completedWithinEstimate']==1 and after['confirmedDeferredTasks']==1 and after['eligibleTasks']==2 and after['rate']==.4
    entry=next(t for t in after['tasks'] if t['taskId']==tasks[2]['id'])
    assert len(entry['deferrals'])==1 and entry['deferrals'][0]['reason']==reason and entry['deferrals'][0]['targetDate']==target
    assert all(t['status']!='Completed' for t in after['tasks'] if t['taskId'] in [tasks[2]['id'],tasks[3]['id'],tasks[4]['id']])
    print('PASS real parent-confirmed deferral counted once, reason/date retained, removal revision does not shrink denominator')

    # A further published revision retains the same fixed tasks without duplicating the denominator.
    parent.request('/students/'+sid+'/availability/'+TODAY,{'minutes':30,'reserved':0},method='PUT')
    latest=parent.request('/students/'+sid+'/plans/'+TODAY+':generate',{})
    plan=parent.request('/students/'+sid+'/plans/'+TODAY)
    parent.request('/plans/'+latest['id']+':publish',{'previewHash':plan['revision']['inputHash']})
    repeated=parent.request('/students/'+sid+'/weekly-summary')['budgetExecutability']
    assert repeated['publishedTasks']==5 and repeated['eligibleTasks']==2 and len({t['taskId'] for t in repeated['tasks']})==5
    old=(date.fromisoformat(TODAY)-timedelta(days=14)).isoformat()
    assert parent.request('/students/'+sid+'/weekly-summary?end='+old)['budgetExecutability']['rate'] is None
    assert 'BudgetExecutability' in document['components']['schemas'] and 'BudgetTaskResult' in document['components']['schemas']
    child.request('/students/'+sid+'/weekly-summary',expected=403)
    print('PASS intermediate publications deduplicate, empty past week stays unknown, typed contract and child isolation')


if __name__=='__main__':
    c=Client();c.request('/auth/register',{'userName':'budget-report-'+uuid.uuid4().hex[:12],'password':'budget-private-'+uuid.uuid4().hex},expected=201)
    draft=c.request('/content/fixture',{});c.request('/content/drafts/'+draft['id']+':review',{});preview=c.request('/content/drafts/'+draft['id']+'/preview');c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':preview['hash']})
    verify(c.request('/openapi.json'),c)
