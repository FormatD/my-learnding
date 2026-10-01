"""Progress correction preserves original records, learning results and published plans."""
import json
import time
import uuid
from api_acceptance import Client,TODAY

def main():
    c=Client();c.request('/auth/register',{'userName':'progress-'+uuid.uuid4().hex[:12],'password':'private-'+uuid.uuid4().hex},expected=201);c.request('/me')
    sid=c.request('/students',{'name':'进度纠正验收'},expected=201)['id'];draft=c.request('/content/fixture',{});catalog=json.loads(draft['payload'])
    c.request('/content/drafts/'+draft['id']+':review',{});p=c.request('/content/drafts/'+draft['id']+'/preview');release=c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':p['hash']})
    c.request('/students/'+sid+'/content/'+release['id']+':bind',{})
    old=c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    assert old['releaseId']==release['id'] and old['status']=='Confirmed'
    rev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});tasks=c.request('/students/'+sid+'/plans/'+TODAY)['tasks'];c.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash']})
    task=tasks[0];c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+task['id']+'/sessions',{})
    c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'999'},expected=201)
    for _ in range(100):
        m=c.request('/students/'+sid+'/mastery')
        if m['pending']==0:break
        time.sleep(.1)
    path='/students/'+sid+'/progress/'+old['id']+':correct'
    c.request(path,{'lessonId':catalog['lessons'][1]['id'],'reason':''},expected=422)
    c.request(path,{'lessonId':str(uuid.uuid4()),'reason':'课时错误'},expected=422)
    assert c.request('/students/'+sid+'/progress')[0]['id']==old['id']
    corrected=c.request(path,{'lessonId':catalog['lessons'][1]['id'],'reason':'家长核对作业，实际已学到买文具'})
    active=c.request('/students/'+sid+'/progress');history=c.request('/students/'+sid+'/progress?includeHistory=true')
    assert len(active)==1 and active[0]['id']==corrected['replacement']['id'] and len(history)==2 and next(p for p in history if p['id']==old['id'])['status']=='Superseded'
    assert json.loads(corrected['change']['before'])['lessonId']==catalog['lessons'][0]['id']
    print('PASS 进度纠正保留原记录、原因、前后快照及内容版本；无效纠正无写入')
    current=c.request('/students/'+sid+'/today');export=c.request('/students/'+sid+'/export')
    assert [t['id'] for t in current['tasks']]==[t['id'] for t in tasks]
    assert len(export['attempts'])==1 and export['sessions'][0]['releaseId']==release['id'] and export['student']['activeGenerationId']==m['generation']
    assert len(export['progressChanges'])==1
    print('PASS 纠正不改变已发布计划、原始作答及评估世代，导出包含历史纠正')
    newrev=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});newtasks=c.request('/students/'+sid+'/plans/'+TODAY)['tasks']
    retired={catalog['kcs'][4]['id'],catalog['kcs'][5]['id']}
    assert newrev['inputHash']!=rev['inputHash'] and all(t['kcId'] not in retired for t in newtasks if t['id']!=task['id'])
    assert any(t['id']==task['id'] and t['status']=='InProgress' for t in newtasks)
    print('PASS 新草稿采用有效课时，保留正在执行的TaskId')
    target=corrected['replacement']['id'];c.request('/students/'+sid+'/progress/'+target+':correct',{'lessonId':None,'reason':'这条确认也需要撤回'})
    assert c.request('/students/'+sid+'/progress')==[]
    c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    assert c.request('/students/'+sid+'/progress')[0]['id']==old['id'] and len(c.request('/students/'+sid+'/progress-changes'))==3
    print('PASS 进度可撤回与重新确认，全部操作留有可读历史')
    c.request('/students/'+sid+'/child-sessions',{})
    c.request('/students/'+sid+'/progress-changes',expected=403)
    c.request('/students/'+sid+'/progress/'+old['id']+':correct',{'lessonId':None,'reason':'孩子尝试更改'},expected=403)
    print('PASS 孩子会话不能查询或修改家长进度纠正记录')

if __name__=='__main__':main()
