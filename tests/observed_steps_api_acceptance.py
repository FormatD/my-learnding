"""Observed-step authoring, frozen manual task selection and effective grading context."""
import json,time,uuid,copy
from api_acceptance import Client,TODAY

def main():
    c=Client();other=Client()
    for client in [c,other]:client.request('/auth/register',{'userName':'steps-'+uuid.uuid4().hex[:12],'password':'steps-private-'+uuid.uuid4().hex},expected=201);client.request('/me')
    sid=c.request('/students',{'name':'步骤观察验收'},expected=201)['id']
    fixture=c.request('/content/fixture',{});cat=json.loads(fixture['payload']);cat['kcs']=cat['kcs'][:2];a,b=[k['id'] for k in cat['kcs']];qid=str(uuid.uuid4())
    cat['questions']=[{'id':qid,'revisionId':str(uuid.uuid4()),'stem':'先列式，再计算：买3支笔，每支4元，还剩8元，原来有多少钱？','answer':'20元','explanation':'3×4+8=20，先乘后加。','type':'MultiStep','difficulty':'Medium','policy':'ObservedSteps','mappings':[{'kcId':a,'role':'Primary','share':.5,'mode':'StepObserved','step':'列式'},{'kcId':b,'role':'Secondary','share':.5,'mode':'StepObserved','step':'计算'}]}];cat['resources']=[];cat['lessons']=[{'id':str(uuid.uuid4()),'title':'步骤观察','sequence':1,'kcIds':[a,b]}];cat['relations']=[]
    def publish(catalog):
        d=c.request('/content/drafts',{'title':'步骤测量','catalog':catalog});c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview');return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']})
    r1=publish(cat);c.request('/students/'+sid+'/content/'+r1['id']+':bind',{})
    progress_path='/students/'+sid+'/school-progress/'+TODAY+'/'+cat['lessons'][0]['id']
    c.request(progress_path,{},method='PUT');before=c.request('/domain-events?studentId='+sid)['total'];c.request(progress_path,{},method='PUT');assert c.request('/domain-events?studentId='+sid)['total']==before
    other.request('/domain-events?studentId='+sid,expected=404);assert not other.request('/domain-events')['events']
    rev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});assert c.request('/plans/'+rev['id']+'/questions')[0]['id']==qid
    other.request('/plans/'+rev['id']+'/questions',expected=404);other.request('/students/'+sid+'/assessment-contexts',expected=404);other.request('/students/'+sid+'/evidence-revocations',expected=404)
    inp={'title':'家长观察两步过程','minutes':5,'resourceRef':'在纸上列式与计算，家长观察后确认','type':'Practice','questionId':qid}
    c.request('/plans/'+rev['id']+'/tasks',{**inp,'questionId':str(uuid.uuid4())},expected=422)
    task=c.request('/plans/'+rev['id']+'/tasks',inp);assert task['questionId']==qid and task['releaseId']==r1['id'] and task['locked']
    v=c.request('/students/'+sid+'/plans/'+TODAY);c.request('/plans/'+rev['id']+':publish',{'previewHash':v['revision']['inputHash'],'confirmWarnings':True})
    c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+task['id']+'/sessions',{});attempt=c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'3×4+8'},expected=201)['attempt'];aid=attempt['id']
    context=c.request('/attempts/'+aid+'/grading-context');assert context['question']['stem']==cat['questions'][0]['stem'] and context['grade']['result']=='Pending' and len(context['observations'])==2
    other.request('/attempts/'+aid+'/grading-context',expected=404)
    print('PASS 固定计划版本的正式多步骤题可安排、作答、读取判分上下文；跨家庭404')
    grade={'result':'Partial','reason':'纸上列式正确，计算没有写，不能推断','steps':[{'step':'列式','result':'Correct','hintLevel':1},{'step':'计算','result':'Unknown','hintLevel':0}]}
    c.request('/attempts/'+aid+'/grading-preview',{**grade,'steps':[{'step':'不存在','result':'Correct'}]},expected=422)
    c.request('/attempts/'+aid+'/grading-revisions',{**grade,'previewHash':'stale'},expected=412)
    p=c.request('/attempts/'+aid+'/grading-preview',grade);c.request('/attempts/'+aid+'/grading-revisions',{**grade,'previewHash':p['previewHash']},expected=202)
    def settled():
        for _ in range(100):
            m=c.request('/students/'+sid+'/mastery')
            if m['pending']==0:return m
            time.sleep(.1)
        raise AssertionError('projection did not settle')
    settled();e=c.request('/students/'+sid+'/mastery/'+a)['evidence'];assert len(e)==1 and e[0]['weight']==.35
    assert c.request('/students/'+sid+'/mastery/'+b)['evidence']==[]
    print('PASS 已知步骤产生证据，未知步骤不产生正负证据，预览拒绝不存在的观察点')
    changed=copy.deepcopy(cat);changed['questions'][0]['revisionId']=str(uuid.uuid4());changed['questions'][0]['mappings'][0]['step']='列出算式';r2=publish(changed)
    correction={'releaseId':r2['id'],'attemptIds':[aid],'reason':'明确列式观察点名称'};p=c.request('/students/'+sid+'/mapping-corrections:preview',correction);c.request('/students/'+sid+'/mapping-corrections:confirm',{**correction,'previewHash':p['previewHash']},expected=202);settled()
    context=c.request('/attempts/'+aid+'/grading-context');assert context['mappingReleaseId']==r2['id'] and context['observations'][0]['step']=='列出算式'
    c.request('/attempts/'+aid+'/grading-preview',grade,expected=422)
    grade['steps'][0]['step']='列出算式';p=c.request('/attempts/'+aid+'/grading-preview',grade);c.request('/attempts/'+aid+'/grading-revisions',{**grade,'previewHash':p['previewHash']},expected=202);settled();assert len(c.request('/students/'+sid+'/mastery/'+a)['evidence'])==1
    print('PASS 历史映射更正后使用有效观察点判分，原始作答保持不变且证据不双计')
    direct_source=c.request('/content/drafts',{'title':'独立映射权限验收','catalog':json.loads(r2['payload'])});q=json.loads(r2['payload'])['questions'][0];kcs=json.loads(r2['payload'])['kcs']
    direct={'sourceDraftId':direct_source['id'],'expectedDraftVersion':direct_source['version'],'libraryReleaseId':r2['id'],'ownerType':'Question','ownerId':q['id'],'ownerRevisionId':q['revisionId'],'evidencePolicy':q['policy'],'reason':'核对家庭权限','items':[{'kcId':m['kcId'],'kcRevisionId':next(k['revisionId'] for k in kcs if k['id']==m['kcId']),'role':m['role'],'coverageWeight':1,'evidenceShare':m['share'],'evidenceMode':m['mode'],'step':m['step'],'sequence':i+1,'modelScore':None,'sourceRefs':[f"draft:{direct_source['id']}/Question:{q['revisionId']}"]} for i,m in enumerate(q['mappings'])]}
    direct_saved=c.request('/content/mapping-sets',direct,expected=201);other.request('/content/mapping-sets/'+direct_saved['set']['id'],expected=404);other.request('/content/mapping-sets',direct,expected=404);assert not other.request('/content/mapping-sets')['sets'];assert not other.request('/builder/calls')['calls']
    source=c.request('/content/sources',{'title':'调用账本权限验收','text':'仅供隔离调用账本权限验收。'},expected=201);run=c.request('/builder/runs',{'sourceId':source['id']},expected=202);other.request('/builder/calls?runId='+run['id'],expected=404)
    for _ in range(100):
        if c.request('/builder/calls?runId='+run['id'])['total']:break
        time.sleep(.05)
    else:raise AssertionError('scoped call ledger not visible to owner')
    from projection_receipt_api_acceptance import verify as verify_projection_receipts
    verify_projection_receipts(c,sid);other.request('/students/'+sid+'/consumer-receipts',expected=404)
    callid=c.request('/builder/calls?runId='+run['id'])['calls'][0]['id'];other.request('/builder/calls/'+callid+':reconcile',{'providerFinished':True,'chargedCost':0,'currency':None,'inputTokens':None,'outputTokens':None,'reason':'不能跨家庭核对','receiptReference':'fixture'},expected=404)
    assert all(j['familyId']!=c.request('/me')['family']['id'] for j in other.request('/background-jobs')['jobs']);c.request('/students/'+sid+'/child-sessions',{});c.request('/domain-events',expected=403);c.request('/background-jobs',expected=403);c.request('/students/'+sid+'/consumer-receipts',expected=403);c.request('/attempts/'+aid+'/grading-context',expected=403);c.request('/plans/'+rev['id']+'/questions',expected=403);c.request('/students/'+sid+'/assessment-contexts',expected=403);c.request('/students/'+sid+'/evidence-revocations',expected=403)
    c.request('/builder/calls/'+callid+':reconcile',{'providerFinished':True,'chargedCost':0,'currency':None,'inputTokens':None,'outputTokens':None,'reason':'孩子不能核对','receiptReference':'fixture'},expected=403);c.request('/builder/budget',expected=403);c.request('/builder/budget',{'dailyCostLimit':0,'dailyTokenLimit':0,'perCallCostLimit':0,'perCallTokenLimit':0,'maxConcurrentCalls':1,'reason':'孩子不能修改预算'},method='PUT',expected=403);c.request('/builder/calls',expected=403);c.request('/content/mapping-sets',expected=403);c.request('/content/mapping-sets/'+direct_saved['set']['id'],expected=403);c.request('/content/mapping-sets',direct,expected=403)
    print('PASS 独立映射真实草稿跨家庭404、列表隔离、孩子拒绝读写；孩子不能获取参考答案与家长观察判分上下文')
if __name__=='__main__':main()
