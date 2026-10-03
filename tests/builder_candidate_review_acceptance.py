"""Owned, frozen candidate review sources, measurement examples and explicit decisions."""
import argparse,copy,json,secrets,socket,subprocess,tempfile,time,uuid,urllib.request
from pathlib import Path
import api_acceptance
from api_acceptance import Client
from persistence_environment import environment,dotnet

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--browser',action='store_true');args=parser.parse_args()
    root=Path(__file__).resolve().parents[1];database='learning_fault_'+uuid.uuid4().hex[:12];env=environment(database);sdk=dotnet(root);env['ConnectionStrings__Learning']=env['PERSISTENCE_TEST_CONNECTION'];env.pop('BackupConfigFile',None)
    subprocess.run(['createdb',database],env=env,check=True);service=None
    try:
        with tempfile.TemporaryDirectory(prefix='candidate-review-') as temp:
            work=Path(temp);env.update(DeletionLedger=str(work/'students'),FamilyDeletionLedger=str(work/'families'),ExportDirectory=str(work/'exports'))
            with socket.socket() as socket_:socket_.bind(('127.0.0.1',0));port=socket_.getsockname()[1]
            origin=f'http://127.0.0.1:{port}'
            with (work/'api.log').open('w+') as log:
                service=subprocess.Popen([sdk,str(root/'src/server/bin/Debug/net10.0/Learning.Api.dll'),'--urls',origin],cwd=root/'src/server',env=env,stdout=log,stderr=subprocess.STDOUT)
                deadline=time.monotonic()+40
                while time.monotonic()<deadline:
                    assert service.poll() is None,'Disposable service stopped'
                    try:
                        with urllib.request.urlopen(origin+'/api/health',timeout=1) as response:
                            if response.status==200:break
                    except OSError:time.sleep(.1)
                else:raise AssertionError('No service health')
                api_acceptance.BASE=origin+'/api/v1';user='candidate-review-'+uuid.uuid4().hex[:12];password=secrets.token_hex(24);c=Client();c.request('/auth/register',{'userName':user,'password':password},expected=201);c.request('/me');student=c.request('/students',{'name':'候选审核隔离学生'},expected=201)
                def publish(d):
                    c.request('/content/drafts/'+d['id']+':review',{});preview=c.request('/content/drafts/'+d['id']+'/preview');return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':preview['hash']})
                draft=c.request('/content/fixture',{});catalog=json.loads(draft['payload']);release=publish(draft)
                plain='先乘除后加减，独立计算乘加运算。\n一层小括号里先算加减，不含嵌套。\n独立列出两步应用问题的综合算式。\n检查是否按同级从左到右计算。\n遇到除法先算除法再做减法。'
                source=c.request('/content/sources',{'title':'原创候选审核来源','text':'\n'.join(line+' <img src=x onerror="window.__candidateInjected=true">' for line in plain.splitlines())},expected=201);run=c.request('/builder/runs',{'sourceId':source['id']},expected=202)
                for _ in range(100):
                    state=c.request('/builder');actual=next(r for r in state['runs'] if r['id']==run['id'])
                    if actual['status']=='Completed':break
                    assert actual['status']=='Queued',actual;time.sleep(.1)
                else:raise AssertionError('No candidate result')
                candidates=sorted([r for r in state['candidates'] if r['runId']==run['id']],key=lambda r:r['id']);assert len(candidates)==5
                def measured(q,kc):return q['policy']!='NoEvidence' and any(m['kcId']==kc and m['share']>0 and m['mode'] in ('WholeItem','StepObserved') and m['role'] not in ('Context','Prerequisite') for m in q['mappings'])
                original={}
                for row in candidates:
                    context=c.request('/builder/candidates/'+row['id']+'/review-context');original[row['id']]=context;assert context['candidate']==row and context['run']['libraryReleaseId']==release['id'] and context['libraryNumber']==release['number']
                    assert context['protocol']==json.loads(row['protocolPayload']) and all(f['sourceId']==source['id'] for f in context['source']['fragments'])
                    for match in context['matches']:
                        kc=next(k for k in catalog['kcs'] if k['id']==match['match']['kcId']);support=[q for q in catalog['questions'] if measured(q,kc['id'])];contrast=[q for q in catalog['questions'] if q['policy']!='NoEvidence' and not measured(q,kc['id'])]
                        assert match['definition']==kc and match['supportCount']==len(support) and match['supportQuestions']==support[:3] and match['contrastQuestions']==contrast[:2]
                updated=copy.deepcopy(catalog);updated['kcs'][0]['name']+=' · 后续名称';updated['kcs'][0]['revisionId']=str(uuid.uuid4());publish(c.request('/content/drafts',{'title':'后续正式定义说明','catalog':updated}))
                for row in candidates:assert c.request('/builder/candidates/'+row['id']+'/review-context')==original[row['id']]
                print('PASS 实际候选核对完整原片段/结构输出、固定正式定义及实际测量题；后续发布不替换原审核上下文，无测量题对照不冒充能力证据')
                row=candidates[0];path='/builder/candidates/'+row['id']+':decide';context=c.request('/builder/candidates/'+row['id']+'/review-context');saved_version=c.etag
                decision={'decision':'CreateDraft','reason':'明确核对来源与一层括号范围，仅接受为草稿','name':'一层括号原创能力','behavior':'独立计算一层括号内加减，再做除法','boundary':'不含嵌套或应用建模'}
                c.request(path,{**decision,'reason':' '},expected=422);c.request(path,{**decision,'reason':'x'*4001},expected=422);assert c.request('/builder/candidates/'+row['id']+'/review-context')==context
                c.request('/students',{'name':'另一页的实际写入'},expected=201);c.etag=saved_version;c.request(path,decision,expected=412);assert c.request('/builder/candidates/'+row['id']+'/review-context')==context
                accepted=c.request(path,decision);assert accepted['reviewReason']==decision['reason'] and accepted['name']==row['name'] and accepted['behavior']==row['behavior'] and accepted['status']=='Accepted'
                c.request(path,decision,expected=409);rejected=c.request('/builder/candidates/'+candidates[1]['id']+':decide',{'decision':'Reject','reason':'来源只给顺序名称，尚不足以定义独立测量边界'});assert rejected['reviewReason'].startswith('来源')
                target=original[candidates[2]['id']]['matches'][0]['match']['kcId'];linked=c.request('/builder/candidates/'+candidates[2]['id']+':decide',{'decision':'LinkExisting','reason':'逐项核对固定定义与测量题后关联同一稳定能力','existingKCId':target});assert linked['existingKCId']==target
                provenance=c.request('/content/kcs/'+accepted['createdKCId']+'/provenance');assert provenance['citations'][0]['review']['reviewReason']==decision['reason']
                print('PASS 空白/超长依据422、真实另一页写入后旧版本412且原候选不变；显式新建/拒绝/关联保存实际理由，原输出与来源保留')
                if args.browser:
                    browser_env={**env,'LEARNING_TEST_URL':origin,'CANDIDATE_REVIEW_USER':user,'CANDIDATE_REVIEW_PASSWORD':password,'CANDIDATE_REVIEW_ID':candidates[3]['id'],'CANDIDATE_REVIEW_SECOND':candidates[4]['id']}
                    subprocess.run(['node',str(root/'src/web/node_modules/@playwright/test/cli.js'),'test','--config',str(root/'src/web/playwright.config.ts'),str(root/'src/web/tests/builder-candidate-review.spec.ts'),'--workers=1'],cwd=root/'src/web',env=browser_env,check=True)
                # Controlled damaged-modern and legacy-with-unknown-protocol fixtures, never reported as historical upgrades.
                cid=candidates[4]['id'];rid=run['id']
                def sql(query):return subprocess.check_output(['psql','-Atc',query],env=env,text=True).strip()
                original_payload=sql(f'SELECT "ProtocolPayload" FROM "Candidates" WHERE "Id"=\'{cid}\';')
                sql(f'UPDATE "Candidates" SET "ProtocolPayload"=NULL WHERE "Id"=\'{cid}\';')
                assert c.request('/builder/candidates/'+cid+'/review-context',expected=422)['code']=='CANDIDATE_PROTOCOL_UNKNOWN'
                sql(f'UPDATE "BuilderRuns" SET "InputVersion"=\'builder-input/2\' WHERE "Id"=\'{rid}\';')
                legacy=c.request('/builder/candidates/'+cid+'/review-context');assert legacy['protocol'] is None and '不补造' in legacy['notice'] and legacy['source']['fragments']
                assert sql(f'SELECT "ProtocolPayload" IS NULL FROM "Candidates" WHERE "Id"=\'{cid}\';')=='t'
                sql(f'UPDATE "BuilderRuns" SET "InputVersion"=\'builder-input/3\' WHERE "Id"=\'{rid}\';')
                payload=json.loads(original_payload);payload['supportingQuotes'][0]=None;encoded=json.dumps(payload,ensure_ascii=False).replace("'","''")
                sql(f'UPDATE "Candidates" SET "ProtocolPayload"=\'{encoded}\' WHERE "Id"=\'{cid}\';')
                assert c.request('/builder/candidates/'+cid+'/review-context',expected=422)['code']=='BUILDER_SOURCE_INVALID'
                encoded=original_payload.replace("'","''");sql(f'UPDATE "Candidates" SET "ProtocolPayload"=\'{encoded}\' WHERE "Id"=\'{cid}\';')
                print('PASS 受控现代缺失输出或无效引文422；旧版本未知结构输出保持NULL、只显示实际保留片段，不补造或回填')
                outsider=Client();outsider.request('/auth/register',{'userName':'outside-candidate-'+uuid.uuid4().hex[:10],'password':secrets.token_hex(24)},expected=201);outsider.request('/me');outsider.request('/builder/candidates/'+row['id']+'/review-context',expected=404)
                c.request('/me');c.request('/students/'+student['id']+'/child-sessions',{});c.request('/builder/candidates/'+row['id']+'/review-context',expected=403)
                print('PASS 候选支持题及参考答案仅供有内容维护权限的本家庭读取；另一家庭404、孩子403')
    finally:
        if service is not None and service.poll() is None:service.terminate();service.wait(timeout=10)
        subprocess.run(['dropdb','--force',database],env=env,check=True)
if __name__=='__main__':main()
