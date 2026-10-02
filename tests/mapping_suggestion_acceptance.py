"""Frozen mapping suggestions, atomic review and reviewed draft version boundaries."""
import copy,hashlib,http.cookiejar,io,json,urllib.request,uuid,zipfile
from api_acceptance import Client
import api_acceptance

def verify(document,c):
    def draft(catalog,title):return c.request('/content/drafts',{'catalog':catalog,'title':title})
    def publish(d,expected=200):
        c.request('/content/drafts/'+d['id']+':review',{})
        p=c.request('/content/drafts/'+d['id']+'/preview')
        return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']},expected=expected)
    releases=c.request('/content')['releases']
    library=next(r for r in releases if all(x.get('revisionId') for x in json.loads(r['payload'])['resources']+json.loads(r['payload'])['lessons']))
    original=json.loads(library['payload']);catalog=copy.deepcopy(original)
    for k in catalog['kcs']:k['revisionId']=str(uuid.uuid4())
    source=draft(catalog,'冻结映射输入')
    selected=[('Question',catalog['questions'][0]),('Question',catalog['questions'][1]),('Lesson',catalog['lessons'][0]),('Resource',catalog['resources'][0])]
    owners=[{'ownerType':t,'ownerId':o['id'],'ownerRevisionId':o['revisionId']} for t,o in selected]
    request={'draftId':source['id'],'libraryReleaseId':library['id'],'owners':owners}
    error=c.request('/builder/mapping-runs',{**request,'provider':'External'},expected=422);assert error['code']=='PROVIDER_UNCONFIGURED'
    error=c.request('/builder/mapping-runs',{**request,'owners':owners+[owners[0]]},expected=422);assert error['code']=='INVALID_SELECTION'
    error=c.request('/builder/mapping-runs',{**request,'owners':[{**owners[0],'ownerRevisionId':str(uuid.uuid4())}]},expected=422);assert error['code']=='OWNER_REVISION_UNKNOWN'
    ambiguous=copy.deepcopy(catalog);ambiguous['questions'][1]['revisionId']=ambiguous['questions'][0]['revisionId'];ambiguous_draft=draft(ambiguous,'来源对象修订重复')
    error=c.request('/builder/mapping-runs',{**request,'draftId':ambiguous_draft['id'],'owners':[owners[0]]},expected=422);assert error['code']=='MAPPING_SOURCE_INVALID'
    run=c.request('/builder/mapping-runs',request,expected=201);rid=run['id'];path='/builder/mapping-runs/'+rid
    assert c.request('/builder/mapping-runs',request)['id']==rid
    detail=c.request(path);suggestions=detail['suggestions'];assert len(suggestions)==4 and all(s['status']=='Pending' and s['version']==1 for s in suggestions)
    assert len(c.request('/builder/mapping-runs'))==1
    for s in suggestions:
        assert 'MockOnly' in json.loads(s['validationFlags']) and s['ownerRevisionId']==next(o['ownerRevisionId'] for o in owners if o['ownerId']==s['ownerId'])
        if s['ownerType']!='Question':assert s['evidencePolicy']=='NoEvidence' and all(i['evidenceShare']==0 and i['evidenceMode']=='None' for i in json.loads(s['suggestedItems']))
    changed=copy.deepcopy(catalog);changed['resources'][0]['paperReference']='之后保存的说明，旧运行不得读入';changed['resources'][0]['revisionId']=str(uuid.uuid4())
    updated=c.request('/content/drafts/'+source['id'],{'title':'更新后的源草稿','catalog':changed},method='PUT')
    later=copy.deepcopy(original);later['kcs'][0]['name']+=' · 后发布名称';later['kcs'][0]['revisionId']=str(uuid.uuid4());publish(draft(later,'之后发布的能力库'))
    detail=c.request(path);assert detail['run']['sourcePayload']==source['payload'] and detail['run']['libraryReleaseId']==library['id']
    assert detail['library'][0]['name']==original['kcs'][0]['name'] and detail['suggestions']==suggestions
    print('PASS mapping run deduplicates frozen owner/draft/library revisions; later source edits and releases never replace original proposals')

    lookup={(s['ownerType'],s['ownerId']):s for s in suggestions}
    q=lookup[('Question',catalog['questions'][0]['id'])];q2=lookup[('Question',catalog['questions'][1]['id'])];lesson=lookup[('Lesson',catalog['lessons'][0]['id'])];resource=lookup[('Resource',catalog['resources'][0]['id'])]
    def proposal(s):return {'evidencePolicy':s['evidencePolicy'],'items':json.loads(s['suggestedItems'])}
    def item(s,k,index=1,share=0,mode='None',coverage=1,role='Primary'):
        return {'kcId':k['id'],'kcRevisionId':k['revisionId'],'role':role,'coverageWeight':coverage,'evidenceShare':share,'evidenceMode':mode,'step':None,'sequence':index,'modelScore':None,'sourceRefs':[f"draft:{source['id']}/{s['ownerType']}:{s['ownerRevisionId']}"]}
    def accept(s,p):return {'suggestionId':s['id'],'decision':'Accept','reason':'家长逐项核对测量范围与来源','correctedProposal':p}
    def reject(s):return {'suggestionId':s['id'],'decision':'Reject','reason':'本项尚无独立测量依据，暂不接受'}
    qp={'evidencePolicy':'SingleKC','items':[item(q,original['kcs'][0],share=1,mode='WholeItem')]}
    rp={'evidencePolicy':'NoEvidence','items':[item(resource,original['kcs'][0],coverage=.8)]}
    lp={'evidencePolicy':'NoEvidence','items':[item(lesson,original['kcs'][0],coverage=.4),item(lesson,original['kcs'][1],index=2,coverage=.7)]}
    before=len(c.request('/content')['drafts']);bad={'evidencePolicy':'SingleKC','items':[item(resource,original['kcs'][0],share=1,mode='WholeItem')]}
    error=c.request(path+'/suggestions:decide',{'decisions':[accept(q,qp),accept(resource,bad)]},expected=422);assert error['code']=='INVALID_MAPPING'
    remaining=c.request(path);assert remaining['decisions']==[] and remaining['sets']==[] and remaining['items']==[] and all(s['status']=='Pending' and s['version']==1 for s in remaining['suggestions'])
    assert len(c.request('/content')['drafts'])==before
    for invalid in [None,[None]]:
        c.request(path+'/suggestions:decide',{'decisions':[accept(q,{'evidencePolicy':'SingleKC','items':invalid})]},expected=422)
    print('PASS invalid teaching evidence and malformed items reject the entire batch, preserving every status and creating no partial draft/set/decision')

    bindings=[(s['id'],s['activeReleaseId']) for s in c.request('/students')]
    body={'decisions':[accept(q,qp),accept(resource,rp),reject(q2)]};key=str(uuid.uuid4())
    first=c.request(path+'/suggestions:decide',body,key=key);assert first['pending']==1 and first['draftId'] and len(first['decisionIds'])==3 and first['draftValidationWarnings']==[]
    c.request('/me');assert c.request(path+'/suggestions:decide',body,key=key)==first
    error=c.request(path+'/suggestions:decide',body,expected=409);assert error['code']=='ALREADY_REVIEWED'
    first_detail=c.request(path);assert len(first_detail['decisions'])==3 and len(first_detail['sets'])==2
    first_draft=next(d for d in c.request('/content')['drafts'] if d['id']==first['draftId']);first_catalog=json.loads(first_draft['payload'])
    assert first_draft['status']=='Draft' and first_draft['reviewedBy'] is None
    error=c.request('/content/drafts/'+first_draft['id']+':publish',{'previewHash':'unchecked'},expected=422);assert error['code']=='REVIEW_REQUIRED'
    second=c.request(path+'/suggestions:decide',{'decisions':[accept(lesson,lp)]});assert second['pending']==0 and second['draftId']!=first['draftId'] and second['draftValidationWarnings']==[]
    data=c.request('/content');generated=next(d for d in data['drafts'] if d['id']==second['draftId']);result=json.loads(generated['payload'])
    assert result['questions'][0]==first_catalog['questions'][0] and result['resources'][0]==first_catalog['resources'][0]
    assert result['questions'][0]['id']==catalog['questions'][0]['id'] and result['questions'][0]['revisionId']!=catalog['questions'][0]['revisionId']
    assert result['resources'][0]['paperReference']==catalog['resources'][0]['paperReference'] and result['resources'][0]['revisionId']!=catalog['resources'][0]['revisionId']
    assert result['lessons'][0]['revisionId']!=catalog['lessons'][0]['revisionId'] and len(result['lessons'][0]['kcIds'])==2
    assert next(d for d in data['drafts'] if d['id']==source['id'])['payload']==updated['payload']
    assert next(r for r in data['releases'] if r['id']==library['id'])['payload']==library['payload']
    assert bindings==[(s['id'],s['activeReleaseId']) for s in c.request('/students')]
    reviewed=c.request(path);assert len(reviewed['decisions'])==4 and len(reviewed['sets'])==3 and len(reviewed['items'])==4
    assert all(s['version']==2 for s in reviewed['suggestions']) and all(a['suggestedItems']==b['suggestedItems'] for a,b in zip(reviewed['suggestions'],suggestions))
    assert reviewed['quality']['accepted']==3 and reviewed['quality']['rejected']==1 and reviewed['quality']['pending']==0 and reviewed['quality']['evaluationStatus']=='NotEvaluated'
    def semantic(p):
        p=copy.deepcopy(p);p['items'].sort(key=lambda i:i['sequence'])
        for i in p['items']:i.pop('modelScore',None)
        return p
    expected_corrected=sum(semantic(proposal(next(s for s in suggestions if s['id']==d['suggestionId'])))!=semantic(json.loads(d['correctedPayload'])) for d in reviewed['decisions'] if d['decision']=='Accept')
    assert reviewed['quality']['corrected']==expected_corrected and reviewed['quality']['unchanged']==3-expected_corrected
    for decision in reviewed['decisions']:
        assert decision['reason'] and decision['reviewerId'] and decision['reviewedAt'] and decision['originalPayloadHash']
        if decision['decision']=='Accept':assert hashlib.sha256(decision['correctedPayload'].encode()).hexdigest()==decision['correctedPayloadHash']
    for mapping in reviewed['sets']:
        owner=next(x for x in result[{'Question':'questions','Lesson':'lessons','Resource':'resources'}[mapping['ownerType']]] if x['id']==mapping['ownerId'])
        assert owner['revisionId']==mapping['ownerRevisionId'] and mapping['reviewStatus']=='ReviewedDraft' and mapping['ownerDefinitionHash']
    print('PASS accepted/rejected decisions retain original and corrected payloads; retries are idempotent; later batches include earlier accepted mappings without replacing source or student binding')

    tampered=copy.deepcopy(result);tampered['resources'][0]['paperReference']='不得用同一人工审核修订覆盖说明'
    bad_draft=draft(tampered,'错误复用已接受的对象修订');error=publish(bad_draft,expected=422);assert error['code']=='MAPPING_REVISION_IMMUTABLE'
    changed_kc=copy.deepcopy(result);changed_kc['kcs'][0]['revisionId']=str(uuid.uuid4());error=publish(draft(changed_kc,'改变固定能力修订'),expected=422);assert error['code']=='MAPPING_LIBRARY_REVISION_CHANGED'
    released=publish(generated);assert json.loads(released['payload'])==result
    print('PASS acceptance cannot publish unchecked content; publish rejects changed reviewed owner/KC revisions and preserves the explicitly reviewed snapshot')

    other=Client();other.request('/auth/register',{'userName':'mapping-isolation-'+uuid.uuid4().hex[:12],'password':'mapping-private-'+uuid.uuid4().hex},expected=201)
    other.request('/me')
    other.request(path,expected=404);other.request('/builder/mapping-runs',request,expected=404);other.request(path+'/suggestions:decide',body,expected=404)
    other.request('/content/drafts/'+source['id']+'/reviews',expected=404);other.request('/content/drafts/'+source['id']+'/mapping-preview',expected=404)
    child=Client()
    for cookie in c.jar:child.jar.set_cookie(copy.copy(cookie))
    child.etag=c.etag
    sid=c.request('/students',{'name':'映射隔离验收'},expected=201)['id'];child.request('/students/'+sid+'/child-sessions',{})
    child.request('/builder/mapping-runs',expected=403);child.request(path,expected=403);child.request('/builder/mapping-runs',request,expected=403);child.request(path+'/suggestions:decide',body,expected=403)
    child.request('/content/drafts/'+source['id']+'/reviews',expected=403);child.request('/content/drafts/'+source['id']+'/mapping-preview',expected=403)
    with c.http.open(api_acceptance.BASE+'/family/export') as response:
        manifest=json.loads(zipfile.ZipFile(io.BytesIO(response.read())).read('manifest.json'))
    exported=manifest['data']
    assert len(exported['MappingRun'])==1 and len(exported['MappingSuggestion'])==4 and len(exported['MappingReviewDecision'])==4
    assert all(any(x['id']==s['id'] for x in exported['MappingSetRevision']) for s in reviewed['sets'])
    assert all(any(x['id']==i['id'] for x in exported['MappingSetItem']) for i in reviewed['items'])
    assert any(x['releaseId']==released['id'] for x in exported['ReleaseMappingSet'])
    other.request('/content/releases/'+released['id']+'/mapping-sets',expected=404);child.request('/content/releases/'+released['id']+'/mapping-sets',expected=403)
    print('PASS all mapping routes isolate families and reject children; full private export includes frozen inputs, original proposals, human decisions and normalized sets/items')

    member_input={'userName':'mapping-editor-'+uuid.uuid4().hex[:12],'password':'mapping-private-'+uuid.uuid4().hex,'roles':['ContentEditor']}
    member=c.request('/family/members',member_input,expected=201);editor=Client()
    editor.request('/auth/login',{'userName':member_input['userName'],'password':member_input['password']});editor.request('/me')
    editor.request(path)
    editor_request={**request,'owners':[owners[0]]};editor_key=str(uuid.uuid4())
    editor_run=editor.request('/builder/mapping-runs',editor_request,key=editor_key,expected=201)
    c.request('/me');c.request('/family/members/'+member['id']+'/roles',{'roles':['Parent'],'reason':'隔离用例撤销内容维护权限'},method='PUT')
    editor.request(path,expected=401);editor.request('/builder/mapping-runs',editor_request,key=editor_key,expected=401)
    editor_path='/builder/mapping-runs/'+editor_run['id'];editor_detail=c.request(editor_path);assert editor_detail['quality']['pending']==1
    only=editor_detail['suggestions'][0];metadata_only=proposal(only)
    for i in metadata_only['items']:i['modelScore']=None
    c.request(editor_path+'/suggestions:decide',{'decisions':[accept(only,metadata_only)]})
    quality=c.request(editor_path)['quality'];assert quality['accepted']==1 and quality['corrected']==0 and quality['unchanged']==1
    print('PASS editor role/revoked session/cached reply protected; removing only sorting metadata is not reported as a mapping correction')

    manual_request={**request,'provider':'Manual','owners':[owners[0],owners[2]]}
    manual=c.request('/builder/mapping-runs',manual_request,expected=201)
    assert manual['provider']=='Manual' and manual['model']=='None' and manual['promptVersion']=='manual-source/1'
    assert c.request('/builder/mapping-runs',manual_request)['id']==manual['id']
    manual_path='/builder/mapping-runs/'+manual['id'];md=c.request(manual_path)
    for row in md['suggestions']:
        flags=json.loads(row['validationFlags']);items=json.loads(row['suggestedItems'])
        assert 'ManualSource' in flags and 'CoverageNeedsReview' in flags and 'MockOnly' not in flags and json.loads(row['matches'])==[]
        owner=next(x for x in catalog['questions' if row['ownerType']=='Question' else 'lessons'] if x['id']==row['ownerId'])
        expected=[m['kcId'] for m in owner['mappings']] if row['ownerType']=='Question' else owner['kcIds']
        assert [i['kcId'] for i in items]==expected and all(i['modelScore'] is None for i in items)
    decisions=[]
    for row in md['suggestions']:
        manual_proposal=proposal(row)
        for i in manual_proposal['items']:i['coverageWeight']=.7
        decisions.append(accept(row,manual_proposal))
    accepted_manual=c.request(manual_path+'/suggestions:decide',{'decisions':decisions})
    assert accepted_manual['pending']==0 and accepted_manual['draftValidationWarnings']==[]
    reviewed_manual=c.request(manual_path)
    assert len(reviewed_manual['sets'])==2 and all(i['coverageWeight']==.7 and i['modelScore'] is None for i in reviewed_manual['items'])
    assert reviewed_manual['quality']['evaluationStatus']=='NotEvaluated'
    print('PASS manual source keeps explicit associations without ranking or substitutes; frozen revisions, explicit coverage, review decisions and normalized sets share the same atomic workflow')

    pending_source=draft(original,'撤回能力库后的审核边界');pending_request={**request,'draftId':pending_source['id'],'owners':[owners[0]]}
    pending_run=c.request('/builder/mapping-runs',pending_request,expected=201);pending_path='/builder/mapping-runs/'+pending_run['id'];pending_suggestion=c.request(pending_path)['suggestions'][0]
    c.request('/content/releases/'+library['id']+':withdraw',{'reason':'隔离用例明确撤回旧能力库'})
    error=c.request(pending_path+'/suggestions:decide',{'decisions':[accept(pending_suggestion,proposal(pending_suggestion))]},expected=422);assert error['code']=='LIBRARY_WITHDRAWN'
    assert c.request(pending_path)['suggestions'][0]['status']=='Pending'
    rejected=c.request(pending_path+'/suggestions:decide',{'decisions':[reject(pending_suggestion)]});assert rejected['draftId'] is None and rejected['pending']==0
    assert all('/api/v1'+p in document['paths'] for p in ['/builder/mapping-runs','/builder/mapping-runs/{id}','/builder/mapping-runs/{id}/suggestions:decide'])
    print('PASS withdrawn frozen library blocks acceptance without partial output, permits explicit rejection, and every new route is in the actual contract')
