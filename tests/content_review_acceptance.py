"""Actual immutable whole-catalog review provenance, stale edits and publication binding."""
import copy,hashlib,io,json,uuid,zipfile
import api_acceptance

def verify(document,c):
    original=json.loads(c.request('/content')['releases'][0]['payload'])
    d=c.request('/content/drafts',{'title':'整份内容审核快照','catalog':original})
    path='/content/drafts/'+d['id'];before=d['payload']
    error=c.request(path+'/reviews',{'expectedDraftVersion':d['version']+1,'reason':'过期页面'},expected=412)
    assert error['code']=='DRAFT_CHANGED' and c.request(path+'/reviews')==[]
    error=c.request(path+'/reviews',{'expectedDraftVersion':d['version'],'reason':'a'*4001},expected=422);assert error['code']=='INVALID_REVIEW'
    error=c.request(path+'/reviews',{},expected=422);assert error['code']=='INVALID_REVIEW'
    reason='核对整份答案与映射，保留版本依据'
    c.request(path+'/reviews',{'expectedDraftVersion':d['version'],'reason':reason})
    old_preview=c.request(path+'/preview');r1=c.request(path+'/reviews')[0]
    assert r1['sourcePayload']==before and r1['sourceTitle']==d['title'] and r1['draftVersion']==d['version'] and r1['scope']=='CatalogSnapshot'
    assert r1['payloadHash']==hashlib.sha256(before.encode()).hexdigest() and r1['reason']==reason and r1['reasonSource']=='UserProvided' and r1['reviewerId'] and r1['reviewedAt'] and r1['publishedReleaseId'] is None
    changed=copy.deepcopy(original);changed['questions'][0]['stem']+='（审核后新编辑）';changed['questions'][0]['revisionId']=str(uuid.uuid4())
    saved=c.request(path,{'title':'编辑后的整份快照','catalog':changed},method='PUT')
    assert saved['status']=='Draft' and saved['reviewedBy'] is None and c.request(path+'/reviews')[0]==r1
    error=c.request(path+':publish',{'previewHash':old_preview['hash']},expected=422);assert error['code']=='REVIEW_REQUIRED'
    c.request(path+':review',{})
    reviews=c.request(path+'/reviews');assert len(reviews)==2
    latest=next(r for r in reviews if r['draftVersion']==saved['version']);assert latest['sourcePayload']==saved['payload'] and latest['reasonSource']=='CommandConfirmation' and latest['scope']=='CatalogSnapshot'
    error=c.request(path+':publish',{'previewHash':old_preview['hash']},expected=412);assert error['code']=='PREVIEW_CHANGED'
    assert all(r['publishedReleaseId'] is None for r in c.request(path+'/reviews'))
    preview=c.request(path+'/preview');release=c.request(path+':publish',{'previewHash':preview['hash']})
    reviews=c.request(path+'/reviews');assert next(r for r in reviews if r['id']==r1['id'])==r1
    assert next(r for r in reviews if r['id']==latest['id'])['publishedReleaseId']==release['id'] and release['payload']==saved['payload']
    with c.http.open(api_acceptance.BASE+'/family/export') as response:
        exported=json.loads(zipfile.ZipFile(io.BytesIO(response.read())).read('manifest.json'))['data']['ContentReviewRecord']
    assert all(any(x['id']==r['id'] and x['sourcePayload']==r['sourcePayload'] for x in exported) for r in reviews)
    assert '/api/v1/content/drafts/{id}/reviews' in document['paths']
    print('PASS stale review cannot approve unseen edit; frozen whole-catalog review retains real reason/confirmation, old approval does not survive saving, publication binds exact reviewed snapshot and private export preserves history')
