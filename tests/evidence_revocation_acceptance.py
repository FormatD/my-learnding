"""Real confirmed corrections revoke only changed evidence, including downstream caps."""
import copy,json,time,uuid
from api_acceptance import TODAY

def verify(document,c):
    sid=c.request('/students',{'name':'撤销及后续限额验收'},expected=201)['id']
    fixture=c.request('/content/fixture',{});catalog=json.loads(fixture['payload']);group=str(uuid.uuid4())
    for i,k in enumerate(catalog['kcs']):k['code']='REVOCATION.'+uuid.uuid4().hex+'.'+str(i)
    questions=catalog['questions'][:3]
    for q in questions:
        q.update(difficulty='Hard',variantGroupId=group,revisionId=str(uuid.uuid4()),mappings=[{'kcId':catalog['kcs'][0]['id'],'role':'Primary','share':1,'mode':'WholeItem','step':None}])
    d=c.request('/content/drafts',{'title':'撤销依赖验收','catalog':catalog});c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview');r=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']});c.request('/students/'+sid+'/content/'+r['id']+':bind',{})
    plan=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{})
    tasks=[c.request('/plans/'+plan['id']+'/tasks',{'title':'限额验收题','minutes':5,'resourceRef':'本地构造题','type':'Practice','questionId':q['id']}) for q in questions]
    v=c.request('/students/'+sid+'/plans/'+TODAY);c.request('/plans/'+plan['id']+':publish',{'previewHash':v['revision']['inputHash'],'confirmWarnings':True})
    def settled():
        for _ in range(100):
            m=c.request('/students/'+sid+'/mastery')
            if m['pending']==0:return m
            time.sleep(.05)
        raise AssertionError('projection did not settle')
    attempts=[]
    for q,t in zip(questions,tasks):
        c.request('/tasks/'+t['id']+':transition',{'status':'InProgress'});s=c.request('/tasks/'+t['id']+'/sessions',{});attempts.append(c.request('/sessions/'+s['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':q['answer']},expected=201)['attempt']);settled()
    kc=catalog['kcs'][0]['id'];old=c.request('/students/'+sid+'/mastery/'+kc)['evidence'];assert [e['weight'] for e in old]==[1.2,.8,0]
    online=c.request('/students/'+sid+'/export');assert len(online['generations'])==1 and len(online['assessmentCheckpoints'])==3 and len(online['assessmentContexts'])==3 and len(online['evidence'])==3
    generation=online['generations'][0];assert generation['calculationMode']=='OnlineAppend' and generation['processedInputCount']==1
    for receipt in online['consumerReceipts']:
        state=next(x for x in online['assessmentCheckpoints'] if x['id']==receipt['checkpointId']);assert state['generationId']==generation['id'] and state['inputHash']==receipt['inputHash']
    assert len({r['inputHash'] for r in online['consumerReceipts']})==3
    assert c.request('/students/'+sid+'/evidence-revocations')['total']==0
    def grade(a,result,steps=None):
        inp={'result':result,'reason':'实际确认首题判分，完整重放后续限额','steps':steps or []};p=c.request('/attempts/'+a['id']+'/grading-preview',inp);g=c.request('/attempts/'+a['id']+'/grading-revisions',{**inp,'previewHash':p['previewHash']},expected=202);settled();return g
    g=grade(attempts[0],'Incorrect');assert g['correctionBatchId']
    page=c.request('/students/'+sid+'/evidence-revocations');assert page['total']==3 and {r['evidenceId'] for r in page['revocations']}=={e['id'] for e in old}
    assert all(x['correctionBatchId']==g['correctionBatchId'] and x['reason']=='实际确认首题判分，完整重放后续限额' for x in page['revocations'])
    assert {x['effect'] for x in page['revocations']}=={'DirectCorrection','ReplayDependency'}
    current=c.request('/students/'+sid+'/mastery/'+kc)['evidence'];assert [e['weight'] for e in current]==[.8,.96,.96] and all(e['correctionBatchId']==g['correctionBatchId'] for e in current)
    # Original immutable evidence fields remain unchanged; only a separate revocation row is appended.
    export=c.request('/students/'+sid+'/export');by_id={e['id']:e for e in export['evidence']};assert all(by_id[e['id']]==e for e in old)
    assert all(next(b for b in export['corrections'] if b['id']==x['correctionBatchId'])['status']=='Applied' for x in page['revocations'])
    assert next(b for b in export['corrections'] if b['id']==g['correctionBatchId'])['cause']=='Grading'
    c.request('/students/'+sid+':rebuild',{});assert c.request('/students/'+sid+'/evidence-revocations')['total']==3
    grade(attempts[0],'Incorrect');assert c.request('/students/'+sid+'/evidence-revocations')['total']==3
    assert {x['id']:x for x in c.request('/students/'+sid+'/attempts')['attempts']}=={x['id']:x for x in attempts}
    assert len(c.request('/students/'+sid+'/evidence-revocations?limit=1')['revocations'])==1
    c.request('/students/'+sid+'/evidence-revocations?limit=101',expected=422)
    assert '/api/v1/students/{id}/evidence-revocations' in document['paths']
    print('PASS online arrivals preserve one generation and do not revoke evidence; confirmed grading correction revokes the three actual immutable historical parts with direct/downstream causes; replay and equal-result re-review do not double revoke, export and pagination retain provenance')

    # Six-decimal database storage must not turn the same high-precision calculation into an error.
    observed=copy.deepcopy(catalog);q=observed['questions'][0];q.update(id=str(uuid.uuid4()),variantGroupId=None,revisionId=str(uuid.uuid4()),type='MultiStep',policy='ObservedSteps',mappings=[{'kcId':kc,'role':'Primary' if i==0 else 'Secondary','share':share,'mode':'StepObserved','step':'point'+str(i)} for i,share in enumerate([.333333,.333333,.333334])])
    d=c.request('/content/drafts',{'title':'小数存储不误撤销','catalog':observed});c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview');r2=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']});c.request('/students/'+sid+'/content/'+r2['id']+':bind',{})
    plan2=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});t=c.request('/plans/'+plan2['id']+'/tasks',{'title':'已观察的小数份额','minutes':5,'resourceRef':'核对三步过程','type':'Practice','questionId':q['id']});v=c.request('/students/'+sid+'/plans/'+TODAY);c.request('/plans/'+plan2['id']+':publish',{'previewHash':v['revision']['inputHash'],'confirmWarnings':True});c.request('/tasks/'+t['id']+':transition',{'status':'InProgress'});s=c.request('/tasks/'+t['id']+'/sessions',{});a=c.request('/sessions/'+s['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'观察过程'},expected=201)['attempt'];settled()
    steps=[{'step':'point'+str(i),'result':'Correct','hintLevel':0} for i in range(3)];grade(a,'Partial',steps)
    decimal_evidence=[e for e in c.request('/students/'+sid+'/mastery/'+kc)['evidence'] if e['attemptId']==a['id']]
    assert sorted(e['rawWeight'] for e in decimal_evidence)==[.4,.4,.400001]
    total=c.request('/students/'+sid+'/evidence-revocations')['total'];grade(a,'Partial',steps);assert c.request('/students/'+sid+'/evidence-revocations')['total']==total==3
    print('PASS actual six-decimal PostgreSQL evidence with .333333 observed shares remains equivalent on identical re-review; database rounding does not fabricate revocations')
