"""Actual review endpoints over explicitly controlled provider outputs from real queued processing."""
import json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);password=secrets.token_hex(24);env.update(V2_USER='controlled-v2-'+uuid.uuid4().hex[:12],V2_PASSWORD=password,ConnectionStrings__Learning=env['PERSISTENCE_TEST_CONNECTION']);env.pop('BackupConfigFile',None);service=None
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        seeded=subprocess.check_output([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'builder-v2-seed'],env=env,text=True);fixture=json.loads(seeded.splitlines()[-1])
        with tempfile.TemporaryDirectory(prefix='builder-v2-api-') as temp:
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
                api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/login',{'userName':fixture['userName'],'password':password})
                for row in fixture['candidates']:
                    context=c.request('/builder/candidates/'+row['id']+'/review-context');assert context['run']['promptVersion']=='kc-candidate/2' and context['protocol']['kcType']==row['type'] and all(m['definition']['type']==row['type'] for m in context['matches'])
                    if row['type']=='Strategy':
                        accepted=c.request('/builder/candidates/'+row['id']+':decide',{'decision':'CreateDraft','reason':'明确受控结构流程核对，不作为模型语义质量结论','name':'受控策略能力草稿','behavior':'独立选择运算顺序','boundary':'不含建模'})
                        draft=next(d for d in c.request('/content')['drafts'] if d['id']==accepted['createdDraftId']);kc=json.loads(draft['payload'])['kcs'][0];assert kc['type']=='Strategy' and kc['subject']=='MATH' and kc['gradeMin']==3 and kc['gradeMax']==3 and draft['status']=='Draft'
                    else:
                        rejected=c.request('/builder/candidates/'+row['id']+':decide',{'decision':'LinkExisting','reason':'受控表达候选不可关联策略能力','existingKCId':next(r['target'] for r in fixture['candidates'] if r['type']=='Strategy')},expected=422);assert rejected['code']=='KC_SUBJECT_OR_TYPE_CONFLICT';assert c.request('/builder/candidates/'+row['id']+'/review-context')['candidate']['status']=='Pending'
                        linked=c.request('/builder/candidates/'+row['id']+':decide',{'decision':'LinkExisting','reason':'核对原固定表达定义及支持测量题后关联','existingKCId':row['target']});assert linked['existingKCId']==row['target']
                calls=c.request('/builder/calls?runId='+fixture['runId']);assert calls['total']==1 and calls['calls'][0]['billingStatus']=='LocalNoCharge'
                other=Client();other.request('/auth/register',{'userName':'outside-v2-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);other.request('/builder/candidates/'+fixture['candidates'][0]['id']+'/review-context',expected=404)
                c.request('/me');student=c.request('/students',{'name':'受控新协议导出'},expected=201);export=c.request('/students/'+student['id']+'/export');assert export['evidence']==[] and export['mastery']==[];c.request('/students/'+student['id']+'/child-sessions',{});c.request('/builder/candidates/'+fixture['candidates'][0]['id']+'/review-context',expected=403)
                print('PASS actual v2 review endpoints over controlled queued provider results: Strategy draft retains type/metadata; Expression rejects Strategy target then links matching type; calls, family404, child403 and zero evidence checked; not model-quality evidence')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
