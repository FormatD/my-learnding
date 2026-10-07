"""Opt-in actual oMLX mapping queue diagnostic, only a disposable family/database.
The temporary published library is an automated workflow fixture, NOT human gold or formal approval.
"""
import json,os,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import dotnet,environment
if os.environ.get('OMLX_MAPPING_QUEUE_SMOKE')!='1':raise SystemExit('Set OMLX_MAPPING_QUEUE_SMOKE=1 for an actual local provider diagnostic.')
root=Path(__file__).resolve().parent.parent
configuration=json.loads((root/'.local/omlx.json').read_text())['Omlx'];pack=json.loads((root/'.local/textbook-workflow/division-original-draft/content-pack.json').read_text());catalog=pack['catalog']
question=next(q for q in catalog['questions'] if q['policy']=='SingleKC' and '至少' in q['stem']);selected=[('Question',question),('Lesson',catalog['lessons'][0]),('Resource',catalog['resources'][0])]
name='learning_fault_'+uuid.uuid4().hex[:12];env=environment(name);created=False;child=None
report={'scope':'IsolatedWorkflowDiagnostic','textbook':'北师大二年级下册第一单元除法','formalStudentScopeChanged':False,'libraryReview':'Automated disposable workflow fixture; not human approval or gold','modelResultReview':'Pending','status':'Started'}
output=root/'.local/textbook-workflow/mapping-queue-diagnostics'/('actual-'+uuid.uuid4().hex+'.json');output.parent.mkdir(parents=True,exist_ok=True);output.parent.chmod(0o700)
def save():
    scratch=output.with_suffix('.tmp');fd=os.open(scratch,os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600)
    with os.fdopen(fd,'w') as stream:json.dump(report,stream,ensure_ascii=False,indent=2)
    os.replace(scratch,output)
save()
try:
    with tempfile.TemporaryDirectory(prefix='learning-mapping-actual-') as directory:
        temp=Path(directory);content_root=temp/'app/server';content_root.mkdir(parents=True)
        env.update(ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION'],Omlx__Endpoint=configuration['Endpoint'],Omlx__Model=configuration['Model'],Omlx__MaxOutputTokens=str(configuration.get('MaxOutputTokens',4096)),Omlx__TimeoutMilliseconds=str(configuration.get('TimeoutMilliseconds',600000)),Omlx__ApiKeyFile=str(root/'.local/omlx-api-key.txt'),DeletionLedger=str(temp/'deleted-students'),FamilyDeletionLedger=str(temp/'deleted-families'),ExportDirectory=str(temp/'exports'))
        env.pop('BackupConfigFile',None);subprocess.run(['createdb',name],env=env,check=True);created=True
        with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
        origin=f'http://127.0.0.1:{port}'
        with (temp/'process.log').open('w+') as log:
            child=subprocess.Popen([dotnet(root),str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin,'--contentRoot',str(content_root)],cwd=content_root,env=env,stdout=log,stderr=log)
            deadline=time.monotonic()+40
            while time.monotonic()<deadline:
                assert child.poll() is None,'isolated actual-model API stopped'
                try:
                    with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                        if json.load(response)['status']=='ok':break
                except OSError:time.sleep(.1)
            else:raise AssertionError('isolated actual-model API did not become ready')
            api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/register',{'userName':'mapping-actual-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);c.request('/me')
            library_draft=c.request('/content/drafts',{'title':'隔离工作流能力库夹具，不代表正式人工审核','catalog':catalog});c.request('/content/drafts/'+library_draft['id']+':review',{});preview=c.request('/content/drafts/'+library_draft['id']+'/preview');library=c.request('/content/drafts/'+library_draft['id']+':publish',{'previewHash':preview['hash']})
            draft=c.request('/content/drafts',{'title':'隔离实际模型原创内容，全部待审核','catalog':catalog});body={'draftId':draft['id'],'libraryReleaseId':library['id'],'owners':[{'ownerType':kind,'ownerId':item['id'],'ownerRevisionId':item['revisionId']} for kind,item in selected],'provider':'LocalOmlx'}
            preparation=c.request('/builder/mapping-preparations',body,expected=202);report.update(preparation=preparation,input=draft,libraryRelease=library,model=configuration['Model']);save()
            deadline=time.monotonic()+configuration.get('TimeoutMilliseconds',600000)/1000+60
            while time.monotonic()<deadline:
                current=c.request('/builder/mapping-preparations/'+preparation['id']);facts=c.request('/builder/mapping-calls?preparationId='+preparation['id'])
                report.update(status=current['status'],preparation=current,calls=facts['calls'],reconciliations=facts['reconciliations']);save()
                if current['status'] in ['Succeeded','Failed','Cancelled']:break
                assert child.poll() is None,'actual-model API stopped during diagnostic';time.sleep(1)
            else:raise AssertionError('actual local mapping queue did not finish before bounded deadline')
            if current['status']=='Succeeded':report['result']=c.request('/builder/mapping-runs/'+preparation['runId']);save()
            assert current['status']=='Succeeded',(current['status'],current.get('error'))
            result=report['result'];calls=report['calls'];assert len(calls)==1 and calls[0]['status']=='Returned' and calls[0]['budgetState']=='Settled' and calls[0]['model']==configuration['Model'] and calls[0]['chargedCost']==0
            assert len(result['suggestions'])==3 and result['decisions']==[] and result['sets']==[] and result['quality']['evaluationStatus']=='NotEvaluated'
            for row in result['suggestions']:
                item=json.loads(row['modelResultPayload']);assert row['status']=='Pending' and item['ownerId']==row['ownerId'] and item['ownerRevisionId']==row['ownerRevisionId'] and item['reason']
                if row['ownerType']!='Question':assert item['proposal']['evidencePolicy']=='NoEvidence' and all(x['evidenceShare']==0 and x['evidenceMode']=='None' for x in item['proposal']['items'])
            print('PASS actual local oMLX through hosted queued mapping, frozen original content/library/config, durable returned usage and three pending-only results; not quality evaluation')
            print('Private report:',output)
except Exception as error:
    report['diagnosticError']=type(error).__name__;save();raise
finally:
    if child is not None and child.poll() is None:child.terminate();child.wait(timeout=15)
    if created:subprocess.run(['dropdb','--force',name],env=env,check=True)
