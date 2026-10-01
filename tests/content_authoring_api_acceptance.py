"""Stable meaning enforcement, new KC identity without transferred probability, cycle path."""
import copy
import json
import time
import uuid
from api_acceptance import Client,TODAY

def main():
    c=Client();c.request('/auth/register',{'userName':'meaning-'+uuid.uuid4().hex[:12],'password':'private-'+uuid.uuid4().hex},expected=201);c.request('/me')
    sid=c.request('/students',{'name':'能力身份验收'},expected=201)['id'];d=c.request('/content/fixture',{});catalog=json.loads(d['payload'])
    def publish(draft):
        c.request('/content/drafts/'+draft['id']+':review',{});preview=c.request('/content/drafts/'+draft['id']+'/preview');return c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':preview['hash']})
    r1=publish(d);c.request('/students/'+sid+'/content/'+r1['id']+':bind',{});c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    rev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});tasks=c.request('/students/'+sid+'/plans/'+TODAY)['tasks'];c.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash']})
    task=tasks[0];question=next(q for q in catalog['questions'] if q['id']==task['questionId']);oldkc=question['mappings'][0]['kcId']
    c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+task['id']+'/sessions',{});c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':question['answer']},expected=201)
    c.request('/tasks/'+task['id']+':transition',{'status':'Completed'})
    def settle():
        for _ in range(100):
            m=c.request('/students/'+sid+'/mastery')
            if m['pending']==0:return m
            time.sleep(.1)
        raise AssertionError('projection timeout')
    original=settle()['masteries'][0]
    renamed=copy.deepcopy(catalog);renamed['kcs'][0]['name']+=' · 名称说明';renamed['kcs'][0]['revisionId']=str(uuid.uuid4())
    publish(c.request('/content/drafts',{'title':'名称修订','catalog':renamed}))
    print('PASS 同一测量含义可沿用稳定能力身份修订名称')
    for field,value in [('behavior','独立计算两数和'),('boundary','只测10以内加法'),('type','Concept'),('requiredCoverage',['Basic','Transfer'])]:
        changed=copy.deepcopy(renamed);changed['kcs'][0][field]=value;changed['kcs'][0]['revisionId']=str(uuid.uuid4())
        bad=c.request('/content/drafts',{'title':'错误复用身份','catalog':changed});c.request('/content/drafts/'+bad['id']+':review',{});p=c.request('/content/drafts/'+bad['id']+'/preview')
        error=c.request('/content/drafts/'+bad['id']+':publish',{'previewHash':p['hash']},expected=422)
        assert error['code']=='MEASUREMENT_IDENTITY_CHANGED' and len(c.request('/content')['releases'])==2
    print('PASS 改变测量行为、边界、类型或覆盖要求不能复用旧身份，发布不留下半成品')
    fresh=copy.deepcopy(renamed);kc=copy.deepcopy(renamed['kcs'][0]);kc.update(behavior='独立计算两数和',boundary='只测10以内加法');kc.update(id=str(uuid.uuid4()),revisionId=str(uuid.uuid4()),code='MATH.CUSTOM.'+uuid.uuid4().hex,name='新独立能力');fresh['kcs'].append(kc)
    q={'id':str(uuid.uuid4()),'revisionId':str(uuid.uuid4()),'stem':'2 + 3 = ?','answer':'5','explanation':'2和3合起来是5。','type':'Numeric','difficulty':'Medium','policy':'SingleKC','mappings':[{'kcId':kc['id'],'role':'Primary','share':1,'mode':'WholeItem'}],'coverage':'Basic'};fresh['questions'].append(q)
    lesson={'id':str(uuid.uuid4()),'title':'新能力独立诊断','sequence':5,'kcIds':[kc['id']]};fresh['lessons'].append(lesson)
    r3=publish(c.request('/content/drafts',{'title':'新身份保持独立','catalog':fresh}));c.request('/students/'+sid+'/content/'+r3['id']+':bind',{})
    assert not any(m['kcId']==kc['id'] for m in c.request('/students/'+sid+'/mastery')['masteries'])
    c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+lesson['id'],{},method='PUT');rev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});new=next(t for t in c.request('/students/'+sid+'/plans/'+TODAY)['tasks'] if t['kcId']==kc['id']);c.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash']})
    c.request('/tasks/'+new['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+new['id']+'/sessions',{});c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'5'},expected=201)
    m=settle();previous=next(x for x in m['masteries'] if x['kcId']==oldkc);independent=next(x for x in m['masteries'] if x['kcId']==kc['id'])
    assert (previous['alpha'],previous['beta'])==(original['alpha'],original['beta']) and (independent['alpha'],independent['beta'])==(3,2)
    print('PASS AT34 新能力从无证据开始，新作答只产生自己的证据，旧概率不转移')
    cycle=copy.deepcopy(catalog);cycle['relations']=[{'from':catalog['kcs'][i]['id'],'to':catalog['kcs'][(i+1)%3]['id'],'type':'Prerequisite'} for i in range(3)]
    d=c.request('/content/drafts',{'title':'前置环验收','catalog':cycle});e=c.request('/content/drafts/'+d['id']+':review',{},expected=422)
    assert all(k['name'] in e['title'] for k in catalog['kcs'][:3]) and '→' in e['title']
    print('PASS AT33 前置环拒绝，并返回家长可读的完整环路径')

if __name__=='__main__':main()
