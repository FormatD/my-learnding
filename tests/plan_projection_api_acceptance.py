"""Plan lag warning, fixed provenance and export against an actual disposable service."""
import hashlib,json,subprocess,uuid,time
from api_acceptance import TODAY
def verify(c,env):
    c.request('/me');release=next(r for r in c.request('/content')['releases'] if not r['withdrawn'] and json.loads(r['payload'])['questions'])
    student=c.request('/students',{'name':'计划评估依据接口验收'},expected=201);sid=student['id'];c.request('/students/'+sid+'/content/'+release['id']+':bind',{});catalog=json.loads(release['payload']);c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    def generate():return c.request('/students/'+sid+'/plans/'+TODAY+':generate',{})
    healthy=generate();snapshot=json.loads(healthy['projectionSnapshot']);assert snapshot['version']=='plan-projection/1' and snapshot['studentId']==sid and snapshot['generationId'] is None and snapshot['assessmentEventSequence'] is None and snapshot['pending']==[] and not snapshot['conservative'];assert healthy['projectionSnapshotHash']==hashlib.sha256(healthy['projectionSnapshot'].encode()).hexdigest();assert generate()['id']==healthy['id']
    view=c.request('/students/'+sid+'/plans/'+TODAY);c.request('/plans/'+healthy['id']+':publish',{'previewHash':healthy['inputHash'],'confirmWarnings':True});task=next(t for t in view['tasks'] if t.get('questionId'));c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+task['id']+'/sessions',{});question=next(q for q in catalog['questions'] if q['id']==task['questionId']);submitted=c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':question['answer']},expected=201)
    for _ in range(100):
        if c.request('/students/'+sid+'/mastery')['pending']==0:break
        time.sleep(.1)
    else:raise AssertionError('actual projection did not catch up')
    # Controlled exhausted legacy queue fixture, scoped only to this disposable database.
    assert env['PGDATABASE'].startswith('learning_fault_')
    eid=str(uuid.uuid4());fid=str(uuid.UUID(student['familyId']));attempt=str(uuid.UUID(submitted['attempt']['id']));student_id=str(uuid.UUID(sid))
    query=f'''INSERT INTO "Outbox" ("Id","FamilyId","StudentId","AttemptId","CreatedAt","Retries","Error") VALUES ('{eid}','{fid}','{student_id}','{attempt}',clock_timestamp(),3,'CONTROLLED_PLAN_FAILURE');'''
    subprocess.run(['psql','-v','ON_ERROR_STOP=1','-c',query],env=env,check=True,stdout=subprocess.DEVNULL)
    lagged=generate();captured=json.loads(lagged['projectionSnapshot']);sync=c.request('/students/'+sid+'/assessment-consumption');assert lagged['id']!=healthy['id'] and lagged['ruleVersion']=='plan/3' and captured['conservative'] and captured['failedCount']==1 and captured['pending'][0]['eventId']==eid and captured['generationId'] is not None and captured['assessmentEventSequence']==sync['cursor']['lastEventSequence'] and captured['assessmentAppliedEventId']==sync['cursor']['lastAppliedEventId'];assert any(w.startswith('PROJECTION_LAG:') for w in json.loads(lagged['warnings']));assert generate()['id']==lagged['id']
    c.request('/plans/'+lagged['id']+':publish',{'previewHash':lagged['inputHash']},expected=422);c.request('/plans/'+lagged['id']+':publish',{'previewHash':lagged['inputHash'],'confirmWarnings':True})
    export=c.request('/students/'+sid+'/export');assert next(r for r in export['revisions'] if r['id']==healthy['id'])['projectionSnapshot']==healthy['projectionSnapshot'];assert next(r for r in export['revisions'] if r['id']==lagged['id'])['projectionSnapshot']==lagged['projectionSnapshot']
    print('PASS actual plan API records unknown initial progress without fabrication, reuses identical capture, captures real later generation/event boundary, requires lag warning acknowledgement and preserves original snapshots in private export')
