import base64
import copy
import json
import io,zipfile
import api_acceptance
import time
import uuid
from api_acceptance import Client,TODAY

def pdf(text):
    stream=f'BT /F1 12 Tf 40 100 Td ({text}) Tj ET'.encode()
    objects=[b'<< /Type /Catalog /Pages 2 0 R >>',b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',f'<< /Length {len(stream)} >>\nstream\n'.encode()+stream+b'\nendstream']
    data=b'%PDF-1.4\n';offsets=[0]
    for i,obj in enumerate(objects,1):offsets.append(len(data));data+=f'{i} 0 obj\n'.encode()+obj+b'\nendobj\n'
    xref=len(data);data+=b'xref\n0 6\n0000000000 65535 f \n'+b''.join(f'{n:010} 00000 n \n'.encode() for n in offsets[1:])+f'trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF'.encode()
    return data

def main():
    c=Client();c.request('/auth/register',{'userName':'advanced-'+uuid.uuid4().hex[:12],'password':'advanced-private-'+uuid.uuid4().hex},expected=201);c.request('/me')
    s=c.request('/students',{'name':'高级验收'},expected=201);sid=s['id']
    d=c.request('/content/fixture',{});catalog=json.loads(d['payload'])
    def publish(draft):
        c.request('/content/drafts/'+draft['id']+':review',{});p=c.request('/content/drafts/'+draft['id']+'/preview');return c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':p['hash']})
    r1=publish(d);c.request('/students/'+sid+'/content/'+r1['id']+':bind',{})
    c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    rev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});v=c.request('/students/'+sid+'/plans/'+TODAY);t=v['tasks'][0]
    c.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash']});c.request('/tasks/'+t['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+t['id']+'/sessions',{})
    changed=copy.deepcopy(catalog);q=next(q for q in changed['questions'] if q['id']==t['questionId']);oldkc=q['mappings'][0]['kcId'];newkc=next(k['id'] for k in changed['kcs'] if k['id']!=oldkc);q['mappings'][0]['kcId']=newkc;q['revisionId']=str(uuid.uuid4())
    r2=publish(c.request('/content/drafts',{'title':'审核后的映射更正','catalog':changed}));c.request('/students/'+sid+'/content/'+r2['id']+':bind',{})
    a=c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'999'},expected=201)
    assert a['attempt']['sessionId']==session['sessionId']
    q1=next(q for q in catalog['questions'] if q['id']==t['questionId'])
    binding1=next(b for b in c.request('/content/releases/'+r1['id']+'/mapping-sets')['bindings'] if b['ownerType']=='Question' and b['ownerId']==q1['id'])
    assert a['attempt']['questionRevisionId']==q1['revisionId'] and a['attempt']['mappingSetRevisionId']==binding1['setRevisionId']
    original_attempt=a['attempt'].copy()
    def wait():
        for _ in range(50):
            m=c.request('/students/'+sid+'/mastery')
            if m['pending']==0:return m
            time.sleep(.1)
        raise AssertionError('projection did not settle')
    m=wait();assert m['masteries'][0]['kcId']==oldkc
    assert c.request('/students/'+sid+'/mastery/'+oldkc)['evidence'][0]['mappingSetRevisionId']==binding1['setRevisionId']
    initial_context=c.request('/students/'+sid+'/assessment-contexts');ctx1=initial_context['contexts'][0]
    assert ctx1['mappingSetRevisionId']==binding1['setRevisionId'] and ctx1['gradingRevisionId']==a['grading']['id'] and ctx1['evidenceRuleVersion']=='evidence/1.1' and ctx1['activationStatus']=='Active'
    assert ctx1['mappingSource']=='FixedContainer' and ctx1['questionRevisionId']==q1['revisionId'] and ctx1['contentReleaseId']==r1['id'] and ctx1['mappingReleaseId']==r1['id']
    assert c.request('/students/'+sid+'/mastery/'+oldkc)['evidence'][0]['contextId']==ctx1['id']
    print('PASS AT09 切换发布版本后，当前会话仍按领取内容归因')
    correction={'releaseId':r2['id'],'attemptIds':[a['attempt']['id']],'reason':'人工确认原映射错误'}
    p=c.request('/students/'+sid+'/mapping-corrections:preview',correction)
    c.request('/students/'+sid+'/mapping-corrections:confirm',{**correction,'previewHash':p['previewHash']},expected=202)
    m=wait();assert len(m['masteries'])==1 and m['masteries'][0]['kcId']==newkc and m['masteries'][0]['beta']==3
    detail=c.request('/students/'+sid+'/mastery/'+newkc);assert detail['evidence'][0]['mappingReleaseId']==r2['id'] and detail['evidence'][0]['correctionBatchId']
    binding2=next(b for b in c.request('/content/releases/'+r2['id']+'/mapping-sets')['bindings'] if b['ownerType']=='Question' and b['ownerId']==q1['id'])
    assert detail['evidence'][0]['mappingSetRevisionId']==binding2['setRevisionId'] and binding2['setRevisionId']!=binding1['setRevisionId']
    assert next(x for x in c.request('/students/'+sid+'/attempts')['attempts'] if x['id']==original_attempt['id'])==original_attempt
    with c.http.open(api_acceptance.BASE+'/family/export') as response:
        data=json.loads(zipfile.ZipFile(io.BytesIO(response.read())).read('manifest.json'))['data']
    stored_session=next(s for s in data['LearningSession'] if s['id']==session['sessionId']);correction_row=next(x for x in data['CorrectionItem'] if x['attemptId']==original_attempt['id'])
    assert stored_session['mappingSetRevisionId']==binding1['setRevisionId'] and stored_session['questionRevisionId']==q1['revisionId']
    assert correction_row['mappingSetRevisionId']==binding2['setRevisionId'] and correction_row['questionRevisionId']==q['revisionId']
    export=c.request('/students/'+sid+'/export');assert {binding1['setRevisionId'],binding2['setRevisionId']}.issubset({x['id'] for x in export['mappingSets']})
    assert export['sessions'][0]['mappingSetRevisionId']==binding1['setRevisionId'] and {binding1['setRevisionId'],binding2['setRevisionId']}.issubset({x['setRevisionId'] for x in export['mappingBindings']})
    current=c.request('/students/'+sid+'/assessment-contexts');ctx2=current['contexts'][0]
    assert ctx2['id']!=ctx1['id'] and ctx2['mappingSetRevisionId']==binding2['setRevisionId'] and ctx2['correctionBatchId']==correction_row['batchId'] and ctx2['gradingRevisionId']==ctx1['gradingRevisionId']
    assert ctx2['activationStatus']=='Active' and detail['evidence'][0]['contextId']==ctx2['id']
    retired=c.request('/students/'+sid+'/assessment-contexts?generation='+initial_context['generation']['id']);assert retired['contexts'][0]=={**ctx1,'activationStatus':'Retired'} and retired['generation']['status']=='Retired'
    assert {ctx1['id'],ctx2['id']}.issubset({x['id'] for x in export['assessmentContexts']}) and {ctx1['id'],ctx2['id']}.issubset({x['id'] for x in data['AssessmentContext']})
    # Explicit repeated rebuild reuses the fixed generation and context IDs.
    c.request('/students/'+sid+':rebuild',{})
    assert wait()['generation']==current['generation']['id'] and c.request('/students/'+sid+'/assessment-contexts')['contexts']==current['contexts']
    c.request('/students/'+sid+'/assessment-contexts?offset=-1',expected=422);c.request('/students/'+sid+'/assessment-contexts?generation='+str(uuid.uuid4()),expected=404)
    print('PASS AT10 映射更正新世代重放，旧 KC 不双计；上下文固定新旧归因、退休状态、导出和重复重建不双计')
    for text,status in [('First multiply then add.','Completed'),('','NeedsOCR')]:
        f=c.request('/files',{'name':'fixture.pdf','mimeType':'application/pdf','base64':base64.b64encode(pdf(text)).decode()},expected=201)
        job=c.request('/content/pdf-sources',{'fileId':f['id'],'title':'PDF 验收'},expected=202)['job']
        for _ in range(50):
            info=c.request('/builder');result=next(r for r in info['runs'] if r['id']==job['id'])
            if result['status']!='Queued':break
            time.sleep(.1)
        assert result['status']==status,result
        if text:assert any(ch['locator']=='PDF 第 1 页' and 'multiply' in ch['text'] for ch in info['chunks'])
        print('PASS 本地 PDF 解析 '+status)
    source=c.request('/content/sources',{'title':'相似检索验收','text':'先乘除后加减，独立确定运算顺序。'},expected=201)
    run=c.request('/builder/runs',{'sourceId':source['id']},expected=202)
    for _ in range(50):
        info=c.request('/builder');candidates=[x for x in info['candidates'] if x['runId']==run['id']]
        if candidates:break
        time.sleep(.1)
    candidate=candidates[0];matches=json.loads(candidate['matches']);assert len(matches)>0
    c.request('/builder/candidates/'+candidate['id']+':decide',{'decision':'LinkExisting','existingKCId':matches[0]['kcId'],'name':candidate['name'],'reason':'人工确认现有能力'})
    print('PASS 模拟检索 → 人工关联已有 KC → Alias，未自动发布')

if __name__=='__main__':main()
