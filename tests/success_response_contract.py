"""Exercise actual success branches and their emitted OpenAPI structures."""
import base64
import json
import uuid
import urllib.request
from api_acceptance import Client, TODAY
import api_acceptance


def verify(document, client, registered):
    schemas=document['components']['schemas']
    paths=document['paths']
    def validate(schema, value):
        if '$ref' in schema:
            return validate(schemas[schema['$ref'].split('/')[-1]],value)
        if 'anyOf' in schema or 'oneOf' in schema:
            options=schema.get('anyOf',schema.get('oneOf'))
            for option in options:
                try:
                    validate(option,value)
                    return
                except AssertionError:
                    pass
            raise AssertionError('Response does not match declared alternatives')
        types=schema.get('type',[])
        if isinstance(types,str): types=[types]
        actual='null' if value is None else 'boolean' if isinstance(value,bool) else 'integer' if isinstance(value,int) else 'number' if isinstance(value,float) else 'string' if isinstance(value,str) else 'array' if isinstance(value,list) else 'object'
        assert not types or actual in types or actual=='integer' and 'number' in types,(actual,types)
        if isinstance(value,dict):
            assert set(schema.get('required',[]))<=value.keys()
            for name, definition in schema.get('properties',{}).items():
                if name in value: validate(definition,value[name])
        if isinstance(value,list) and 'items' in schema:
            for item in value: validate(schema['items'],item)
    def response(path, method, status, value):
        declared=paths['/api/v1'+path][method]['responses']
        assert str(status) in declared,(path,status,declared.keys())
        validate(declared[str(status)]['content']['application/json']['schema'],value)
    def properties(schema):
        return schemas[schema['$ref'].split('/')[-1]]['properties'] if '$ref' in schema else schema['properties']
    assert not any('AnonymousType' in name for name in schemas)
    for methods in paths.values():
        for method, operation in methods.items():
            if method not in {'get','post','put','patch','delete'}: continue
            for code, body in operation.get('responses',{}).items():
                if code.startswith('2'):
                    assert body.get('content'), 'Success body lacks media declaration'
                    assert all('schema' in media for media in body['content'].values())
    response('/auth/register','post',201,registered)
    assert {code for code in paths['/api/v1/auth/register']['post']['responses'] if code.startswith('2')}=={'201'}
    student=client.request('/students',{'name':'响应契约验收'},expected=201)
    response('/students','post',201,student)
    sid=student['id']
    empty=client.request('/students/'+sid+'/catalog')
    assert empty is None
    response('/students/{id}/catalog','get',200,empty)
    print('PASS actual registration/student 201 and unbound catalog JSON null match declarations')

    source_input={'title':'隔离来源','text':'先乘除后加减，逐步检查同级运算顺序。','usageScope':'原创本地契约验收'}
    source=client.request('/content/sources',source_input,expected=201)
    response('/content/sources','post',201,source)
    existing=client.request('/content/sources',source_input)
    response('/content/sources','post',200,existing)
    assert existing['id']==source['id']
    run_input={'sourceId':source['id']}
    run=client.request('/builder/runs',run_input,expected=202)
    response('/builder/runs','post',202,run)
    existing=client.request('/builder/runs',run_input)
    response('/builder/runs','post',200,existing)
    assert existing['id']==run['id']
    print('PASS actual source 201/200 and builder 202/200 distinct branches match schemas')

    from advanced_api_acceptance import pdf
    pdf_file=client.request('/files',{'name':'source.pdf','mimeType':'application/pdf','base64':base64.b64encode(pdf('Contract source text')).decode()},expected=201)
    pdf_input={'fileId':pdf_file['id'],'title':'PDF契约来源','usageScope':'原创本地验收'}
    imported=client.request('/content/pdf-sources',pdf_input,expected=202)
    response('/content/pdf-sources','post',202,imported)
    prior=client.request('/content/pdf-sources',pdf_input)
    response('/content/pdf-sources','post',200,prior)
    assert prior['id']==imported['source']['id']
    burden=client.request('/students/'+sid+'/parent-burden',{'date':TODAY,'category':'Daily','minutes':1},expected=201)
    response('/students/{id}/parent-burden','post',201,burden)
    corrected=client.request('/parent-burden/'+burden['id']+':correct',{'date':TODAY,'category':'Daily','minutes':2,'reason':'更正隔离验收记录'},expected=201)
    response('/parent-burden/{id}:correct','post',201,corrected)
    print('PASS PDF import 202/200 and immutable parent burden creation/correction 201 match schemas')

    draft=client.request('/content/fixture',{})
    client.request('/content/drafts/'+draft['id']+':review',{})
    preview=client.request('/content/drafts/'+draft['id']+'/preview')
    release=client.request('/content/drafts/'+draft['id']+':publish',{'previewHash':preview['hash']})
    client.request('/students/'+sid+'/content/'+release['id']+':bind',{})
    catalog=client.request('/students/'+sid+'/catalog')
    response('/students/{id}/catalog','get',200,catalog)
    client.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    rev=client.request('/students/'+sid+'/plans/'+TODAY+':generate',{})
    view=client.request('/students/'+sid+'/plans/'+TODAY)
    task=next(t for t in view['tasks'] if t['questionId'])
    client.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash']})
    client.request('/tasks/'+task['id']+':transition',{'status':'InProgress'})
    session=client.request('/tasks/'+task['id']+'/sessions',{})
    answer={'clientSubmissionId':str(uuid.uuid4()),'answer':'999'}
    created=client.request('/sessions/'+session['sessionId']+'/attempts',answer,expected=201)
    response('/sessions/{id}/attempts','post',201,created)
    prior=client.request('/sessions/'+session['sessionId']+'/attempts',answer)
    response('/sessions/{id}/attempts','post',200,prior)
    assert set(created)=={'attempt','grading','assessmentStatus','feedback','explanation'}
    assert set(prior)=={'attempt','grading','assessmentStatus'} and prior['attempt']['id']==created['attempt']['id']
    # Wait for projection to settle so the correction preview's generation remains current.
    import time
    for _ in range(100):
        if client.request('/students/'+sid+'/mastery')['pending']==0: break
        time.sleep(.1)
    else: raise AssertionError('Projection did not settle')
    correction={'result':'Correct','reason':'家长核对响应结构'}
    preview=client.request('/attempts/'+created['attempt']['id']+'/grading-preview',correction)
    grade=client.request('/attempts/'+created['attempt']['id']+'/grading-revisions',{**correction,'previewHash':preview['previewHash']},expected=202)
    response('/attempts/{id}/grading-revisions','post',202,grade)
    print('PASS actual attempt 201/200 preserve different wire fields; grading correction 202 typed')

    for path, expected in [('/api/v1/content/kc-changes',{'id','status'}),('/api/v1/auth/register',{'familyId','role'})]:
        root=paths[path]['get' if path.endswith('kc-changes') else 'post']['responses']['200' if path.endswith('kc-changes') else '201']['content']['application/json']['schema']
        if path.endswith('kc-changes'): root=properties(root)['identities']['items']
        assert set(properties(root))==expected
    print('PASS same anonymous property types with different names have independent schemas')

    samples={'image/png':bytes([137,80,78,71,13,10,26,10])+b'contract', 'image/jpeg':b'\xff\xd8\xffcontract', 'application/pdf':b'%PDF-1.4\ncontract','audio/wav':b'RIFF'+b'\x00'*4+b'WAVE','audio/mpeg':b'ID3contract'}
    media=paths['/api/v1/files/{id}']['get']['responses']['200']['content']
    assert set(media)==set(samples)
    for mime, data in samples.items():
        file=client.request('/content/resource-files' if mime.startswith('audio/') else '/files',{'name':'local.dat','mimeType':mime,'base64':base64.b64encode(data).decode()},expected=201)
        with client.http.open(urllib.request.Request(api_acceptance.BASE+'/files/'+file['id'])) as downloaded:
            assert downloaded.status==200 and downloaded.headers.get_content_type()==mime and downloaded.read()==data
        assert media[mime]['schema']=={'type':'string','format':'binary'}
    for path,mime in [('/students/'+sid+'/export','application/json'),('/family/export','application/zip')]:
        template='/api/v1/students/{id}/export' if path.startswith('/students') else '/api/v1/family/export'
        assert mime in paths[template]['get']['responses']['200']['content']
        with client.http.open(urllib.request.Request(api_acceptance.BASE+path)) as downloaded:
            data=downloaded.read()
            assert downloaded.status==200 and downloaded.headers.get_content_type()==mime
            assert 'attachment' in downloaded.headers.get('Content-Disposition','')
            if mime=='application/json': assert json.loads(data)['student']['id']==sid
            else: assert data.startswith(b'PK')
    print('PASS actual private PDF/PNG/JPEG/WAV/MP3 and student JSON/family ZIP download media and bytes')
