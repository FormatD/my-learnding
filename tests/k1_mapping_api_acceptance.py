#!/usr/bin/env python3
"""Normalize actual queued Mock outputs and actual review decisions in a disposable DB."""
import copy,json,os,secrets,socket,subprocess,sys,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'scripts'))
import k1_evaluation as e
import k1_mapping_results as adapter
DATA=json.loads((ROOT/'docs/evaluation/mixed-operations-draft-v1.json').read_text());ITEMS=[i for i in DATA['items'] if i['partition']=='Holdout']
def main():
    database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);env['ConnectionStrings__Learning']=env['PERSISTENCE_TEST_CONNECTION'];env.pop('BackupConfigFile',None);service=None
    subprocess.run(['createdb',database],env=env,check=True)
    try:
        with tempfile.TemporaryDirectory(prefix='k1-mapping-') as directory:
            work=Path(directory);env.update(DeletionLedger=str(work/'students'),FamilyDeletionLedger=str(work/'families'),ExportDirectory=str(work/'exports'))
            with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
            origin=f'http://127.0.0.1:{port}'
            with (work/'api.log').open('w+') as log:
                service=subprocess.Popen([dotnet(ROOT),str(ROOT/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=ROOT/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT);deadline=time.monotonic()+40
                while time.monotonic()<deadline:
                    assert service.poll() is None,'Disposable service stopped'
                    try:
                        with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('Service not healthy')
                api_acceptance.BASE=origin+'/api/v1';c=Client();c.request('/auth/register',{'userName':'k1-mapping-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);c.request('/me');student=c.request('/students',{'name':'受控评测隔离测试'},expected=201);draft=c.request('/content/unit-pack',{})
                # Explicit isolated test publication, not real textbook approval or gold labels.
                c.request('/content/drafts/'+draft['id']+':review',{'expectedDraftVersion':draft['version'],'reason':'受控评测导出接口测试，不作为正式教材或金标准审核'});preview=c.request('/content/drafts/'+draft['id']+'/preview');release=c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':preview['hash']})
                request={'draftId':draft['id'],'libraryReleaseId':release['id'],'owners':[{'ownerType':'Question','ownerId':i['id'],'ownerRevisionId':i['revisionId']} for i in ITEMS],'provider':'Mock'}
                prepared=c.request('/builder/mapping-preparations',request,expected=202)
                for _ in range(150):
                    state=c.request('/builder/mapping-preparations/'+prepared['id'])
                    if state['status']=='Succeeded':break
                    assert state['status'] in ('Queued','Running'),state;time.sleep(.1)
                else:raise AssertionError('Queued mappings not complete')
                detail=c.request('/builder/mapping-runs/'+state['runId']);original=adapter.normalize(DATA,detail);assert len(original['predictions'])==56
                before=e.digest(original['predictions']);assert all(r['status']=='Mapped' for r in original['predictions']);assert all(r['provenance']['reviewStatus']=='Pending' for r in original['predictions'])
                options=[s for s in detail['suggestions'] if s['evidencePolicy']=='SingleKC'];chosen=options[0];other=options[1];proposal={'evidencePolicy':chosen['evidencePolicy'],'items':json.loads(chosen['suggestedItems'])};newkc=next(k for k in detail['library'] if k['id']!=proposal['items'][0]['kcId']);proposal['items'][0].update(kcId=newkc['id'],kcRevisionId=newkc['revisionId'])
                c.request('/builder/mapping-runs/'+state['runId']+'/suggestions:decide',{'decisions':[{'suggestionId':chosen['id'],'decision':'Accept','reason':'受控测试只验证实际校正与原结果分离','correctedProposal':proposal},{'suggestionId':other['id'],'decision':'Reject','reason':'受控测试拒绝，无正式内容质量判断'}]})
                reviewed_detail=c.request('/builder/mapping-runs/'+state['runId']);after=adapter.normalize(DATA,reviewed_detail);reviewed=adapter.normalize(DATA,reviewed_detail,stage='ReviewedMapping');byid={r['id']:r for r in reviewed['predictions']}
                assert byid[chosen['ownerId']]['primaryKCIds']==[newkc['id']] and byid[other['ownerId']]['status']=='Rejected' and sum(r['status']=='Skipped' for r in reviewed['predictions'])==54
                assert [{k:r[k] for k in ('id','inputHash','status','primaryKCIds','mappedKCIds')} for r in original['predictions']]==[{k:r[k] for k in ('id','inputHash','status','primaryKCIds','mappedKCIds')} for r in after['predictions']]
                assert before==e.digest(original['predictions']) and reviewed['capture']['detailHash']==e.digest(reviewed_detail)
                saved=work/'detail.json';saved.write_text(json.dumps(reviewed_detail));output=work/'normalized.json';subprocess.run(['python3',str(ROOT/'scripts/k1_mapping_results.py'),'--dataset',str(ROOT/'docs/evaluation/mixed-operations-draft-v1.json'),'--detail',str(saved),'--stage','ReviewedMapping','--output',str(output)],check=True,capture_output=True);assert json.loads(output.read_text())==reviewed
                report=e.evaluate(DATA,json.loads((ROOT/'docs/evaluation/mixed-operations-labels-template-v1.json').read_text()),reviewed);assert report['pendingItems']==56 and report['qualityGate']['status']=='NotEvaluated' and report['primaryAccuracy']['rate'] is None and report['reviewMinutesPer100'] is None and not report['formalV1ExitProven']
                export=c.request('/students/'+student['id']+'/export');assert export['evidence']==[] and export['mastery']==[]
                c.request('/students/'+student['id']+'/child-sessions',{});c.request('/builder/mapping-runs/'+state['runId'],expected=403)
                other_family=Client();other_family.request('/auth/register',{'userName':'k1-other-'+uuid.uuid4().hex[:12],'password':secrets.token_hex(24)},expected=201);other_family.request('/me');other_family.request('/builder/mapping-runs/'+state['runId'],expected=404)
                print('PASS actual queued 56-item Mock mapping API output matches frozen inputs; original/corrected/rejected/pending records remain separate')
                print('PASS actual review payload hashes and CLI normalization; Pending gold remains NotEvaluated, no inferred timing/schema or formal quality; family boundary 404 and child 403, no evidence/mastery')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
