"""Isolated hosted API -> real queue -> controlled loopback HTTP -> durable pending suggestions.
Publishes a test fixture only; never writes production family data or claims model quality.
"""
import hashlib,json,secrets,socket,subprocess,tempfile,threading,time,uuid,urllib.request
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import dotnet,environment
root=Path(__file__).resolve().parent.parent
started=threading.Event();release=threading.Event();requests=[];key=secrets.token_hex(24)
class Model(BaseHTTPRequestHandler):
    def log_message(self,*args):pass
    def do_POST(self):
        assert self.path=='/v1/chat/completions' and self.headers.get('Authorization')=='Bearer '+key
        if self.headers.get('Transfer-Encoding','').lower()=='chunked':
            pieces=[];total=0
            while True:
                size=int(self.rfile.readline().split(b';',1)[0].strip(),16)
                if size==0:
                    while self.rfile.readline() not in [b'\r\n',b'\n',b'']:pass
                    break
                total+=size;assert total<=200_000;pieces.append(self.rfile.read(size));assert self.rfile.read(2)==b'\r\n'
            raw=b''.join(pieces)
        else:raw=self.rfile.read(int(self.headers['Content-Length']))
        body=json.loads(raw)
        assert body['model']=='controlled-api-local' and body['response_format']['type']=='json_schema' and body['stream'] is False
        captured=json.loads(body['messages'][1]['content']);requests.append(captured);started.set();assert release.wait(30)
        results=[{'ownerType':o['ownerType'],'ownerId':o['ownerId'],'ownerRevisionId':o['ownerRevisionId'],'decision':'NeedsReview','reason':'受控HTTP夹具，内容需人工核对；不代表实际模型质量。','evidenceQuotes':[],'proposal':{'evidencePolicy':'NoEvidence','items':[]}} for o in captured['owners']]
        payload=json.dumps({'model':'controlled-api-local','choices':[{'finish_reason':'stop','message':{'content':json.dumps({'schemaVersion':'mapping-model/1','results':results},ensure_ascii=False)}}],'usage':{'prompt_tokens':35,'completion_tokens':17}}).encode()
        self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(payload)));self.end_headers();self.wfile.write(payload)
name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name);child=None;created=False
server=ThreadingHTTPServer(('127.0.0.1',0),Model);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
try:
    with tempfile.TemporaryDirectory(prefix='learning-mapping-http-') as directory:
        temp=Path(directory);content_root=temp/'app/server';content_root.mkdir(parents=True);keyfile=temp/'model-key.txt';keyfile.write_text(key);keyfile.chmod(0o600)
        env.update(ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION'],Omlx__Endpoint=f'http://127.0.0.1:{server.server_port}/v1',Omlx__Model='controlled-api-local',Omlx__MaxOutputTokens='1024',Omlx__TimeoutMilliseconds='20000',Omlx__ApiKeyFile=str(keyfile),DeletionLedger=str(temp/'deleted-students'),FamilyDeletionLedger=str(temp/'deleted-families'),ExportDirectory=str(temp/'exports'))
        env.pop('BackupConfigFile',None)
        subprocess.run(['createdb',name],env=env,check=True);created=True
        with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
        origin=f'http://127.0.0.1:{port}'
        with (temp/'process.log').open('w+') as log:
            child=subprocess.Popen([dotnet(root),str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin,'--contentRoot',str(content_root)],cwd=content_root,env=env,stdout=log,stderr=log)
            deadline=time.monotonic()+40
            while time.monotonic()<deadline:
                assert child.poll() is None,'isolated hosted API stopped'
                try:
                    with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                        if json.load(response)['status']=='ok':break
                except OSError:time.sleep(.1)
            else:raise AssertionError('hosted API did not become ready')
            api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/register',{'userName':'mapping-http-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);c.request('/me')
            fixture=c.request('/content/fixture',{});catalog=json.loads(fixture['payload'])
            for item in catalog['resources']+catalog['lessons']:item['revisionId']=str(uuid.uuid4())
            c.request('/content/drafts/'+fixture['id'],{'title':'受控HTTP能力库夹具，非正式质量评测','catalog':catalog},method='PUT');c.request('/content/drafts/'+fixture['id']+':review',{});preview=c.request('/content/drafts/'+fixture['id']+'/preview');library=c.request('/content/drafts/'+fixture['id']+':publish',{'previewHash':preview['hash']})
            draft=c.request('/content/drafts',{'title':'受控映射HTTP来源','catalog':catalog})
            owners=[{'ownerType':kind,'ownerId':item['id'],'ownerRevisionId':item['revisionId']} for kind,item in [('Question',catalog['questions'][0]),('Lesson',catalog['lessons'][0]),('Resource',catalog['resources'][0])]]
            body={'draftId':draft['id'],'libraryReleaseId':library['id'],'owners':owners,'provider':'LocalOmlx'}
            assert c.request('/builder/mapping-runs',body,expected=422)['code']=='MAPPING_JOB_REQUIRED'
            preparation=c.request('/builder/mapping-preparations',body,expected=202)
            if not started.wait(10):
                stopped=c.request('/builder/mapping-preparations/'+preparation['id']);facts=c.request('/builder/mapping-calls?preparationId='+preparation['id'])['calls']
                raise AssertionError(('hosted queue did not reach controlled model HTTP',stopped['status'],stopped.get('error'),[(x['status'],x.get('errorCode')) for x in facts]))
            calls=c.request('/builder/mapping-calls?preparationId='+preparation['id'])['calls'];assert len(calls)==1 and calls[0]['status']=='Started' and calls[0]['inputTokens'] is None and calls[0]['chargedCost'] is None
            # The provider remains paused while a real authenticated family mutation commits.
            c.request('/me');before=time.monotonic();c.request('/content/drafts/'+draft['id'],{'title':'模型等待期间保存的另一个标题','catalog':catalog},method='PUT');assert time.monotonic()-before<3,'local mapping wait blocked family write'
            release.set();deadline=time.monotonic()+20
            while time.monotonic()<deadline:
                result=c.request('/builder/mapping-preparations/'+preparation['id'])
                if result['status'] in ['Succeeded','Failed','Cancelled']:break
                time.sleep(.1)
            else:raise AssertionError('hosted mapping did not finish')
            assert result['status']=='Succeeded',result
            detail=c.request('/builder/mapping-runs/'+preparation['runId']);calls=c.request('/builder/mapping-calls?preparationId='+preparation['id'])['calls']
            assert len(requests)==1 and len(calls)==1 and calls[0]['status']=='Returned' and calls[0]['inputTokens']==35 and calls[0]['outputTokens']==17 and calls[0]['budgetState']=='Settled'
            assert detail['run']['sourceTitle']==draft['title'] and detail['run']['sourcePayload']==draft['payload'] and detail['run']['model']=='controlled-api-local'
            assert len(detail['suggestions'])==3 and detail['decisions']==[] and detail['sets']==[] and detail['quality']['evaluationStatus']=='NotEvaluated'
            for suggestion in detail['suggestions']:
                model=json.loads(suggestion['modelResultPayload']);assert suggestion['status']=='Pending' and model['decision']=='NeedsReview' and model['reason'] and model['evidenceQuotes']==[] and json.loads(suggestion['suggestedItems'])==[]
            print('PASS isolated hosted LocalOmlx API queue uses actual controlled HTTP/auth, records real usage, permits family write during wait, freezes original source and saves unreviewed reasons without publishing')
finally:
    release.set()
    if child is not None and child.poll() is None:child.terminate();child.wait(timeout=15)
    server.shutdown();server.server_close();thread.join(timeout=5)
    if created:subprocess.run(['dropdb','--force',name],env=env,check=True)
