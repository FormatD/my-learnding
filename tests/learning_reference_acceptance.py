"""Scoped fault injection distinguishes unrecorded legacy references from missing modern bindings."""
import json,re,subprocess,time,uuid
from api_acceptance import TODAY

def verify(document,c,env):
    assert re.fullmatch('learning_fault_openapi_[0-9a-f]{12}',env['PGDATABASE']),'only disposable contract database allowed'
    def sql(q):return subprocess.check_output(['psql','-Atc',q],env=env,text=True).strip()
    def uid(x):return str(uuid.UUID(x))
    sid=c.request('/students',{'name':'固定引用兼容验收'},expected=201)['id'];f=c.request('/content/fixture',{});cat=json.loads(f['payload'])
    for k in cat['kcs']:k['code']='REFERENCE.'+uuid.uuid4().hex
    d=c.request('/content/drafts',{'title':'旧引用与现代引用','catalog':cat});c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview');r=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']});c.request('/students/'+sid+'/content/'+r['id']+':bind',{})
    q=json.loads(r['payload'])['questions'][0];plan=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});task=c.request('/plans/'+plan['id']+'/tasks',{'title':'引用核对题','minutes':5,'resourceRef':'本地构造','type':'Practice','questionId':q['id']});v=c.request('/students/'+sid+'/plans/'+TODAY);c.request('/plans/'+plan['id']+':publish',{'previewHash':v['revision']['inputHash'],'confirmWarnings':True});c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+task['id']+'/sessions',{});a=c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':q['answer']},expected=201)['attempt']
    for _ in range(100):
        if c.request('/students/'+sid+'/mastery')['pending']==0:break
        time.sleep(.05)
    else:raise AssertionError('projection did not settle')
    original=c.request('/students/'+sid+'/export');s=next(s for s in original['sessions'] if s['id']==session['sessionId']);assert s['questionRevisionId'] and s['mappingSetRevisionId']
    def restore():
        sql(f'''UPDATE "Sessions" SET "QuestionRevisionId"='{uid(s['questionRevisionId'])}',"MappingSetRevisionId"='{uid(s['mappingSetRevisionId'])}' WHERE "Id"='{uid(s['id'])}'; UPDATE "Attempts" SET "MappingSetRevisionId"='{uid(a['mappingSetRevisionId'])}' WHERE "Id"='{uid(a['id'])}';''')
    try:
        sql(f'''UPDATE "Sessions" SET "MappingSetRevisionId"=NULL WHERE "Id"='{uid(s['id'])}'; UPDATE "Attempts" SET "MappingSetRevisionId"=NULL WHERE "Id"='{uid(a['id'])}';''')
        c.request('/tasks/'+task['id']+'/sessions',{},expected=422);c.request('/sessions/'+s['id']+'/hints',{'level':1},expected=422);c.request('/sessions/'+s['id']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'20'},expected=422);c.request('/attempts/'+a['id']+'/grading-context',expected=422);c.request('/attempts/'+a['id']+'/grading-preview',{'result':'Incorrect','reason':'缺引用不能继续确认','steps':[]},expected=422);c.request('/students/'+sid+':rebuild',{},expected=422)
        after=c.request('/students/'+sid+'/export');assert after['generations']==original['generations'] and after['evidence']==original['evidence'] and after['gradings']==original['gradings'] and len(after['attempts'])==1 and next(x for x in after['sessions'] if x['id']==s['id'])['hintLevel']==s['hintLevel']
        print('PASS missing modern mapping is rejected by session reuse, hints, submission, grading and rebuild with no partial hint/attempt/grading/generation/evidence changes')
        # Explicitly simulate an old session predating both nullable reference fields.
        sql(f'''UPDATE "Sessions" SET "QuestionRevisionId"=NULL WHERE "Id"='{uid(s['id'])}';''')
        assert c.request('/tasks/'+task['id']+'/sessions',{})['sessionId']==s['id'];assert c.request('/sessions/'+s['id']+'/hints',{'level':1})['level']==1;assert c.request('/attempts/'+a['id']+'/grading-context')['question']['revisionId']==q['revisionId']
        c.request('/students/'+sid+':rebuild',{});ctx=c.request('/students/'+sid+'/assessment-contexts');assert ctx['contexts'][0]['mappingSource']=='LegacySnapshot' and ctx['contexts'][0]['mappingSetRevisionId'] is None
        exported=c.request('/students/'+sid+'/export');legacy=next(x for x in exported['sessions'] if x['id']==s['id']);assert legacy['questionRevisionId'] is None and legacy['mappingSetRevisionId'] is None and exported['attempts'][0]['mappingSetRevisionId'] is None
        print('PASS explicitly simulated unrecorded legacy session reference shape still uses the fixed original snapshot, records LegacySnapshot explicitly and never invents saved container references in export')
    finally:restore()
    c.request('/students/'+sid+':rebuild',{});assert c.request('/students/'+sid+'/assessment-contexts')['contexts'][0]['mappingSource']=='FixedContainer'

    mapped=json.loads(r['payload']);mapped['questions'][0]['revisionId']=str(uuid.uuid4());mapped['questions'][0]['mappings'][0]['kcId']=mapped['kcs'][1]['id']
    d2=c.request('/content/drafts',{'title':'有效的明确映射更正','catalog':mapped});c.request('/content/drafts/'+d2['id']+':review',{});p=c.request('/content/drafts/'+d2['id']+'/preview');r2=c.request('/content/drafts/'+d2['id']+':publish',{'previewHash':p['hash']})
    change={'releaseId':r2['id'],'attemptIds':[a['id']],'reason':'检查有效更正不能掩盖损坏原引用'};p=c.request('/students/'+sid+'/mapping-corrections:preview',change);c.request('/students/'+sid+'/mapping-corrections:confirm',{**change,'previewHash':p['previewHash']},expected=202)
    for _ in range(100):
        if c.request('/students/'+sid+'/mastery')['pending']==0:break
        time.sleep(.05)
    else:raise AssertionError('mapping projection did not settle')
    complete=c.request('/students/'+sid+'/export');item=complete['correctionItems'][0]
    try:
        sql(f'''UPDATE "CorrectionItem" SET "MappingSetRevisionId"=NULL WHERE "Id"='{uid(item['id'])}';''')
        c.request('/attempts/'+a['id']+'/grading-context',expected=422);c.request('/students/'+sid+':rebuild',{},expected=422)
    finally:sql(f'''UPDATE "CorrectionItem" SET "MappingSetRevisionId"='{uid(item['mappingSetRevisionId'])}' WHERE "Id"='{uid(item['id'])}';''')
    bindings=c.request('/content/releases/'+r['id']+'/mapping-sets')['bindings'];other=next(b['setRevisionId'] for b in bindings if b['ownerType']=='Question' and b['ownerId']!=q['id'])
    try:
        sql(f'''UPDATE "Sessions" SET "MappingSetRevisionId"='{uid(other)}' WHERE "Id"='{uid(s['id'])}';UPDATE "Attempts" SET "MappingSetRevisionId"='{uid(other)}' WHERE "Id"='{uid(a['id'])}';''')
        c.request('/attempts/'+a['id']+'/grading-context',expected=422);c.request('/students/'+sid+':rebuild',{},expected=422)
    finally:restore()
    end=c.request('/students/'+sid+'/export');assert end['generations']==complete['generations'] and end['evidence']==complete['evidence'] and end['attempts']==complete['attempts']
    print('PASS missing effective correction mapping and wrong original owner binding fail both grading and replay; a valid newer correction cannot hide damaged original references, with no partial projection or raw answer mutation')
