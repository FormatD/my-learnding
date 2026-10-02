"""Published textbook/course directory, stable revisions and actual goal scope planning."""
import copy,json,uuid
from api_acceptance import Client,TODAY

def main():
    c=Client();other=Client()
    for client in [c,other]:client.request('/auth/register',{'userName':'directory-'+uuid.uuid4().hex[:12],'password':'directory-test-'+uuid.uuid4().hex},expected=201);client.request('/me')
    sid=c.request('/students',{'name':'目录范围验收'},expected=201)['id'];d=c.request('/content/fixture',{});catalog=json.loads(d['payload']);unit1=catalog['units'][0]
    source=c.request('/content/sources',{'title':'目录验收原创说明','text':'这些课程与题目用于本地目录范围验收。','allowExternalAI':False,'usageScope':'FamilyOnly'},expected=201)
    catalog['textbooks'][0]['sourceId']=source['id'];catalog['textbooks'][0]['edition']='验收用原创适配说明，非正式教材原题'
    unit2={**unit1,'id':str(uuid.uuid4()),'revisionId':str(uuid.uuid4()),'title':'小括号范围','sequence':2};catalog['units'].append(unit2)
    math={'id':str(uuid.uuid4()),'revisionId':str(uuid.uuid4()),'provider':'原创测试','subject':'Math','title':'同级运算课程','sourceRefs':[source['id']]}
    english={'id':str(uuid.uuid4()),'revisionId':str(uuid.uuid4()),'provider':'家长自备','subject':'English','title':'每日听力来源','sourceRefs':[]};catalog['courses']=[math,english]
    catalog['lessons'][2]['unitId']=unit2['id'];catalog['lessons'][2]['sequence']=1
    catalog['lessons'][3]['unitId']=None;catalog['lessons'][3]['courseId']=math['id'];catalog['lessons'][3]['sequence']=1;catalog['lessons'][3]['sourceRefs']=[source['id']]
    foreign=other.request('/content/sources',{'title':'其他家庭来源','text':'仅其他家庭可用。','allowExternalAI':False,'usageScope':'FamilyOnly'},expected=201)
    def draft(value):return c.request('/content/drafts',{'title':'目录验收草稿','catalog':value})
    def publish(d):
        c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview');return c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']})
    invalid=copy.deepcopy(catalog);invalid['courses'][0]['sourceRefs']=[foreign['id']];bad=draft(invalid);c.request('/content/drafts/'+bad['id']+':review',{},expected=422)
    invalid=copy.deepcopy(catalog);invalid['units'][1]['textbookId']=str(uuid.uuid4());bad=draft(invalid);c.request('/content/drafts/'+bad['id']+':review',{},expected=422)
    invalid=copy.deepcopy(catalog);invalid['lessons'][0]['courseId']=math['id'];bad=draft(invalid);c.request('/content/drafts/'+bad['id']+':review',{},expected=422)
    release=publish(draft(catalog));c.request('/students/'+sid+'/content/'+release['id']+':bind',{});active=c.request('/students/'+sid+'/catalog');assert active['units'][1]['title']=='小括号范围' and active['textbooks'][0]['sourceId']==source['id']
    print('PASS 教材、单元、课程、课时和家庭来源完整发布；非法归属及跨家庭来源拒绝')
    changed=copy.deepcopy(catalog);changed['units'][0]['title']='已改名但未换修订';bad=draft(changed);c.request('/content/drafts/'+bad['id']+':review',{});p=c.request('/content/drafts/'+bad['id']+'/preview');c.request('/content/drafts/'+bad['id']+':publish',{'previewHash':p['hash']},expected=422)
    changed['units'][0]['revisionId']=str(uuid.uuid4());r2=publish(draft(changed));assert json.loads(c.request('/content')['releases'][-1]['payload'])['units'][0]['title']==unit1['title']
    print('PASS 同一目录修订不能被覆盖，名称调整须新修订，旧发布目录保持原值')
    goal={'title':'单元限定练习','minutes':5,'paperReference':'正式题目','subject':'Math','goalType':'Practice','unitId':unit1['id'],'period':'Daily','targetValue':1,'reason':'按教材单元安排'}
    for change in [{'unitId':str(uuid.uuid4())},{'kcId':catalog['kcs'][3]['id']},{'courseId':math['id']},{'unitId':None}]:c.request('/students/'+sid+'/goals',{**goal,**change},expected=422)
    g=c.request('/students/'+sid+'/goals',goal)
    def generate():c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});return c.request('/students/'+sid+'/plans/'+TODAY)
    view=generate();task=next(t for t in view['tasks'] if t['title']==goal['title']);assert task['kcId'] in set(k for l in catalog['lessons'] if l.get('unitId')==unit1['id'] for k in l['kcIds']) and json.loads(task['goalSnapshots'])[0]['unitId']==unit1['id']
    c.request('/plans/'+view['revision']['id']+':publish',{'previewHash':view['revision']['inputHash'],'confirmWarnings':True});c.request('/tasks/'+task['id']+':transition',{'status':'InProgress'});session=c.request('/tasks/'+task['id']+'/sessions',{});c.request('/sessions/'+session['sessionId']+'/attempts',{'clientSubmissionId':str(uuid.uuid4()),'answer':'999'},expected=201);c.request('/tasks/'+task['id']+':transition',{'status':'Completed'})
    changed_goal={**goal,'unitId':unit2['id'],'active':True,'reason':'切换小括号单元'};c.request('/goals/'+g['id'],changed_goal,method='PUT');view=generate();new=next(t for t in view['tasks'] if t['status']=='Planned' and t['title']==goal['title']);assert new['kcId'] in catalog['lessons'][2]['kcIds'] and new['id']!=task['id'];assert json.loads(next(t for t in view['tasks'] if t['id']==task['id'])['goalSnapshots'])[0]['unitId']==unit1['id']
    legacy=c.request('/goals/'+g['id'],{'title':goal['title'],'minutes':5,'paperReference':'正式题目','active':False},method='PUT');assert legacy['unitId']==unit2['id']
    print('PASS 单元练习只选范围内能力；改范围不复用旧完成配额，任务快照与旧编辑范围保留')
    course_goal={**goal,'title':'课程限定练习','unitId':None,'courseId':math['id']};c.request('/students/'+sid+'/goals',course_goal);listening={**goal,'title':'自备听力','unitId':None,'courseId':english['id'],'goalType':'Listening','subject':'English'};c.request('/students/'+sid+'/goals',listening);view=generate();practice=next(t for t in view['tasks'] if t['title']==course_goal['title']);activity=next(t for t in view['tasks'] if t['title']=='自备听力');assert practice['kcId'] in catalog['lessons'][3]['kcIds'] and activity['questionId'] is None and activity['kcId'] is None
    export=c.request('/students/'+sid+'/export');assert any(g.get('courseId')==english['id'] for g in export['goals']);assert any(json.loads(t['goalSnapshots'])[0].get('unitId')==unit1['id'] for t in export['tasks'] if json.loads(t['goalSnapshots']))
    print('PASS 课程练习限定已映射能力，听力课程只生成行为任务，目录与目标历史可导出')
if __name__=='__main__':main()
