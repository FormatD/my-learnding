"""Deferral and concurrent correction acceptance against real local PostgreSQL."""
import base64
import concurrent.futures
import json
import time
import uuid
from datetime import date, timedelta
from api_acceptance import Client, TODAY

def main():
    c=Client();name='boundary-'+uuid.uuid4().hex[:12];password='private-'+uuid.uuid4().hex
    c.request('/auth/register',{'userName':name,'password':password},expected=201);c.request('/me')
    sid=c.request('/students',{'name':'边界验收'},expected=201)['id']
    d=c.request('/content/fixture',{});catalog=json.loads(d['payload'])
    c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview')
    r=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']});c.request('/students/'+sid+'/content/'+r['id']+':bind',{})
    c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    rev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});tasks=c.request('/students/'+sid+'/plans/'+TODAY)['tasks']
    c.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash'],'confirmWarnings':True})
    sessions=[]
    for task in tasks[:2]:
        c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});sessions.append(c.request('/tasks/'+task['id']+'/sessions',{})['sessionId'])
    a=c.request('/sessions/'+sessions[0]+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'999'},expected=201)['attempt']
    def settle():
        for _ in range(100):
            m=c.request('/students/'+sid+'/mastery')
            if m['pending']==0:return m
            time.sleep(.1)
        raise AssertionError('projection did not settle')
    settle()
    correction={'result':'Correct','reason':'并发验收：家长更正'}
    preview=c.request('/attempts/'+a['id']+'/grading-preview',correction)
    other=Client();other.request('/auth/login',{'userName':name,'password':password});other.request('/me')
    # Different sessions use independent HTTP clients; server family lock serializes commits.
    def confirm():
        try:return c.request('/attempts/'+a['id']+'/grading-revisions',{**correction,'previewHash':preview['previewHash']},expected=202)
        except AssertionError as e:
            if '412' not in str(e):raise
            return None
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        f=pool.submit(confirm)
        second=pool.submit(other.request,'/sessions/'+sessions[1]+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'999'},expected=201).result()['attempt']
        done=f.result()
    if done is None:
        settle();preview=c.request('/attempts/'+a['id']+'/grading-preview',correction)
        c.request('/attempts/'+a['id']+'/grading-revisions',{**correction,'previewHash':preview['previewHash']},expected=202)
    m=settle();export=c.request('/students/'+sid+'/export')
    active=[g for g in export['generations'] if g['status']=='Active']
    assert len(active)==1 and active[0]['cursor']==max(a['sequence'],second['sequence'])
    assert len(export['attempts'])==2
    grades=sorted([g for g in export['gradings'] if g['attemptId']==a['id']],key=lambda g:g['number'])
    assert grades[-1]['result']=='Correct'
    assert len(export['evidence'])>=2
    print('PASS AT39 更正与新作答并发：完整保存、单活动世代、游标追平')
    original_count=c.request('/students/'+sid+'/weekly-summary')['original']['total']
    tomorrow=(date.fromisoformat(TODAY)+timedelta(days=1)).isoformat()
    deferred=c.request('/tasks/'+tasks[0]['id']+':defer',{'date':tomorrow,'reason':'今天时间不足'})
    assert not any(t['id']==tasks[0]['id'] for t in c.request('/students/'+sid+'/today')['tasks'])
    assert any(t['id']==tasks[0]['id'] and t['status']=='Deferred' for t in c.request('/students/'+sid+'/plans/'+tomorrow)['tasks'])
    summary=c.request('/students/'+sid+'/weekly-summary')
    assert summary['original']['total']==original_count and summary['adjusted']['total']==original_count-1
    print('PASS AT38 顺延后周报保留原始分母，不以缩减计划伪造完成率')
    c.request('/plans/'+deferred['revision']['id']+':publish',{'previewHash':deferred['revision']['inputHash'],'confirmWarnings':True})
    c.request('/students/'+sid+'/child-sessions',{})
    c.request('/tasks/'+tasks[0]['id']+':transition',{'status':'InProgress'},expected=409)
    c.request('/tasks/'+tasks[0]['id']+'/sessions',{},expected=422)
    c.request('/auth/login',{'userName':name,'password':password});c.request('/me')
    c.request('/tasks/'+tasks[0]['id']+':transition',{'status':'InProgress'})
    resumed=c.request('/tasks/'+tasks[0]['id']+'/sessions',{})
    assert resumed['sessionId']==sessions[0]
    assert len(c.request('/students/'+sid+'/export')['attempts'])==2
    print('PASS 家长顺延保留 TaskId、原会话和作答，孩子无法提前执行未来任务')
    png=base64.b64encode(bytes([137,80,78,71,13,10,26,10])+b'test-image').decode()
    file=c.request('/files',{'name':'paper.png','mimeType':'image/png','base64':png},expected=201)
    paper=c.request('/students/'+sid+'/paper-wrongs',{'stem':'纸质验收题','answer':'错答','fileId':file['id']},expected=201)
    export=c.request('/students/'+sid+'/export')
    assert export['format']=='learning-export/2' and export['paperWrongs'][0]['id']==paper['id']
    assert export['files'][0]['bytes']==png
    print('PASS 学生导出包含纸质错题和原始图片，可离线保存')
    c.request('/students/'+sid+'/availability/'+tomorrow,{'minutes':30,'reserved':10},method='PUT')
    revision=c.request('/students/'+sid+'/plans/'+tomorrow+':generate',{})
    c.request('/plans/'+revision['id']+'/tasks',{'title':'学校作业','minutes':10,'resourceRef':'学校作业本','type':'Schoolwork'},expected=422)
    print('PASS AT25 列表外预留作业与学校作业任务冲突时明确拒绝')


if __name__=='__main__':main()
