"""Direct independent mapping drafts remain unreviewed until actual snapshot review."""
import copy,io,json,uuid,zipfile
import api_acceptance

def verify(document,c):
    def export():
        with c.http.open(api_acceptance.BASE+'/family/export') as r:return json.loads(zipfile.ZipFile(io.BytesIO(r.read())).read('manifest.json'))['data']
    f=c.request('/content/fixture',{});cat=json.loads(f['payload'])
    for i,k in enumerate(cat['kcs']):k['code']='INDEPENDENT.'+uuid.uuid4().hex+'.'+str(i)
    src=c.request('/content/drafts',{'title':'独立映射固定来源','catalog':cat});c.request('/content/drafts/'+src['id']+':review',{});p=c.request('/content/drafts/'+src['id']+'/preview');lib=c.request('/content/drafts/'+src['id']+':publish',{'previewHash':p['hash']});cat=json.loads(lib['payload']);src=c.request('/content/drafts',{'title':'可继续编辑的映射来源','catalog':cat})
    original=c.request('/content/releases/'+lib['id']+'/mapping-sets')
    def inp(t,o,weight=.37):
        mappings=o['mappings'] if t=='Question' else [{'kcId':k,'role':'Primary','share':0,'mode':'None','step':None} for k in o['kcIds']]
        return {'sourceDraftId':src['id'],'expectedDraftVersion':src['version'],'libraryReleaseId':lib['id'],'ownerType':t,'ownerId':o['id'],'ownerRevisionId':o['revisionId'],'evidencePolicy':o.get('policy','NoEvidence'),'reason':'人工维护草稿，未执行审核 <script>neverExecute()</script>','items':[{'kcId':m['kcId'],'kcRevisionId':next(k['revisionId'] for k in cat['kcs'] if k['id']==m['kcId']),'role':m['role'],'coverageWeight':weight,'evidenceShare':m['share'],'evidenceMode':m['mode'],'step':m.get('step'),'sequence':i+1,'modelScore':None,'sourceRefs':[f"draft:{src['id']}/{t}:{o['revisionId']}"]} for i,m in enumerate(mappings)]}
    drafts=[]
    for t,collection in [('Question','questions'),('Lesson','lessons'),('Resource','resources')]:
        body=inp(t,cat[collection][0]);key=str(uuid.uuid4());r=c.request('/content/mapping-sets',body,key=key,expected=201);assert c.request('/content/mapping-sets',body,key=key,expected=201)==r
        assert r['set']['reviewStatus']=='Draft' and r['set']['reviewDecisionId'] is None and r['set']['contentReviewRecordId'] is None and r['set']['coverageOrigin']=='UnreviewedDraft'
        assert r['draft']['status']=='Draft' and r['set']['ownerRevisionId']==body['ownerRevisionId'] and r['set']['originalOwnerRevisionId']==body['ownerRevisionId']
        assert r['source']['sourcePayload']==src['payload'] and r['source']['sourceDraftVersion']==src['version'] and r['source']['provider']=='Manual'
        assert c.request('/content/mapping-sets/'+r['set']['id'])==r
        c.request('/content/drafts/'+r['draft']['id']+':publish',{'previewHash':'not-approved'},expected=422)
        drafts.append(r)
    assert c.request('/content/mapping-sets?limit=1')['total']==3 and len(c.request('/content/mapping-sets?limit=1')['sets'])==1
    c.request('/content/mapping-sets?offset=-1',expected=422);c.request('/content/mapping-sets?limit=101',expected=422)
    before=export();bad=inp('Question',cat['questions'][0]);bad['items'][0]['coverageWeight']=.3333333;c.request('/content/mapping-sets',bad,expected=422)
    bad=inp('Question',cat['questions'][0]);bad['items'][0]['sourceRefs']=['fabricated'];c.request('/content/mapping-sets',bad,expected=422)
    bad=inp('Question',cat['questions'][0]);bad['items'][0]['modelScore']=.99;c.request('/content/mapping-sets',bad,expected=422)
    bad=inp('Question',cat['questions'][0]);bad['provider']='External';c.request('/content/mapping-sets',bad,expected=422)
    after=export();assert before['IndependentMappingDraft']==after['IndependentMappingDraft'] and before['MappingSetRevision']==after['MappingSetRevision'] and before['MappingSetItem']==after['MappingSetItem'] and before['ContentDraft']==after['ContentDraft']
    print('PASS independent question/lesson/resource draft sets have no fabricated review, preserve frozen source and exact weights; idempotency/pagination/invalid precision/provenance/model score reject atomically')
    # Review the exact generated catalog; publication uses the same actual normalized set.
    for r in drafts:
        d=r['draft'];c.request('/content/drafts/'+d['id']+'/reviews',{'expectedDraftVersion':d['version'],'reason':'核对原对象、正式能力与覆盖权重'});reviewed=c.request('/content/mapping-sets/'+r['set']['id']);assert reviewed['set']['reviewStatus']=='ReviewedCatalog' and reviewed['set']['contentReviewRecordId'] and reviewed['set']['reviewDecisionId'] is None
        p=c.request('/content/drafts/'+d['id']+'/preview');rel=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']});bindings=c.request('/content/releases/'+rel['id']+'/mapping-sets');assert bindings['status']=='Bound'
        assert next(b for b in bindings['bindings'] if b['ownerType']==r['set']['ownerType'] and b['ownerId']==r['set']['ownerId'])['setRevisionId']==r['set']['id']
    assert c.request('/content/releases/'+lib['id']+'/mapping-sets')==original and next(r for r in c.request('/content')['releases'] if r['id']==lib['id'])['payload']==lib['payload']
    print('PASS only actual full snapshot review approves independent draft containers; subsequent publication binds their exact identities without modifying previous releases')
    # Editing a generated content draft must not approve or bind the original mapping proposal.
    edit=c.request('/content/mapping-sets',inp('Resource',cat['resources'][0],.63),expected=201);edited_cat=json.loads(edit['draft']['payload'])
    for row in edited_cat['mappingCoverage']:
        if row['ownerType']=='Resource' and row['ownerId']==cat['resources'][0]['id']:row['coverageWeight']=.64
    d=c.request('/content/drafts/'+edit['draft']['id'],{'title':edit['draft']['title'],'catalog':edited_cat},method='PUT');c.request('/content/drafts/'+d['id']+'/reviews',{'expectedDraftVersion':d['version'],'reason':'实际审核后来明确修改的覆盖'})
    assert c.request('/content/mapping-sets/'+edit['set']['id'])['set']['reviewStatus']=='Draft'
    p=c.request('/content/drafts/'+d['id']+'/preview');rel=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']});detail=c.request('/content/releases/'+rel['id']+'/mapping-sets');binding=next(b for b in detail['bindings'] if b['ownerType']=='Resource' and b['ownerId']==cat['resources'][0]['id']);assert binding['setRevisionId']!=edit['set']['id'] and all(i['coverageWeight']==.64 for i in detail['items'] if i['setRevisionId']==binding['setRevisionId'])
    blocked=c.request('/content/mapping-sets',inp('Resource',cat['resources'][0],.84),expected=201);bd=blocked['draft'];c.request('/content/drafts/'+bd['id']+'/reviews',{'expectedDraftVersion':bd['version'],'reason':'撤回前真实完成审核'});bp=c.request('/content/drafts/'+bd['id']+'/preview')
    print('PASS separately edited generated content approves and binds only the actually reviewed weights; original pending container remains unreviewed')
    # Changed evidence attribution creates a new owner revision; the original remains intact.
    body=inp('Question',cat['questions'][0]);body['evidencePolicy']='NoEvidence';body['items'][0].update(role='Context',evidenceMode='None',evidenceShare=0)
    changed=c.request('/content/mapping-sets',body,expected=201);assert changed['set']['ownerRevisionId']!=body['ownerRevisionId'] and changed['set']['revisionNo']>drafts[0]['set']['revisionNo']
    frozen=changed['source'];updated=c.request('/content/drafts/'+src['id'],{'title':'后来编辑的来源','catalog':cat},method='PUT')
    c.request('/content/mapping-sets',body,expected=412);assert c.request('/content/mapping-sets/'+changed['set']['id'])['source']==frozen
    # Withdrawn frozen library blocks both new creation and approving the existing draft.
    c.request('/content/releases/'+lib['id']+':withdraw',{'reason':'验收固定库撤回'})
    release_count=len(c.request('/content')['releases']);c.request('/content/drafts/'+bd['id']+':publish',{'previewHash':bp['hash']},expected=422);assert len(c.request('/content')['releases'])==release_count
    body['expectedDraftVersion']=updated['version'];c.request('/content/mapping-sets',body,expected=422)
    d=changed['draft'];c.request('/content/drafts/'+d['id']+'/reviews',{'expectedDraftVersion':d['version'],'reason':'不能批准已撤回输入'},expected=422);assert c.request('/content/mapping-sets/'+changed['set']['id'])['set']['reviewStatus']=='Draft'
    data=export();assert any(x['id']==frozen['id'] and x['sourcePayload']==frozen['sourcePayload'] for x in data['IndependentMappingDraft'])
    assert '/api/v1/content/mapping-sets' in document['paths'] and '/api/v1/content/mapping-sets/{id}' in document['paths']
    print('PASS changed evidence gets a new revision; later source edits cannot overwrite frozen inputs and stale creation is rejected; withdrawn library cannot approve old draft; private export retains exact provenance')
