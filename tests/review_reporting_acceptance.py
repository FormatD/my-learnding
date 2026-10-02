"""Exercise weekly review encounters using genuinely scheduled tasks in an isolated fixture."""
import time,uuid
from datetime import date,timedelta
from api_acceptance import TODAY


def verify(c,fixture):
    sid=fixture['studentId']
    path='/students/'+sid+'/weekly-summary'
    initial=c.request(path)['reviewPass']
    assert initial['encounters']==0 and initial['rate'] is None
    c.request('/students/'+sid+'/plans/'+TODAY+':generate',{})
    plan=c.request('/students/'+sid+'/plans/'+TODAY)
    task=next(t for t in plan['tasks'] if t['type']=='Review' and t['questionId']==fixture['questionId'] and t['status']=='Planned')
    c.request('/plans/'+plan['revision']['id']+':publish',{'previewHash':plan['revision']['inputHash'],'confirmWarnings':True})
    c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'})
    session=c.request('/tasks/'+task['id']+'/sessions',{})
    catalog=c.request('/students/'+sid+'/catalog');answer=next(q['answer'] for q in catalog['questions'] if q['id']==task['questionId'])
    result=c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':answer},expected=201)
    aid=result['attempt']['id']
    current=c.request(path)['reviewPass']
    assert current['encounters']==1 and current['gradedEncounters']==1 and current['independentPasses']==1 and current['rate']==1
    assert current['items'][0]['attemptId']==aid and current['items'][0]['reason']=='TRUSTED_INDEPENDENT'
    old=(date.fromisoformat(TODAY)-timedelta(days=7)).isoformat()
    assert c.request(path+'?end='+old)['reviewPass']['rate'] is None
    print('PASS weekly review uses actual first-answer date; skipped/answer-only tasks and prior practice excluded; empty past week unknown')
    for _ in range(100):
        if c.request('/students/'+sid+'/mastery')['pending']==0:break
        time.sleep(.1)
    else:raise AssertionError('projection did not settle')
    grade={'result':'Incorrect','reason':'隔离验收：人工更正首次复习判分'}
    preview=c.request('/attempts/'+aid+'/grading-preview',grade)
    c.request('/attempts/'+aid+'/grading-revisions',{**grade,'previewHash':preview['previewHash']},expected=202)
    updated=c.request(path)['reviewPass']
    assert updated['gradedEncounters']==1 and updated['independentPasses']==0 and updated['rate']==0 and updated['items'][0]['result']=='Incorrect'
    assert updated['items'][0]['gradingId']!=current['items'][0]['gradingId']
    print('PASS effective latest grading changes weekly pass rate without duplicating encounters or rewriting original answer')
    c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':answer},expected=201)
    retried=c.request(path)['reviewPass']
    assert retried['encounters']==1 and retried['gradedEncounters']==1 and retried['independentPasses']==0
    assert 'ReviewPassSummary' in c.request('/openapi.json')['components']['schemas']
    print('PASS correct retry cannot replace failed first answer; typed review report contract')
