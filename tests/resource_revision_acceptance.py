"""Resource revisions preserve legacy snapshots and reject replacement under a published ID."""
import copy,json,uuid


def verify(document,c):
    def draft(catalog,title):return c.request('/content/drafts',{'title':title,'catalog':catalog})
    def publish(d,expected=200):
        c.request('/content/drafts/'+d['id']+':review',{})
        p=c.request('/content/drafts/'+d['id']+'/preview')
        return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']},expected=expected)
    source=c.request('/content/fixture',{});catalog=json.loads(source['payload']);resource=catalog['resources'][0]
    namespace=uuid.uuid4().hex
    for i,kc in enumerate(catalog['kcs']):kc['code']=f'RESOURCE.TEST.{namespace}.{i}'
    assert resource['revisionId'] and len({r['revisionId'] for r in catalog['resources']})==len(catalog['resources'])
    # Deliberately represent a pre-revision resource snapshot, without mutating any real release.
    legacy=copy.deepcopy(catalog)
    for r in legacy['resources']:r['revisionId']=None
    old=publish(draft(legacy,'旧资源格式兼容检查'));assert json.loads(old['payload'])['resources'][0]['revisionId'] is None
    formal=publish(draft(catalog,'资源独立修订'));assert json.loads(formal['payload'])['resources'][0]['revisionId']==resource['revisionId']
    releases=c.request('/content')['releases'];count=len(releases)
    for field,value in [('paperReference','新说明不能覆盖旧资源修订'),('kcIds',[catalog['kcs'][1]['id']]),('minutes',12)]:
        changed=copy.deepcopy(catalog);changed['resources'][0][field]=value
        error=publish(draft(changed,'错误复用资源修订'),expected=422)
        assert error['code']=='REVISION_IMMUTABLE' and len(c.request('/content')['releases'])==count
    error=publish(draft(legacy,'不能降级隐藏修订'),expected=422)
    assert error['code']=='RESOURCE_REVISION_REQUIRED' and len(c.request('/content')['releases'])==count
    print('PASS legacy resource snapshot retained; published text, mapping and duration immutable; version downgrade blocked with no partial releases')
    changed=copy.deepcopy(catalog);changed['resources'][0]['paperReference']='家长核对后的新纸笔执行说明';changed['resources'][0]['revisionId']=str(uuid.uuid4())
    new=publish(draft(changed,'同资源新修订'))
    rows=c.request('/content')['releases']
    assert json.loads(next(r for r in rows if r['id']==formal['id'])['payload'])==catalog
    assert json.loads(next(r for r in rows if r['id']==old['id'])['payload'])==legacy
    assert json.loads(new['payload'])['resources'][0]['id']==resource['id'] and json.loads(new['payload'])['resources'][0]['revisionId']!=resource['revisionId']
    print('PASS new resource revision keeps stable identity and all older release bytes/definitions')
    duplicate=copy.deepcopy(catalog);duplicate['resources'][1]['id']=resource['id']
    bad=draft(duplicate,'重复资源身份');c.request('/content/drafts/'+bad['id']+':review',{},expected=422)
    assert 'revisionId' in document['components']['schemas']['Resource']['properties']
    print('PASS duplicate resource identity rejected at review; nullable revision contract exposed')
