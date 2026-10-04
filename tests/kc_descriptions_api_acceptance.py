"""Actual reviewed KC descriptor revisions, frozen retrieval, immutable history and family isolation."""
import copy,json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);env['ConnectionStrings__Learning']=env['PERSISTENCE_TEST_CONNECTION'];env.pop('BackupConfigFile',None);service=None
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        with tempfile.TemporaryDirectory(prefix='kc-descriptions-') as temp:
            work=Path(temp);env.update(DeletionLedger=str(work/'students'),FamilyDeletionLedger=str(work/'families'),ExportDirectory=str(work/'exports'))
            with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
            origin='http://127.0.0.1:'+str(port)
            with (work/'api.log').open('w') as log:
                service=subprocess.Popen([dotnet(root),str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT)
                end=time.monotonic()+40
                while time.monotonic()<end:
                    assert service.poll() is None
                    try:
                        with urllib.request.urlopen(origin+'/api/health',timeout=1) as r:
                            if r.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('No API health')
                api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/register',{'userName':'descriptions-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);c.request('/me')
                def publish(d,expected=200):
                    c.request('/content/drafts/'+d['id']+':review',{});preview=c.request('/content/drafts/'+d['id']+'/preview');return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':preview['hash']},expected=expected)
                first=c.request('/content/fixture',{});original=json.loads(first['payload']);release=publish(first);saved=c.request('/content');snapshot=next(r for r in saved['releases'] if r['id']==release['id'])['payload']
                modern=copy.deepcopy(original)
                for k in modern['kcs']:k.update(revisionId=str(uuid.uuid4()),domain='数与运算',difficultyLevel='Hard',cognitiveLevel='理解与应用')
                second=publish(c.request('/content/drafts',{'title':'独立能力描述审核','catalog':modern}));saved=c.request('/content');actual=json.loads(next(r for r in saved['releases'] if r['id']==second['id'])['payload']);assert actual==modern and next(r for r in saved['releases'] if r['id']==release['id'])['payload']==snapshot
                assert actual['questions']==original['questions'] and actual['kcs'][0]['id']==original['kcs'][0]['id'] and actual['kcs'][0]['revisionId']!=original['kcs'][0]['revisionId']
                print('PASS actual descriptor draft/review/publish; independent revision and original question difficulty/history preserved',flush=True)
                overwritten=copy.deepcopy(modern);overwritten['kcs'][0]['domain']='覆盖旧修订';error=publish(c.request('/content/drafts',{'title':'受控旧修订覆盖拒绝','catalog':overwritten}),expected=422);assert error['code']=='REVISION_IMMUTABLE'
                for field,value in [('domain',' '),('domain','x'*201),('cognitiveLevel','x'*101),('cognitiveLevel','理解\n应用'),('difficultyLevel','hard')]:
                    bad=copy.deepcopy(modern);bad['kcs'][0].update(revisionId=str(uuid.uuid4()));bad['kcs'][0][field]=value;draft=c.request('/content/drafts',{'title':'受控非法描述','catalog':bad});error=c.request('/content/drafts/'+draft['id']+':review',{},expected=422);assert error['code']=='CONTENT_INVALID' and '领域' in error['title'];assert c.request('/content/drafts/'+draft['id']+'/reviews')==[]
                print('PASS exact old revision overwrite refused; invalid descriptors block review with no fake review record',flush=True)
                source=c.request('/content/sources',{'title':'原创描述固定核对','text':'独立计算混合运算，先计算乘除，再进行加减。'},expected=201);run=c.request('/builder/runs',{'sourceId':source['id']},expected=202)
                for _ in range(100):
                    builder=c.request('/builder');result=next(r for r in builder['runs'] if r['id']==run['id'])
                    if result['status']=='Completed':break
                    assert result['status']=='Queued';time.sleep(.1)
                else:raise AssertionError('Builder not complete')
                row=next(r for r in builder['candidates'] if r['runId']==run['id']);frozen=c.request('/builder/candidates/'+row['id']+'/review-context');assert frozen['libraryNumber']==second['number'] and frozen['matches'] and all(m['definition']['domain']=='数与运算' for m in frozen['matches'])
                later=copy.deepcopy(modern)
                for k in later['kcs']:k.update(revisionId=str(uuid.uuid4()),domain='后续描述',difficultyLevel='Easy',cognitiveLevel='独立操作')
                publish(c.request('/content/drafts',{'title':'后续描述修订','catalog':later}));assert c.request('/builder/candidates/'+row['id']+'/review-context')==frozen
                other=Client();other.request('/auth/register',{'userName':'outside-descriptions-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);other.request('/content/kcs/'+modern['kcs'][0]['id']+'/provenance',expected=404)
                c.request('/me');student=c.request('/students',{'name':'描述权限隔离学生'},expected=201);export=c.request('/students/'+student['id']+'/export');assert json.loads(next(r for r in export['releases'] if r['id']==second['id'])['payload'])==modern;assert next(r for r in export['releases'] if r['id']==release['id'])['payload']==snapshot;c.request('/students/'+student['id']+'/child-sessions',{});c.request('/builder/candidates/'+row['id']+'/review-context',expected=403)
                print('PASS later descriptor release cannot replace frozen actual matches; export preserves actual new/original content, other family404 and child403',flush=True)
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
