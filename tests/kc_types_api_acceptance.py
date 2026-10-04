"""Actual classification publication and immutable measurement identities."""
import copy,json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);env['ConnectionStrings__Learning']=env['PERSISTENCE_TEST_CONNECTION'];env.pop('BackupConfigFile',None);service=None
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        with tempfile.TemporaryDirectory(prefix='kc-types-') as temp:
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
                first=c.request('/content/fixture',{});original=json.loads(first['payload']);release=publish(first)
                types=['Concept','Procedure','Representation','Strategy','Application','Expression','Misconception'];published=[]
                for kind in types:
                    catalog=copy.deepcopy(original);identities={k['id']:str(uuid.uuid4()) for k in catalog['kcs']}
                    for k in catalog['kcs']:k.update(id=identities[k['id']],revisionId=str(uuid.uuid4()),code='CONTROLLED.'+kind+'.'+uuid.uuid4().hex,type=kind)
                    for q in catalog['questions']:
                        q.update(id=str(uuid.uuid4()),revisionId=str(uuid.uuid4()))
                        for m in q['mappings']:m['kcId']=identities[m['kcId']]
                    for owner in catalog['resources']+catalog['lessons']:
                        owner.update(id=str(uuid.uuid4()))
                        if 'revisionId' in owner:owner['revisionId']=str(uuid.uuid4())
                        owner['kcIds']=[identities[k] for k in owner['kcIds']]
                    for edge in catalog['relations']:edge['from']=identities[edge['from']];edge['to']=identities[edge['to']]
                    published.append((publish(c.request('/content/drafts',{'title':'受控类型 '+kind,'catalog':catalog})),catalog))
                saved=c.request('/content');assert all(json.loads(next(r for r in saved['releases'] if r['id']==release_['id'])['payload'])==catalog for release_,catalog in published)
                print('PASS actual reviewed publication of all six design classifications and legacy Misconception with independent identities',flush=True)
                for kind in ['Strategy','Expression']:
                    changed=copy.deepcopy(original)
                    for k in changed['kcs']:k.update(revisionId=str(uuid.uuid4()),type=kind)
                    error=publish(c.request('/content/drafts',{'title':'受控类型改义拒绝','catalog':changed}),expected=422);assert error['code']=='MEASUREMENT_IDENTITY_CHANGED'
                invalid=copy.deepcopy(original);invalid['kcs'][0]['type']='strategy';draft=c.request('/content/drafts',{'title':'受控非法类型','catalog':invalid});error=c.request('/content/drafts/'+draft['id']+':review',{},expected=422);assert error['code']=='CONTENT_INVALID';assert c.request('/content/drafts/'+draft['id']+'/reviews')==[]
                c.request('/me');student=c.request('/students',{'name':'类型导出核对'},expected=201);export=c.request('/students/'+student['id']+'/export');assert all(json.loads(next(r for r in export['releases'] if r['id']==release_['id'])['payload'])==catalog for release_,catalog in published);assert export['evidence']==[] and export['mastery']==[]
                print('PASS published type changes require new measurement identities; invalid type no review, export preserves exact classifications and zero copied evidence',flush=True)
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
