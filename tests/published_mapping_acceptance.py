"""Frozen release mappings cover direct authoring and preserve individually reviewed weights."""
import copy,io,json,uuid,zipfile
import api_acceptance

def verify(document,c):
    def publish(catalog,title):
        d=c.request('/content/drafts',{'catalog':catalog,'title':title})
        c.request('/content/drafts/'+d['id']+'/reviews',{'expectedDraftVersion':d['version'],'reason':'接口验收核对完整投影'})
        p=c.request('/content/drafts/'+d['id']+'/preview')
        return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']})
    def read(r):
        data=c.request('/content/releases/'+r['id']+'/mapping-sets');data['sets'].sort(key=lambda s:s['id']);return data
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
        if s['contentReviewRecordId']:
            explicit=any(r['ownerType']==t and r['ownerId']==o['id'] and r['origin']!='Default' for r in catalog.get('mappingCoverage') or [])
            assert s['coverageOrigin']==('CatalogReviewed' if explicit else 'CatalogDefault') and all(i['modelScore'] is None for i in items)
            for i,m in zip(items,expected):
                row=next((r for r in catalog.get('mappingCoverage') or [] if r['ownerType']==t and r['ownerId']==o['id'] and r['kcId']==m['kcId'] and r['role']==m['role'] and r['evidenceMode']==m['mode'] and r['step']==m.get('step') and r['evidenceShare']==m['share']),None)
                assert i['coverageWeight']==(row['coverageWeight'] if row else 1)
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

    bad=copy.deepcopy(changed);bad['mappingCoverage']=None;bad['questions'][0]['revisionId']=str(uuid.uuid4())
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

    source_draft=next(d for d in c.request('/content')['drafts'] if d['payload']==original['payload'])
    preview=c.request('/content/drafts/'+source_draft['id']+'/mapping-preview')
    inherited=preview['catalog'];assert preview['draftVersion']==source_draft['version']
    assert inherited['mappingCoverage'] and all(r['origin']=='Inherited' and r['sourceSetRevisionId'] for r in inherited['mappingCoverage'])
    proof=next(r for r in inherited['mappingCoverage'] if r['coverageWeight']==.8)
    before_source=read(original)
    for name in ['kcs','questions','resources','lessons','textbooks','units','courses']:
        for item in inherited.get(name) or []:item['revisionId']=str(uuid.uuid4())
    inherited['resources'][0]['paperReference']+='（修订讲解，保留已核对覆盖）'
    revised=publish(inherited,'所有叶修订改变仍继承真实权重');fixed=read(revised)
    assert fixed['status']=='Bound' and read(original)==before_source
    binding=next(b for b in fixed['bindings'] if b['ownerType']==proof['ownerType'] and b['ownerId']==proof['ownerId'])
    target_items=[i for i in fixed['items'] if i['setRevisionId']==binding['setRevisionId']]
    item=next(i for i in target_items if i['kcId']==proof['kcId'] and i['role']==proof['role'] and i['step']==proof['step'])
    assert item['coverageWeight']==.8 and item['evidenceShare']==proof['evidenceShare'] and 'mapping-set:'+proof['sourceSetRevisionId'] in json.loads(item['sourceRefs'])
    assert next(s for s in fixed['sets'] if s['id']==binding['setRevisionId'])['coverageOrigin']=='CatalogReviewed'
    bad=copy.deepcopy(inherited);bad['mappingCoverage'][0]['sourceSetRevisionId']=str(uuid.uuid4())
    count=len(c.request('/content')['drafts']);err=c.request('/content/drafts',{'catalog':bad,'title':'未知来源不能伪造继承'},expected=422);assert err['code']=='COVERAGE_SOURCE_UNKNOWN'
    assert len(c.request('/content')['drafts'])==count
    bad=copy.deepcopy(inherited);next(r for r in bad['mappingCoverage'] if r['coverageWeight']==.8)['coverageWeight']=.6
    err=c.request('/content/drafts',{'catalog':bad,'title':'继承来源不能捏造权重'},expected=422);assert err['code']=='COVERAGE_SOURCE_CHANGED'
    bad=copy.deepcopy(inherited);bad['questions'][0]['mappings'][0]['role']='Context'
    err=c.request('/content/drafts',{'catalog':bad,'title':'归因变化不能盲目继承'},expected=422);assert err['code']=='INVALID_MAPPING_COVERAGE'
    explicit=copy.deepcopy(inherited);row=next(r for r in explicit['mappingCoverage'] if r['coverageWeight']==.8);row.update(coverageWeight=.6,origin='Explicit',sourceSetRevisionId=None)
    # A new mapping container can change coverage without changing the immutable question definition.
    newer=publish(explicit,'明确重审覆盖权重');new_fixed=read(newer)
    b=next(b for b in new_fixed['bindings'] if b['ownerType']==row['ownerType'] and b['ownerId']==row['ownerId'])
    assert b['ownerRevisionId']==binding['ownerRevisionId'] and b['setRevisionId']!=binding['setRevisionId']
    assert any(i['setRevisionId']==b['setRevisionId'] and i['kcId']==row['kcId'] and i['coverageWeight']==.6 for i in new_fixed['items']), (row,[i for i in new_fixed['items'] if i['setRevisionId']==b['setRevisionId']])
    assert read(revised)==fixed
    assert '/api/v1/content/drafts/{id}/mapping-preview' in document['paths']
    print('PASS preview carries real item weights across all leaf revisions; inherited provenance rejects unknown/forged/changed associations atomically; explicit coverage creates an independent mapping revision without rewriting old releases')

    changed_human=copy.deepcopy(catalog)
    human_row=next(r for r in changed_human['mappingCoverage'] if r['ownerType']==proof['ownerType'] and r['ownerId']==proof['ownerId'] and r['kcId']==proof['kcId'] and r['role']==proof['role'] and r['step']==proof['step'])
    human_row.update(coverageWeight=.55,origin='Explicit',sourceSetRevisionId=None)
    remapped=publish(changed_human,'逐项原映射另建整份审核的覆盖修订');remapped_data=read(remapped)
    b=next(b for b in remapped_data['bindings'] if b['ownerType']==proof['ownerType'] and b['ownerId']==proof['ownerId'])
    old_b=next(b for b in data['bindings'] if b['ownerType']==proof['ownerType'] and b['ownerId']==proof['ownerId'])
    assert b['setRevisionId']!=old_b['setRevisionId'] and b['ownerRevisionId']==old_b['ownerRevisionId']
    assert any(i['setRevisionId']==b['setRevisionId'] and i['kcId']==proof['kcId'] and i['coverageWeight']==.55 for i in remapped_data['items']) and read(original)==data
    assert next(s for s in remapped_data['sets'] if s['id']==b['setRevisionId'])['contentReviewRecordId'] and not next(s for s in remapped_data['sets'] if s['id']==b['setRevisionId'])['reviewDecisionId']
    print('PASS coverage-only re-review also creates an independent catalog-reviewed container for an unchanged human-reviewed owner, preserving the original item review and fixed library revision')
