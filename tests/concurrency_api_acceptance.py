"""Concurrent first answers, concurrent candidate review, missing content and alias dedupe."""
import concurrent.futures
import copy
import getpass
import json
import os
import subprocess
import time
import uuid
from api_acceptance import Client, TODAY

def main():
    c=Client();other=Client();name='concurrency-'+uuid.uuid4().hex[:12];password='private-'+uuid.uuid4().hex
    c.request('/auth/register',{'userName':name,'password':password},expected=201);c.request('/me')
    other.request('/auth/login',{'userName':name,'password':password});other.request('/me')
    sid=c.request('/students',{'name':'并发验收'},expected=201)['id']
    draft=c.request('/content/fixture',{});catalog=json.loads(draft['payload'])
    def publish(d):
        c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview');return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']})
    release=publish(draft);c.request('/students/'+sid+'/content/'+release['id']+':bind',{})
    c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    revision=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});tasks=c.request('/students/'+sid+'/plans/'+TODAY)['tasks']
    c.request('/plans/'+revision['id']+':publish',{'previewHash':revision['inputHash']})
    sessions=[]
    for t in tasks[:2]:
        c.request('/tasks/'+t['id']+':transition',{'status':'InProgress'});sessions.append(c.request('/tasks/'+t['id']+'/sessions',{})['sessionId'])
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        fs=[pool.submit(client.request,'/sessions/'+session+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'999'},expected=201) for client,session in zip([c,other],sessions)]
        attempts=[f.result()['attempt'] for f in fs]
    for _ in range(100):
        m=c.request('/students/'+sid+'/mastery')
        if m['pending']==0:break
        time.sleep(.1)
    assert m['pending']==0 and len(m['masteries'])==2 and all(x['alpha']==2 and x['beta']==3 for x in m['masteries'])
    exported=c.request('/students/'+sid+'/export');generation=next(g for g in exported['generations'] if g['id']==m['generation'])
    assert generation['cursor']==max(a['sequence'] for a in attempts)
    assert len([e for e in exported['evidence'] if e['generationId']==generation['id']])==2
    print('PASS AT11 多个首次作答并发：无丢更新，活动证据和事件游标一致')
    source=c.request('/content/sources',{'title':'重复别名验收','text':'先乘除后加减，需要确定计算顺序。\n混合运算中先确定计算顺序，再动笔。'},expected=201)
    run=c.request('/builder/runs',{'sourceId':source['id']},expected=202)
    for _ in range(100):
        info=c.request('/builder');candidates=[x for x in info['candidates'] if x['runId']==run['id']]
        if len(candidates)==2:break
        time.sleep(.1)
    assert len(candidates)==2
    other.request('/me');decision={'decision':'LinkExisting','existingKCId':catalog['kcs'][0]['id'],'name':'运算优先顺序','reason':'人工核对行为和边界一致'}
    def decide(client):
        try:client.request('/builder/candidates/'+candidates[0]['id']+':decide',decision);return True
        except AssertionError as error:
            assert error.args[0][1] in [409,412],error
            return False
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        results=list(pool.map(decide,[c,other]))
    assert results.count(True)==1
    c.request('/me');c.request('/builder/candidates/'+candidates[1]['id']+':decide',decision)
    env=os.environ.copy();env.update(PGHOST='127.0.0.1',PGPORT='55432',PGUSER=getpass.getuser(),PGDATABASE='learning');env['PATH']='/opt/homebrew/opt/postgresql@16/bin:'+env['PATH']
    family=candidates[0]['familyId'];uuid.UUID(family)
    count=subprocess.check_output(['psql','-Atc',f'''SELECT COUNT(*) FROM "Alias" WHERE "FamilyId"='{family}' '''],env=env,text=True).strip()
    assert count=='1'
    print('PASS AT31 并发审核仅一次有效，同一能力同一别名不重复建库')
    missing=copy.deepcopy(catalog);missing['resources']=[]
    for q in missing['questions']:
        q['revisionId']=str(uuid.uuid4());q['type']='MultiStep';q['policy']='ObservedSteps'
        for mapping in q['mappings']:mapping['mode']='StepObserved';mapping['step']='calculate'
    r2=publish(c.request('/content/drafts',{'title':'需要家长观察步骤的内容','catalog':missing}))
    new=c.request('/students',{'name':'内容缺口验收'},expected=201)['id'];c.request('/students/'+new+'/content/'+r2['id']+':bind',{})
    c.request('/students/'+new+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    rev=c.request('/students/'+new+'/plans/'+TODAY+':generate',{});view=c.request('/students/'+new+'/plans/'+TODAY)
    assert view['tasks']==[] and all(w.startswith('MISSING_CONTENT:') for w in json.loads(rev['warnings'])) and len(json.loads(rev['warnings']))==3
    c.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash']},expected=422)
    print('PASS AT28 无可自动测量题和资源时明确缺口，不生成空任务，发布需确认')

if __name__=='__main__':main()
