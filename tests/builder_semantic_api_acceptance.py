"""Actual isolated hosted queue + loopback controlled semantic HTTP. No model quality claims."""
import copy,hashlib,io,json,secrets,socket,subprocess,tempfile,threading,time,uuid,urllib.request,zipfile
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import dotnet,environment
root=Path(__file__).resolve().parent.parent
started=threading.Event();release=threading.Event();requests=[];outputs=[];fail_next=[False];key=secrets.token_hex(24)
class Model(BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def do_POST(self):
  assert self.path=='/v1/chat/completions' and self.headers.get('Authorization')=='Bearer '+key
  if self.headers.get('Transfer-Encoding','').lower()=='chunked':
   parts=[]
   while True:
    size=int(self.rfile.readline().split(b';',1)[0].strip(),16)
    if size==0:
     while self.rfile.readline() not in [b'\r\n',b'\n',b'']:pass
     break
    parts.append(self.rfile.read(size));assert self.rfile.read(2)==b'\r\n'
   raw=b''.join(parts)
  else:raw=self.rfile.read(int(self.headers['Content-Length']))
  body=json.loads(raw);assert body['model']=='controlled-api-local' and body['response_format']['type']=='json_schema'
  captured=json.loads(body['messages'][1]['content']);assert captured['version']=='builder-semantic/1';assert 'modelScore' not in captured['candidate'];requests.append(captured);started.set();assert release.wait(30)
  if fail_next[0]:
   fail_next[0]=False;self.send_response(503);self.end_headers();return
  output=json.dumps({'schemaVersion':'builder-semantic/1','candidateId':captured['candidateId'],'decision':'NeedsReview','kcId':None,'kcRevisionId':None,'reason':'受控HTTP原输出，需要人工核对行为和边界；不计语义质量。','candidateQuotes':[],'definitionQuotes':[]},ensure_ascii=False);outputs.append(output)
  payload=json.dumps({'model':'controlled-api-local','choices':[{'finish_reason':'stop','message':{'content':output}}],'usage':{'prompt_tokens':31,'completion_tokens':19}}).encode()
  self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(payload)));self.end_headers();self.wfile.write(payload)
name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name);child=None;created=False
server=ThreadingHTTPServer(('127.0.0.1',0),Model);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
try:
    with tempfile.TemporaryDirectory(prefix='learning-semantic-http-') as directory:
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
            api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/register',{'userName':'semantic-http-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);c.request('/me')
            source=c.request('/content/sources',{'title':'明确受控语义API来源','text':'先算乘除，再算加减；人工核对是否构成独立能力。','usageScope':'SyntheticWorkflowFixture'},expected=201)
            run=c.request('/builder/runs',{'sourceId':source['id'],'provider':'Mock'},expected=202);deadline=time.monotonic()+15
            while time.monotonic()<deadline:
                rows=c.request('/builder');candidates=[x for x in rows['candidates'] if x['runId']==run['id']]
                if candidates:break
                time.sleep(.1)
            else:raise AssertionError('Actual Mock extraction did not complete')
            candidate=candidates[0];before=json.dumps(candidate,sort_keys=True);p=c.request('/builder/candidates/'+candidate['id']+'/semantic-preparations',{},expected=202)
            assert started.wait(10),'Hosted semantic worker did not reach controlled HTTP'
            calls=c.request('/builder/semantic-calls?preparationId='+p['id']);assert calls['total']==1 and calls['calls'][0]['status']=='Started' and calls['responses']==[]
            c.request('/me');now=time.monotonic();c.request('/content/sources',{'title':'模型等待期间独立家庭写入','text':'另一个明确受控来源，用于验证无长期家庭锁。','usageScope':'SyntheticWorkflowFixture'},expected=201);assert time.monotonic()-now<3
            release.set();deadline=time.monotonic()+15
            while time.monotonic()<deadline:
                detail=c.request('/builder/semantic-preparations/'+p['id'])
                if detail['preparation']['status'] in ['Succeeded','Failed','Cancelled']:break
                time.sleep(.1)
            assert detail['preparation']['status']=='Succeeded',detail
            assert detail['result']['decision']=='NeedsReview' and detail['input']['candidateId']==candidate['id'] and detail['suggestion']['responseId']
            calls=c.request('/builder/semantic-calls?preparationId='+p['id']);assert calls['total']==1 and calls['calls'][0]['inputTokens']==31 and calls['calls'][0]['outputTokens']==19 and len(calls['responses'])==1
            raw=c.request('/builder/semantic-responses/'+calls['responses'][0]['id']);assert raw['outputPayload']==outputs[0] and raw['outputHash']==hashlib.sha256(outputs[0].encode()).hexdigest()
            stage=c.request('/builder/stages/semantic/tasks/'+p['id']);parts={x['id']:x for x in stage['data']}
            assert stage['record']['candidateId']==candidate['id'] and parts['calls']['total']==1 and parts['responses']['items'][0]['outputPayload']==outputs[0] and parts['suggestions']['total']==1 and parts['jobs']['total']==1 and parts['leases']['total']>=1
            after=next(x for x in c.request('/builder')['candidates'] if x['id']==candidate['id']);assert json.dumps(after,sort_keys=True)==before
            assert c.request('/builder/candidates/'+candidate['id']+'/semantic-preparations',{},expected=202)['id']==p['id'] and len(requests)==1
            assert c.request('/builder/semantic-preparations?candidateId='+candidate['id'])['total']==1
            c.request('/builder/semantic-preparations?page=0',expected=422);c.request('/builder/semantic-calls?pageSize=51',expected=422)
            c.request('/builder/semantic-preparations/'+p['id']+':retry',{'reason':'Completed task cannot be retried'},expected=409)
            c.request('/builder/semantic-calls/'+calls['calls'][0]['id']+':reconcile',{'providerFinished':True,'reason':'Settled','receiptReference':'Controlled settled return'},expected=409)
            with c.http.open(api_acceptance.BASE+'/family/export') as response,zipfile.ZipFile(io.BytesIO(response.read())) as archive:data=json.loads(archive.read('manifest.json'))['data']
            assert any(x['id']==raw['id'] and x['outputPayload']==outputs[0] for x in data['BuilderSemanticResponse']) and any(x['id']==detail['suggestion']['id'] for x in data['BuilderSemanticSuggestion'])
            other=Client();other.request('/auth/register',{'userName':'semantic-other-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);other.request('/me')
            assert other.request('/builder/semantic-preparations')['total']==0
            for path in ['/builder/semantic-preparations/'+p['id'],'/builder/semantic-responses/'+raw['id'],'/builder/semantic-calls?preparationId='+p['id']]:other.request(path,expected=404)
            other.request('/builder/candidates/'+candidate['id']+'/semantic-preparations',{},expected=404)
            student=c.request('/students',{'name':'受控孩子','grade':3,'timeZone':'Asia/Shanghai'},expected=201);kid=Client()
            for cookie in c.jar:kid.jar.set_cookie(copy.copy(cookie))
            kid.etag=c.etag;kid.request('/students/'+student['id']+'/child-sessions',{})
            for path in ['/builder/semantic-preparations','/builder/semantic-calls','/builder/semantic-responses/'+raw['id']]:kid.request(path,expected=403)
            kid.request('/builder/candidates/'+candidate['id']+'/semantic-preparations',{},expected=403)
            fail_next[0]=True;c.request('/me');source2=c.request('/content/sources',{'title':'受控失败与人工恢复','text':'这是另一个受控来源，核对原返回失败及人工恢复。','usageScope':'SyntheticWorkflowFixture'},expected=201);run2=c.request('/builder/runs',{'sourceId':source2['id'],'provider':'Mock'},expected=202);deadline=time.monotonic()+15
            while time.monotonic()<deadline:
                candidate2=next((x for x in c.request('/builder')['candidates'] if x['runId']==run2['id']),None)
                if candidate2:break
                time.sleep(.1)
            assert candidate2
            p2=c.request('/builder/candidates/'+candidate2['id']+'/semantic-preparations',{},expected=202);deadline=time.monotonic()+15
            while time.monotonic()<deadline:
                failed=c.request('/builder/semantic-preparations/'+p2['id'])
                if failed['preparation']['status']=='Failed':break
                time.sleep(.1)
            assert failed['preparation']['error']=='LOCAL_PROVIDER_HTTP_FAILED',failed
            original=c.request('/builder/semantic-calls?preparationId='+p2['id'])['calls'][0];assert original['budgetState']=='Unresolved' and original['inputTokens'] is None and original['outputTokens'] is None
            evidence={'providerFinished':True,'inputTokens':None,'outputTokens':None,'reason':'受控HTTP服务明确拒绝请求，不涉及外部模型；已核对该受控分支结束','receiptReference':'Controlled 503 branch completed without any model invocation'}
            assert c.request('/builder/semantic-preparations/'+p2['id']+':retry',{'reason':'不能仅凭任务停止推定提供者结束'},expected=422)['code']=='BUILDER_USAGE_RECONCILIATION_REQUIRED'
            other.request('/builder/semantic-calls/'+original['id']+':reconcile',evidence,expected=404);kid.request('/builder/semantic-calls/'+original['id']+':reconcile',evidence,expected=403)
            c.request('/builder/semantic-calls/'+original['id']+':reconcile',{**evidence,'providerFinished':False},expected=422);c.request('/builder/semantic-calls/'+original['id']+':reconcile',evidence,expected=201)
            c.request('/builder/semantic-calls/'+original['id']+':reconcile',evidence,expected=409)
            c.request('/builder/semantic-preparations/'+p2['id']+':retry',{'reason':'已观察受控服务拒绝请求并结束，原输入核对一致'},expected=202);deadline=time.monotonic()+15
            while time.monotonic()<deadline:
                recovered=c.request('/builder/semantic-preparations/'+p2['id'])
                if recovered['preparation']['status'] in ['Succeeded','Failed']:break
                time.sleep(.1)
            assert recovered['preparation']['status']=='Succeeded' and recovered['preparation']['retryRound']==1,recovered
            final_calls=c.request('/builder/semantic-calls?preparationId='+p2['id']);assert final_calls['total']==2 and next(x for x in final_calls['calls'] if x['id']==original['id'])==original
            assert len(final_calls['reconciliations'])==1 and len(requests)==3
            print('PASS real hosted semantic failed HTTP holds unknown slot; foreign/child/unfinished/duplicate reconciliation gates, owner append preserves null facts and explicit audited retry returns through actual queue')
            print('PASS actual hosted semantic API -> claimed worker -> controlled HTTP/auth -> independent raw receipt/usage -> immutable suggestion; family writes during wait, idempotency, scope/child gates, export; no actual model or human quality claims')
finally:
    release.set()
    if child is not None and child.poll() is None:child.terminate();child.wait(timeout=15)
    server.shutdown();server.server_close();thread.join(timeout=5)
    if created:subprocess.run(['dropdb','--force',name],env=env,check=True)
