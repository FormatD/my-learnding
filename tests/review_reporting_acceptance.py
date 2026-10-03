"""Exercise weekly review encounters using genuinely scheduled tasks in an isolated fixture."""
import time,uuid
from datetime import date,timedelta
from api_acceptance import TODAY


def verify(c,fixture):
    sid=fixture['studentId']
    path='/students/'+sid+'/weekly-summary'
    initial=c.request(path)['reviewPass']
    assert initial['encounters']==0 and initial['rate'] is None
    coverage=c.request(path)['reviewCoverage'];assert coverage['dueSchedules']==1 and coverage['executedSchedules']==0 and coverage['unexecutedSchedules']==1
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
    covered=c.request(path)['reviewCoverage'];assert covered['dueSchedules']==1 and covered['executedSchedules']==1 and covered['rate']==1
    assert covered['items'][0]['schedule']['executedAttemptId']==aid
    mastery=c.request(path)['masteryChanges'];m=next(v for v in mastery['items'] if v['kcId']==fixture['kcId'])
    assert mastery['evidenceParts']==2 and mastery['encounters']==2 and m['before'] is None and m['negativeWeight']==1 and m['positiveWeight']>0
    assert any(e['attemptId']==aid and e['gradingId']==current['items'][0]['gradingId'] for e in m['evidence'])
    assert current['items'][0]['attemptId']==aid and current['items'][0]['reason']=='TRUSTED_INDEPENDENT'
    old=(date.fromisoformat(TODAY)-timedelta(days=7)).isoformat()
    past=c.request(path+'?end='+old);assert past['reviewPass']['rate'] is None and past['reviewCoverage']['rate'] is None
    print('PASS weekly review uses actual first-answer date; skipped/answer-only tasks and prior practice excluded; empty past week unknown')
    for _ in range(100):
        if c.request('/students/'+sid+'/mastery')['pending']==0:break
        time.sleep(.1)
    else:raise AssertionError('projection did not settle')
    active=c.request('/students/'+sid+'/mastery/'+fixture['kcId'])['mastery']
    assert active['status']==m['after']['status'] and active['needsRecheck']==m['after']['needsRecheck']
    assert active['effectiveEvidence']==m['after']['effectiveEvidence'] and active['distinctQuestions']==m['after']['distinctQuestions']
    grade={'result':'Incorrect','reason':'隔离验收：人工更正首次复习判分'}
    preview=c.request('/attempts/'+aid+'/grading-preview',grade)
    c.request('/attempts/'+aid+'/grading-revisions',{**grade,'previewHash':preview['previewHash']},expected=202)
    updated=c.request(path)['reviewPass']
    assert updated['gradedEncounters']==1 and updated['independentPasses']==0 and updated['rate']==0 and updated['items'][0]['result']=='Incorrect'
    assert updated['items'][0]['gradingId']!=current['items'][0]['gradingId']
    revised=c.request(path)['masteryChanges'];rm=next(v for v in revised['items'] if v['kcId']==fixture['kcId'])
    assert revised['evidenceParts']==2 and rm['positiveWeight']==0 and rm['negativeWeight']>1
    assert all(e['gradingId']!=current['items'][0]['gradingId'] for e in rm['evidence'])
    print('PASS effective latest grading changes weekly pass rate without duplicating encounters or rewriting original answer')
    c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':answer},expected=201)
    retried=c.request(path)['reviewPass']
    assert retried['encounters']==1 and retried['gradedEncounters']==1 and retried['independentPasses']==0
    assert c.request(path)['masteryChanges']['evidenceParts']==2
    assert 'MasteryChangeSummary' in c.request('/openapi.json')['components']['schemas']
    print('PASS correct retry cannot replace failed first answer; typed review report contract')


def verify_coverage_correction(c,fixture):
    sid=fixture['studentId']
    for _ in range(100):
        if c.request('/students/'+sid+'/mastery')['pending']==0:break
        time.sleep(.1)
    else:raise AssertionError('projection did not settle')
    original=next(a for a in c.request('/students/'+sid+'/attempts')['attempts'] if a['answer']=='999' and a['number']==1)
    body={'result':'Correct','reason':'隔离验收：更正最初错题，重新计算历史到期依据'}
    preview=c.request('/attempts/'+original['id']+'/grading-preview',body)
    c.request('/attempts/'+original['id']+'/grading-revisions',{**body,'previewHash':preview['previewHash']},expected=202)
    report=c.request('/students/'+sid+'/weekly-summary')['reviewCoverage']
    assert report['dueSchedules']==0 and report['rate'] is None and report['items']==[]
    assert report['ruleVersion']=='review/3'
    mastery=c.request('/students/'+sid+'/weekly-summary')['masteryChanges'];row=next(v for v in mastery['items'] if v['kcId']==fixture['kcId']);assert row['negativeWeight']<2 and mastery['evidenceParts']==2
    assert any(e['attemptId']==original['id'] and e['positive'] for e in row['evidence'])
    print('PASS correction of original wrong answer reconstructs due cohort; removed obligation stays unknown instead of fabricated 100%')
