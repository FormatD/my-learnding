"""Goal schedules, quotas, frozen history and scoped question practice in real PostgreSQL."""
import json,uuid,time
from datetime import date,timedelta
from api_acceptance import Client,TODAY

def main():
    c=Client();other=Client()
    for client in [c,other]:client.request('/auth/register',{'userName':'goal-'+uuid.uuid4().hex[:12],'password':'goal-private-'+uuid.uuid4().hex},expected=201);client.request('/me')
    sid=c.request('/students',{'name':'周期目标验收'},expected=201)['id'];draft=c.request('/content/fixture',{});catalog=json.loads(draft['payload']);c.request('/content/drafts/'+draft['id']+':review',{});p=c.request('/content/drafts/'+draft['id']+'/preview');release=c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':p['hash']});c.request('/students/'+sid+'/content/'+release['id']+':bind',{})
    today=date.fromisoformat(TODAY);tomorrow=(today+timedelta(days=1)).isoformat();weekday=today.isoweekday()%7
    inp={'title':'每周阅读','minutes':5,'paperReference':'自选图书','subject':'Reading','goalType':'Reading','period':'Weekly','targetValue':1,'days':list(range(7)),'priority':4,'startDate':TODAY,'endDate':(today+timedelta(days=30)).isoformat(),'reason':'固定阅读安排'}
    c.request('/students/'+sid+'/goals',{**inp,'startDate':'2030-01-02','endDate':'2030-01-01'},expected=422)
    c.request('/students/'+sid+'/goals',{**inp,'days':[8]},expected=422)
    c.request('/students/'+sid+'/goals',{**inp,'kcId':catalog['kcs'][0]['id']},expected=422)
    g=c.request('/students/'+sid+'/goals',inp)
    def generate(day):
        c.request('/students/'+sid+'/plans/'+day+':generate',{});return c.request('/students/'+sid+'/plans/'+day)
    assert generate((today-timedelta(days=1)).isoformat())['tasks']==[]
    view=generate(TODAY);assert len(view['tasks'])==1;task=view['tasks'][0];assert json.loads(task['goalSnapshots'])[0]['version']==1
    c.request('/plans/'+view['revision']['id']+':publish',{'previewHash':view['revision']['inputHash'],'confirmWarnings':True})
    c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});c.request('/tasks/'+task['id']+':transition',{'status':'Completed'})
    assert c.request('/students/'+sid+'/mastery')['masteries']==[] and c.request('/students/'+sid+'/attempts')['attempts']==[]
    repeat=generate(TODAY);assert len(repeat['tasks'])==1 and repeat['tasks'][0]['id']==task['id']
    week_start=today-timedelta(days=today.weekday());next_week=(week_start+timedelta(days=7)).isoformat()
    if today.weekday()<6:assert generate(tomorrow)['tasks']==[]
    assert len(generate(next_week)['tasks'])==1
    print('PASS 日期范围、星期及类型校验；阅读无能力证据，周期配额达成后不重复安排，下周重新生效')
    changed={**inp,'goalType':'Listening','subject':'English','title':'英语听力','active':True,'days':[weekday]};updated=c.request('/goals/'+g['id'],changed,method='PUT');assert updated['version']==2
    old=c.request('/students/'+sid+'/today')['tasks'][0];assert json.loads(old['goalSnapshots'])[0]['title']=='每周阅读'
    assert len(c.request('/students/'+sid+'/goal-changes'))==2
    assert not any(t['title']=='英语听力' for t in generate(tomorrow)['tasks'])
    legacy=c.request('/goals/'+g['id'],{'title':changed['title'],'minutes':changed['minutes'],'paperReference':changed['paperReference'],'active':False},method='PUT');assert legacy['goalType']=='Listening' and legacy['period']=='Weekly' and legacy['endDate']==inp['endDate'] and json.loads(legacy['scheduleRule'])==[weekday]
    old_etag=c.etag;c.request('/goals/'+g['id'],{**changed,'active':False,'reason':'暂停听力'},method='PUT');c.etag=old_etag;c.request('/goals/'+g['id'],changed,method='PUT',expected=412);c.request('/me')
    other.request('/goals/'+g['id'],changed,method='PUT',expected=404)
    print('PASS 改范围不把旧阅读当听力，已发布任务保留目标快照，修订可追溯且失效版本拒绝')
    practice={**inp,'subject':'Math','goalType':'Practice','kcId':catalog['kcs'][0]['id'],'title':'能力范围A','targetValue':1,'priority':3}
    c.request('/students/'+sid+'/goals',{**practice,'kcId':str(uuid.uuid4())},expected=422)
    gp=c.request('/students/'+sid+'/goals',practice);gp2=c.request('/students/'+sid+'/goals',{**practice,'title':'能力范围A另一目标'})
    view=generate(TODAY);tasks=[t for t in view['tasks'] if t['type']=='Practice'];assert len(tasks)==1 and tasks[0]['kcId']==practice['kcId'] and len(json.loads(tasks[0]['goalSnapshots']))==2
    c.request('/plans/'+view['revision']['id']+':publish',{'previewHash':view['revision']['inputHash'],'confirmWarnings':True});c.request('/tasks/'+tasks[0]['id']+':transition',{'status':'InProgress'});c.request('/tasks/'+tasks[0]['id']+':transition',{'status':'Completed'},expected=422);session=c.request('/tasks/'+tasks[0]['id']+'/sessions',{});c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'999'},expected=201);c.request('/tasks/'+tasks[0]['id']+':transition',{'status':'Completed'})
    c.request('/students/'+sid+'/goals',{**practice,'title':'能力范围B','kcId':catalog['kcs'][1]['id']})
    view=generate(TODAY);new=next(t for t in view['tasks'] if t['type']=='Practice' and t['kcId']==catalog['kcs'][1]['id']);c.request('/plans/'+view['revision']['id']+':publish',{'previewHash':view['revision']['inputHash'],'confirmWarnings':True});c.request('/tasks/'+new['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+new['id']+'/sessions',{});c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'999'},expected=201);c.request('/tasks/'+new['id']+':transition',{'status':'Completed'})
    future=generate((today+timedelta(days=1 if today.weekday()<6 else 0)).isoformat())
    if today.weekday()<6:
        practice_tasks=[t for t in future['tasks'] if t['type']=='Practice'];assert practice_tasks==[]
    print('PASS 目标仅测指定能力，两目标共用题目不重复安排；勾完成但未作答不算能力练习配额')
    c.request('/students/'+sid+'/goals',{**inp,'title':'日期不足目标','startDate':TODAY,'endDate':TODAY,'days':[(weekday+6)%7]});assert any('GOAL_QUOTA_GAP' in w for w in json.loads(generate(TODAY)['revision']['warnings']))
    export=c.request('/students/'+sid+'/export');assert len(export['goalChanges'])>=5 and any(json.loads(t['goalSnapshots']) for t in export['tasks'])
    c.request('/students/'+sid+'/child-sessions',{});c.request('/students/'+sid+'/goals',expected=403);c.request('/students/'+sid+'/goal-changes',expected=403)
    print('PASS 目标修订和任务快照可导出，孩子不能编辑或查看家长目标管理记录')
if __name__=='__main__':main()
