"""Real rejection paths, shared policy headers and sanitized problem envelopes."""
import json
import subprocess
import urllib.error
import urllib.request
import uuid
import api_acceptance
from api_acceptance import Client


def verify(document,client,env):
    paths=document['paths']
    problem=document['components']['schemas']['ApiProblem']
    assert set(problem['properties'])=={'type','title','status','code','traceId','errors'}
    cookie=document['components']['securitySchemes']['FamilySession']
    assert cookie['type']=='apiKey' and cookie['in']=='cookie' and cookie['name']=='learning-session'
    for path, methods in paths.items():
        for method,operation in methods.items():
            if method not in {'get','post','put','patch','delete'}:continue
            headers={p['name']:p for p in operation.get('parameters',[]) if p.get('in')=='header'}
            write=method not in {'get','head'}
            anonymous=path.startswith('/api/v1/auth/') or path=='/api/health'
            assert bool(operation.get('security'))==(not anonymous)
            assert ('X-Learning-Request' in headers)==write
            assert ('Origin' in headers)==write
            if write:assert not headers['Origin'].get('required',False)
            assert ('Idempotency-Key' in headers)==(write and not anonymous)
            expected_version=write and not anonymous and (method in {'put','patch'} or path in {'/api/v1/content/resource-files','/api/v1/files/upload-tickets'} or path.startswith('/api/v1/files/') and path.endswith(':complete') or path.endswith((':publish',':decide',':correct',':adjust')))
            assert ('If-Match' in headers)==expected_version
            if write:assert headers['X-Learning-Request']['required'] and headers['X-Learning-Request']['schema']['enum']==['1']
            if write and not anonymous: assert headers['Idempotency-Key']['schema']['minLength']==8 and headers['Idempotency-Key']['schema']['maxLength']==100
            if expected_version:assert headers['If-Match']['required']
            if not anonymous:
                assert all('ETag' in reply.get('headers',{}) for status,reply in operation['responses'].items() if status.startswith('2'))
    print('PASS every operation declares actual cookie, CSRF, idempotency and version policy')

    def raw(path, method='GET', body=None, headers=None, who=client, expected=400, code=None, template=None):
        request_headers={'Content-Type':'application/json','X-Learning-Request':'1','Idempotency-Key':str(uuid.uuid4()),'If-Match':client.etag}
        if headers:
            for name,value in headers.items():
                if value is None:request_headers.pop(name,None)
                else:request_headers[name]=value
        req=urllib.request.Request(api_acceptance.BASE+path,method=method,data=body,headers=request_headers)
        try:reply=who.http.open(req,timeout=15)
        except urllib.error.HTTPError as error:reply=error
        data=reply.read()
        assert reply.status==expected,(path,reply.status,data[:200])
        assert reply.headers.get_content_type()=='application/problem+json'
        assert reply.headers.get('Cache-Control')=='no-store'
        value=json.loads(data)
        assert set(value)==set(problem['properties']) and set(problem.get('required',[]))<=value.keys()
        assert value['type']=='about:blank' and value['status']==expected and value['traceId'] and isinstance(value['errors'],dict)
        if code:assert value['code']==code
        assert b'PRIVATE_ERROR_MARKER' not in data and b'Npgsql' not in data and b'SELECT ' not in data
        if template:
            response=paths['/api/v1'+template][method.lower()]['responses'][str(expected)]
            assert 'application/problem+json' in response['content']
        return value

    raw('/students',who=Client(),expected=401,code='LOGIN_REQUIRED',template='/students')
    raw('/students','POST',b'{"name":"blocked"}',headers={'X-Learning-Request':None},expected=403,code='CSRF_DENIED',template='/students')
    raw('/students','POST',b'{"name":"blocked"}',headers={'Origin':'https://other.invalid'},expected=403,code='ORIGIN_DENIED',template='/students')
    raw('/students','POST',b'{"name":"blocked"}',headers={'Idempotency-Key':None},expected=422,code='IDEMPOTENCY_REQUIRED',template='/students')
    raw('/students','POST',b'{"name":"blocked"}',headers={'Idempotency-Key':'short'},expected=422,code='IDEMPOTENCY_REQUIRED',template='/students')
    print('PASS missing login, missing CSRF, wrong origin and missing/short idempotency errors')

    student=client.request('/students',{'name':'失败契约隔离学生'},expected=201)
    sid=student['id']
    raw('/students/'+sid,'PUT',b'{"name":"stale"}',headers={'If-Match':'"0"'},expected=412,code='VERSION_CONFLICT',template='/students/{id}')
    assert next(s for s in client.request('/students') if s['id']==sid)['name']=='失败契约隔离学生'
    body={'name':'幂等冲突隔离学生'};key=str(uuid.uuid4())
    created=client.request('/students',body,key=key,expected=201)
    raw('/students','POST',json.dumps({'name':'different'}).encode(),headers={'Idempotency-Key':key},expected=409,code='IDEMPOTENCY_CONFLICT',template='/students')
    assert next(s for s in client.request('/students') if s['id']==created['id'])['name']==body['name']
    print('PASS stale version and changed-body idempotency conflicts keep original data')

    raw('/students','POST',b'{"name":PRIVATE_ERROR_MARKER',expected=400,code='INVALID_REQUEST',template='/students')
    raw('/students','POST',b'{}',headers={'Content-Type':'text/plain'},expected=415,code='INVALID_REQUEST',template='/students')
    raw('/students','POST',b'',headers={'Content-Length':'15000001'},expected=413,code='TOO_LARGE',template='/students')
    raw('/students/'+str(uuid.uuid4())+'/catalog',expected=404,code='NOT_FOUND',template='/students/{id}/catalog')
    raw('/unknown-contract-route',expected=404,code='NOT_FOUND')
    raw('/unknown-contract-route','POST',b'{}',expected=405,code='METHOD_NOT_ALLOWED')
    print('PASS malformed JSON, unsupported media, oversized body, absent data and unknown API paths')

    release=client.request('/content')['releases'][0]
    client.request('/students/'+sid+'/content/'+release['id']+':bind',{})
    assert env['PGDATABASE'].startswith('learning_fault_openapi_')
    def sql(statement):
        result=subprocess.run(['psql','-X','-q','--set=ON_ERROR_STOP=1'],env=env,input=statement,text=True,capture_output=True,timeout=10)
        assert result.returncode==0,'Disposable database fault setup/cleanup failed.'
    identifier=str(uuid.UUID(release['id']))
    try:
        sql('UPDATE "Releases" SET "Payload"=\'PRIVATE_ERROR_MARKER\' WHERE "Id"=\''+identifier+'\';')
        raw('/students/'+sid+'/catalog',expected=500,code='SERVER_ERROR',template='/students/{id}/catalog')
    finally:
        sql('UPDATE "Releases" SET "Payload"=\''+release['payload'].replace("'","''")+'\' WHERE "Id"=\''+identifier+'\';')
    assert client.request('/students/'+sid+'/catalog')['kcs']
    client.request('/students/'+sid+'/child-sessions',{})
    raw('/content',expected=403,code='FORBIDDEN',template='/content')
    print('PASS real server failure redacts raw input; restored content works; child privilege rejected')


def verify_rate(document):
    # Last step: exhaust the independent listener's window without delaying normal suites.
    caller=Client()
    rejected=None
    for _ in range(11):
        req=urllib.request.Request(api_acceptance.BASE+'/auth/login',data=b'{"userName":"no-such-contract-account","password":"PRIVATE_ERROR_MARKER"}',headers={'Content-Type':'application/json','X-Learning-Request':'1'})
        try:reply=caller.http.open(req,timeout=5)
        except urllib.error.HTTPError as error:reply=error
        data=reply.read()
        assert reply.status in {401,429}
        if reply.status==429:
            rejected=json.loads(data)
            assert reply.headers.get_content_type()=='application/problem+json' and reply.headers.get('Cache-Control')=='no-store'
            break
    assert rejected and rejected['status']==429 and rejected['code']=='RATE_LIMITED' and rejected['traceId'] and rejected['errors']=={}
    assert 'PRIVATE_ERROR_MARKER' not in json.dumps(rejected)
    assert '429' in document['paths']['/api/v1/auth/login']['post']['responses']
    print('PASS actual login limit returns documented 429 problem instead of empty 503')
