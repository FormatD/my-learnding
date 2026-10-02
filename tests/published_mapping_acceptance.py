"""Frozen release mappings cover direct authoring and preserve individually reviewed weights."""
import copy,io,json,uuid,zipfile
import api_acceptance

def verify(document,c):
    def publish(catalog,title):
        d=c.request('/content/drafts',{'catalog':catalog,'title':title})
        c.request('/content/drafts/'+d['id']+'/reviews',{'expectedDraftVersion':d['version'],'reason':'接口验收核对完整投影'})
        p=c.request('/content/drafts/'+d['id']+'/preview')
        return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']})
    def read(r):return c.request('/content/releases/'+r['id']+'/mapping-sets')
    def exported():
        with c.http.open(api_acceptance.BASE+'/family/export') as response:
            return json.loads(zipfile.ZipFile(io.BytesIO(response.read())).read('manifest.json'))['data']
    original=c.request('/content')['releases'][0];catalog=json.loads(original['payload']);data=read(original)
    owners=[(t,o) for t,collection in [('Question','questions'),('Lesson','lessons'),('Resource','resources')] for o in catalog[collection] if o.get('revisionId')]
    assert data['status']=='Bound' and len(data['bindings'])==len(owners)==len(data['sets']) and not data['unversionedOwners']
    for t,o in owners:
        binding=next(b for b in data['bindings'] if b['ownerType']==t and b['ownerId']==o['id'])
        s=next(s for s in data['sets'] if s['id']==binding['setRevisionId']);items=sorted([i for i in data['items'] if i['setRevisionId']==s['id']],key=lambda i:i['sequence'])
        assert binding['ownerRevisionId']==s['ownerRevisionId']==o['revisionId'] and bool(s['reviewDecisionId'])!=bool(s['contentReviewRecordId'])
        expected=o['mappings'] if t=='Question' else [{'kcId':k,'role':'Primary','share':0,'mode':'None','step':None} for k in dict.fromkeys(o['kcIds'])]
        assert len(items)==len(expected)
        for i,m in zip(items,expected):
            assert i['kcId']==m['kcId'] and i['kcRevisionId']==next(k['revisionId'] for k in catalog['kcs'] if k['id']==m['kcId'])
            assert (i['role'],i['evidenceShare'],i['evidenceMode'],i['step'])==(m['role'],m['share'],m['mode'],m.get('step'))
        if s['contentReviewRecordId']:assert s['coverageOrigin']=='CatalogDefault' and all(i['coverageWeight']==1 and i['modelScore'] is None for i in items)
        else:assert s['coverageOrigin']=='HumanReviewed'
    human_sets={s['id'] for s in data['sets'] if s['reviewDecisionId']}
    assert human_sets and any(i['coverageWeight']==.8 for i in data['items'] if i['setRevisionId'] in human_sets)
    print('PASS direct question/lesson/resource release has fixed normalized bindings; observed steps retain actual budget; whole-catalog review is distinguished from item review and human coverage is preserved')

    human_kcs={i['kcId'] for i in data['items'] if i['setRevisionId'] in human_sets}
    target=next(k for k in catalog['kcs'] if k['id'] not in human_kcs and any(m['kcId']==k['id'] for q in catalog['questions'] for m in q['mappings']))
    changed=copy.deepcopy(catalog)
    for k in changed['kcs']:
        if k['id']==target['id']:k['revisionId']=str(uuid.uuid4());k['name']+='（不改变测量含义的名称修订）';new_kc_revision=k['revisionId']
    renamed=publish(changed,'能力名称修订的独立映射版本');after=read(renamed)
    assert after['status']=='Bound' and next(r for r in c.request('/content')['releases'] if r['id']==original['id'])['payload']==original['payload'] and read(original)==data
    affected=next(q for q in catalog['questions'] if any(m['kcId']==target['id'] for m in q['mappings']))
    old_binding=next(b for b in data['bindings'] if b['ownerType']=='Question' and b['ownerId']==affected['id']);new_binding=next(b for b in after['bindings'] if b['ownerType']=='Question' and b['ownerId']==affected['id'])
    assert old_binding['ownerRevisionId']==new_binding['ownerRevisionId']==affected['revisionId'] and old_binding['setRevisionId']!=new_binding['setRevisionId']
    old_set=next(s for s in data['sets'] if s['id']==old_binding['setRevisionId']);new_set=next(s for s in after['sets'] if s['id']==new_binding['setRevisionId'])
    assert new_set['revisionNo']>old_set['revisionNo'] and new_set['ownerDefinitionHash']==old_set['ownerDefinitionHash']
    assert any(i['setRevisionId']==new_set['id'] and i['kcRevisionId']==new_kc_revision for i in after['items'])
    repeated=publish(changed,'相同内容复用准确映射版本');same=read(repeated)
    assert {b['setRevisionId'] for b in same['bindings']}=={b['setRevisionId'] for b in after['bindings']}
    print('PASS unchanged owner can bind a new mapping set for an explicitly reviewed descriptive KC revision; old fixed set is untouched; identical later release reuses exact mappings')

    bad=copy.deepcopy(changed);bad['questions'][0]['revisionId']=str(uuid.uuid4())
    q=next(q for q in bad['questions'] if q['id']==affected['id']);q['revisionId']=str(uuid.uuid4());q['mappings'][0]['share']=.3333333
    before=exported();release_count=len(c.request('/content')['releases'])
    d=c.request('/content/drafts',{'catalog':bad,'title':'高精度证据不能被表列静默舍入'})
    c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview')
    error=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']},expected=422);assert error['code']=='MAPPING_PRECISION_INVALID'
    end=exported()
    assert len(c.request('/content')['releases'])==release_count and all({r['id']:r for r in end[t]}=={r['id']:r for r in before[t]} for t in ['MappingSetRevision','MappingSetItem','ReleaseMappingSet'])
    assert c.request('/content/drafts/'+d['id']+'/reviews')[0]['publishedReleaseId'] is None
    legacy=next(r for r in c.request('/content')['releases'] if any(x.get('revisionId') is None for x in json.loads(r['payload'])['resources']))
    legacy_detail=read(legacy);assert legacy_detail['status']=='UnversionedOwners' and legacy_detail['unversionedOwners']
    assert '/api/v1/content/releases/{id}/mapping-sets' in document['paths'] and all(any(b['setRevisionId']==s['id'] for s in end['MappingSetRevision']) for b in end['ReleaseMappingSet'])
    print('PASS fractional precision rejects entire publication without partial sets/items/bindings; unversioned legacy resources are explicit gaps, private export keeps every real release binding')
