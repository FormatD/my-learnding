"""Real isolated stage/read API, actual Mock generation and review fixture; no quality claim."""
import base64,copy,json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import dotnet,environment
root=Path(__file__).resolve().parent.parent;name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name);child=None;created=False
try:
 with tempfile.TemporaryDirectory(prefix='learning-stage-api-') as directory:
  temp=Path(directory);env.update(ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION'],DeletionLedger=str(temp/'deleted'),FamilyDeletionLedger=str(temp/'families'),ExportDirectory=str(temp/'exports'))
  for k in list(env):
   if k.startswith('Omlx__') or k=='BackupConfigFile':env.pop(k,None)
  subprocess.run(['createdb',name],env=env,check=True);created=True
  with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
  origin=f'http://127.0.0.1:{port}'
  with (temp/'process.log').open('w+') as log:
   child=subprocess.Popen([dotnet(root),str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=root/'src/server',env=env,stdout=log,stderr=log)
   deadline=time.monotonic()+40
   while time.monotonic()<deadline:
    assert child.poll() is None,'isolated API stopped'
    try:
     with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
      if json.load(response)['status']=='ok':break
    except OSError:time.sleep(.1)
   else:raise AssertionError('isolated API not ready')
   api_acceptance.BASE=origin+'/api/v1';a=Client();a.request('/auth/register',{'userName':'stage-'+uuid.uuid4().hex,'password':secrets.token_hex(24)},expected=201);a.request('/me')
   stages=a.request('/builder/stages');assert len(stages)==10 and all(s['total']==0 for s in stages)
   for stage in stages:assert a.request('/builder/stages/'+stage['id']+'/tasks')['items']==[]
   text='\n'.join(f'受控片段{i}：先算乘除，再算加减，明确流程测试。' for i in range(41));source=a.request('/content/sources',{'title':'受控41片段来源','text':text,'usageScope':'SyntheticWorkflowFixture'},expected=201)
   png=base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==')
   image_input={'name':'controlled.png','mimeType':'image/png','base64':base64.b64encode(png).decode(),'expectedSourceHash':source['hash'],'documentHash':'a'*64,'page':7,'printedPage':'受控书内第3页'}
   image=a.request('/content/sources/'+source['id']+'/images',image_input);assert a.request('/content/sources/'+source['id']+'/images',image_input)['id']==image['id']
   a.request('/content/sources/'+source['id']+'/images',{**image_input,'printedPage':'冲突页码'},expected=409)
   a.request('/content/sources/'+source['id']+'/images',{**image_input,'expectedSourceHash':'b'*64},expected=412)
   a.request('/content/sources/'+source['id']+'/images',{**image_input,'page':0},expected=422)
   original=a.request('/builder/reference-images/sources/'+source['id']);assert original[0]['text']==text and len(original[0]['chunks'])==41 and original[0]['pages'][0]['page']==7
   with a.http.open(origin+original[0]['pages'][0]['url']) as response:assert response.read()==png and response.headers['Content-Type']=='image/png'
   for i in range(22):a.request('/content/sources',{'title':'历史来源'+str(i),'text':'受控独立分页来源'+str(i),'usageScope':'SyntheticWorkflowFixture'},expected=201)
   first=a.request('/builder/stages/sources/tasks');second=a.request('/builder/stages/sources/tasks?page=2');assert first['total']==23 and len(first['items'])==20 and len(second['items'])==3 and not(set(x['id'] for x in first['items'])&set(x['id'] for x in second['items']))
   view=a.request('/builder/stages/sources/tasks/'+source['id']);assert view['record']['text']==text
   parts={x['id']:x for x in view['data']};assert parts['chunks']['total']==41 and len(parts['chunks']['items'])==20
   last=a.request('/builder/stages/sources/tasks/'+source['id']+'?related=chunks&relatedPage=3');lastparts={x['id']:x for x in last['data']};assert lastparts['chunks']['page']==3 and len(lastparts['chunks']['items'])==1
   run=a.request('/builder/runs',{'sourceId':source['id'],'provider':'Mock'},expected=202);deadline=time.monotonic()+15
   while time.monotonic()<deadline:
    candidates=[x for x in a.request('/builder')['candidates'] if x['runId']==run['id']]
    if candidates:break
    time.sleep(.1)
   else:raise AssertionError('actual Mock run not completed')
   candidate=candidates[0];before=json.dumps(a.request('/builder'),sort_keys=True)
   extraction=a.request('/builder/stages/extraction/tasks/'+run['id']);parts={x['id']:x for x in extraction['data']};assert extraction['record']['provider']=='Mock' and parts['candidates']['total']==len(candidates) and parts['attempts']['total']>=1 and parts['jobs']['total']==1 and parts['leases']['total']>=1
   review=a.request('/builder/stages/review/tasks/'+candidate['id']);assert review['record']['protocolPayload']==candidate['protocolPayload'] and review['record']['matches']==candidate['matches']
   assert json.dumps(a.request('/builder'),sort_keys=True)==before,'Viewing changed source/run/candidate data'
   a.request('/builder/stages/parsing/tasks/'+run['id'],expected=404)
   for path in ['/builder/stages/unknown/tasks','/builder/stages/sources/tasks?page=0','/builder/stages/sources/tasks?pageSize=51','/builder/stages/sources/tasks/'+source['id']+'?related=unknown','/builder/stages/sources/tasks/'+source['id']+'?relatedPage=0']:a.request(path,expected=422)
   for st,record in [('extraction',run),('review',candidate)]:assert a.request('/builder/reference-images/'+st+'/'+record['id'])[0]['pages'][0]['id']==image['id']
   accepted=a.request('/builder/candidates/'+candidate['id']+':decide',{'decision':'CreateDraft','reason':'受控工作流验证，不是正式内容审核','name':'受控运算次序','behavior':'能先算乘除再算加减','boundary':'仅无括号整数运算'})
   assert a.request('/builder/reference-images/drafts/'+accepted['createdDraftId'])[0]['pages'][0]['id']==image['id']
   fixture=a.request('/content/fixture',{});catalog=json.loads(fixture['payload']);catalog['textbooks'][0]['sourceId']=source['id'];a.request('/content/drafts/'+fixture['id'],{'title':'受控教材草稿','catalog':catalog},method='PUT');a.request('/content/drafts/'+fixture['id']+':review',{});preview=a.request('/content/drafts/'+fixture['id']+'/preview');release=a.request('/content/drafts/'+fixture['id']+':publish',{'previewHash':preview['hash']})
   draft=a.request('/content/drafts',{'title':'受控映射来源','catalog':catalog});owner=catalog['questions'][0];mapping=a.request('/builder/mapping-preparations',{'draftId':draft['id'],'libraryReleaseId':release['id'],'provider':'Mock','owners':[{'ownerType':'Question','ownerId':owner['id'],'ownerRevisionId':owner['revisionId']}]},expected=202)
   deadline=time.monotonic()+15
   while time.monotonic()<deadline:
    mp=a.request('/builder/mapping-preparations/'+mapping['id'])
    if mp['status']=='Succeeded':break
    time.sleep(.1)
   else:raise AssertionError('Mock mapping preparation failed')
   for stage,record in [('mapping',mapping),('mapping-review',{'id':mapping['runId']}),('drafts',draft),('publication',release)]:
    listing=a.request('/builder/stages/'+stage+'/tasks');assert listing['total']>=1;assert a.request('/builder/reference-images/'+stage+'/'+record['id'])[0]['pages'][0]['id']==image['id'];detail=a.request('/builder/stages/'+stage+'/tasks/'+record['id']);assert detail['record']['id']==record['id']
   md=a.request('/builder/stages/mapping-review/tasks/'+mapping['runId']);assert next(x for x in md['data'] if x['id']=='suggestions')['total']==1
   mpdetail=a.request('/builder/stages/mapping/tasks/'+mapping['id']);assert next(x for x in mpdetail['data'] if x['id']=='audits')['total']>=1
   published=a.request('/builder/stages/publication/tasks/'+release['id']);assert next(x for x in published['data'] if x['id']=='reviews')['total']==1
   other=Client();other.request('/auth/register',{'userName':'stage-other-'+uuid.uuid4().hex,'password':secrets.token_hex(24)},expected=201);other.request('/me');assert all(s['total']==0 for s in other.request('/builder/stages'))
   for stage,record in [('sources',source),('extraction',run),('review',candidate),('mapping',mapping),('mapping-review',{'id':mapping['runId']}),('drafts',draft),('publication',release)]:other.request('/builder/stages/'+stage+'/tasks/'+record['id'],expected=404)
   other.request('/builder/reference-images/sources/'+source['id'],expected=404);other.request('/content/source-images/'+image['id'],expected=404)
   student=a.request('/students',{'name':'受控孩子','grade':3},expected=201);kid=Client()
   for cookie in a.jar:kid.jar.set_cookie(copy.copy(cookie))
   actor=a.request('/me')['actor'];family=actor['familyId']
   fixture_sql='BEGIN;'+''.join('INSERT INTO "Audits" ("Id","FamilyId","StudentId","ActorId","Action","Details","CreatedAt") VALUES (\''+str(uuid.uuid4())+'\',\''+family+'\','+("NULL" if sid is None else "'"+sid+"'")+",'"+actor['id']+"','"+action+"','"+json.dumps({'sourceId':source['id'],'marker':'unrelated-learning-fixture'})+"',now());" for action,sid in [('TaskTransition',None),('CandidateReview',student['id'])])+'COMMIT;'
   subprocess.run(['psql','-X','-q','--set=ON_ERROR_STOP=1'],input=fixture_sql,text=True,env=env,check=True,capture_output=True)
   editorname='stage-editor-'+uuid.uuid4().hex;editorpassword=secrets.token_hex(24);a.request('/family/members',{'userName':editorname,'password':editorpassword,'roles':['ContentEditor']},expected=201);editor=Client();editor.request('/auth/login',{'userName':editorname,'password':editorpassword});editor.request('/me');assert editor.request('/builder/reference-images/sources/'+source['id'])[0]['pages'][0]['id']==image['id'];assert editor.request('/students')==[];editor.request('/students/'+student['id']+'/progress',expected=403)
   protected=editor.request('/builder/stages/sources/tasks/'+source['id']);assert all('unrelated-learning-fixture' not in row['details'] for part in protected['data'] if part['id']=='audits' for row in part['items'])
   protected=a.request('/builder/stages/sources/tasks/'+source['id']);assert all('unrelated-learning-fixture' not in row['details'] for part in protected['data'] if part['id']=='audits' for row in part['items'])
   kid.etag=a.etag;kid.request('/students/'+student['id']+'/child-sessions',{})
   for path in ['/builder/reference-images/sources/'+source['id'],'/content/source-images/'+image['id'],'/builder/stages','/builder/stages/sources/tasks','/builder/stages/sources/tasks/'+source['id']]:kid.request(path,expected=403)
   immutable_sql='UPDATE "SourceImage" SET "Page"=8 WHERE "Id"=\''+image['id']+'\';'
   immutable=subprocess.run(['psql','-X','-q','--set=ON_ERROR_STOP=1'],input=immutable_sql,text=True,env=env,capture_output=True);assert immutable.returncode!=0 and 'immutable' in immutable.stderr
   print('PASS source image provenance/hash conflict, immutable database record, private image bytes and downstream review/draft/mapping/publication lineage; actual isolated stage counts/task and related-data pagination, original source/candidate/recall/config/job/lease/calls read without writes, real Mock mapping preparation/review and content publication lineage; invalid stage/page/relation, child/other-family and content-only editor learning-audit isolation; no actual model quality claim')
finally:
 if child is not None and child.poll() is None:child.terminate();child.wait(timeout=15)
 if created:subprocess.run(['dropdb','--force',name],env=env,check=True)
