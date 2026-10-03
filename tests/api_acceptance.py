"""Real PostgreSQL-backed API acceptance. Only creates isolated test families."""
import concurrent.futures
import http.cookiejar
import json
import time
import urllib.error
import urllib.request
import uuid
import base64
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo

BASE = 'http://127.0.0.1:5080/api/v1'
TODAY = datetime.now(ZoneInfo('Asia/Shanghai')).date().isoformat()

class Client:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.http = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
        self.etag = ''
    def request(self, path, body=None, method=None, key=None, expected=200):
        method = method or ('GET' if body is None else 'POST')
        headers = {'Content-Type':'application/json','X-Learning-Request':'1','Idempotency-Key':key or str(uuid.uuid4()),'If-Match':self.etag}
        req = urllib.request.Request(BASE+path,data=None if body is None else json.dumps(body,ensure_ascii=False).encode(),method=method,headers=headers)
        try:
            r=self.http.open(req)
        except urllib.error.HTTPError as e:
            r=e
        payload=r.read().decode()
        if r.headers.get('ETag'): self.etag=r.headers['ETag']
        assert r.status==expected,(path,r.status,payload[:1000])
        return json.loads(payload) if payload else None

passed=[]
def check(name, condition):
    assert condition,name
    passed.append(name)
    print('PASS',name)

def main():
    a=Client(); b=Client(); suffix=uuid.uuid4().hex[:12]
    password='only-test-'+uuid.uuid4().hex
    for c,n in [(a,'a'),(b,'b')]:c.request('/auth/register',{'userName':'acceptance-'+n+suffix,'password':password},expected=201)
    a.request('/me');b.request('/me')
    s=a.request('/students',{'name':'验收学生','dailyMinutes':30},expected=201)
    b.request('/students/'+s['id']+'/catalog',expected=404)
    check('AT29 跨家庭学生查询 404',True)
    d=a.request('/content/fixture',{})
    a.request('/content/drafts/'+d['id']+':publish',{'previewHash':'none'},expected=422)
    check('AT08 未审核草稿不可发布',True)
    catalog=json.loads(d['payload'])
    a.request('/content/drafts/'+d['id']+':review',{})
    preview=a.request('/content/drafts/'+d['id']+'/preview')
    a.request('/content/drafts/'+d['id']+':publish',{'previewHash':preview['hash']+'changed'},expected=412)
    check('AT36 发布预览哈希校验',True)
    release=a.request('/content/drafts/'+d['id']+':publish',{'previewHash':preview['hash']})
    a.request('/students/'+s['id']+'/content/'+release['id']+':bind',{})
    a.request('/students/'+s['id']+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    revision=a.request('/students/'+s['id']+'/plans/'+TODAY+':generate',{})
    view=a.request('/students/'+s['id']+'/plans/'+TODAY)
    task=view['tasks'][0]
    a.request('/tasks/'+task['id']+'/sessions',{},expected=422)
    check('AT08 计划草稿不能领取',True)
    again=a.request('/students/'+s['id']+'/plans/'+TODAY+':generate',{})
    check('AT27 同一输入重复生成同一草稿',again['id']==revision['id'])
    a.request('/plans/'+revision['id']+':publish',{'previewHash':revision['inputHash']})
    a.request('/tasks/'+task['id']+':transition',{'status':'InProgress'})
    session=a.request('/tasks/'+task['id']+'/sessions',{})
    check('孩子领取响应不含答案',not any(k in session for k in ['answer','answerSpec','explanation','mappings']))
    key=str(uuid.uuid4());body={'clientSubmissionId':str(uuid.uuid4()),'answer':'999'}
    first=a.request('/sessions/'+session['sessionId']+'/attempts',body,key=key,expected=201)
    for _ in range(9):
        retried=a.request('/sessions/'+session['sessionId']+'/attempts',body,key=key,expected=201)
        assert retried['attempt']['id']==first['attempt']['id']
    check('AT01 重发十次只有一次作答效果',True)
    a.request('/sessions/'+session['sessionId']+'/attempts',{**body,'answer':'998'},key=key,expected=409)
    check('AT02 同幂等键不同答案 409',True)
    for _ in range(40):
        m=a.request('/students/'+s['id']+'/mastery')
        if m['pending']==0 and m['masteries']:break
        time.sleep(.2)
    check('Outbox 作答投影最终追平',m['pending']==0 and len(m['masteries'])==1)
    a.request('/tasks/'+task['id']+':transition',{'status':'Completed'})
    second=a.request('/students/'+s['id']+'/plans/'+TODAY+':generate',{})
    view=a.request('/students/'+s['id']+'/plans/'+TODAY)
    check('AT26 重生成保留已完成 TaskId',any(t['id']==task['id'] and t['status']=='Completed' for t in view['tasks']))
    wrong=a.request('/students/'+s['id']+'/reviews')
    check('错题自动建立 R1 日程',any(r['targetType']=='WrongQuestion' and r['stage']=='R1' for r in wrong))
    oldgen=m['generation'];aid=first['attempt']['id']
    correction={'result':'Correct','reason':'验收：原判分修正'}
    preview=a.request('/attempts/'+aid+'/grading-preview',correction)
    a.request('/attempts/'+aid+'/grading-revisions',{**correction,'previewHash':preview['previewHash']},expected=202)
    for _ in range(40):
        m=a.request('/students/'+s['id']+'/mastery')
        if m['generation']!=oldgen and m['pending']==0:break
        time.sleep(.2)
    check('AT19 更正切换评估世代，旧负证据不双计',m['masteries'][0]['beta']==2 and m['masteries'][0]['alpha']==3)
    g=m['generation'];event_count=a.request('/domain-events?studentId='+s['id'])['total'];a.request('/students/'+s['id']+':rebuild',{});assert a.request('/domain-events?studentId='+s['id'])['total']==event_count
    check('固定输入重复重算复用世代',a.request('/students/'+s['id']+'/mastery')['generation']==g)
    source=a.request('/content/sources',{'title':'受控验收文本','text':'先算乘除，再算加减。'},expected=201)
    a.request('/builder/runs',{'sourceId':source['id'],'provider':'Cloud'},expected=422)
    check('AT35 未授权外部模型禁止外发',True)
    run=a.request('/builder/runs',{'sourceId':source['id']},expected=202)
    for _ in range(40):
        builder=a.request('/builder')
        candidates=[c for c in builder['candidates'] if c['runId']==run['id']]
        if candidates:break
        time.sleep(.2)
    check('候选来源可追溯',bool(candidates) and candidates[0]['quote'] in source['text'])
    c=candidates[0]
    payload=json.loads(c['protocolPayload']);assert payload['subject']=='MATH' and payload['gradeMin']==1 and payload['gradeMax']==12 and payload['sourceChunkIds']==[c['chunkId']] and payload['supportingQuotes']==[c['quote']]
    protocol=json.loads(next(x for x in builder['attempts'] if x['runId']==run['id'])['protocolResult']);assert protocol['calls']==1 and protocol['repaired'] is False
    a.request('/builder/candidates/'+c['id']+':decide',{'decision':'Reject','reason':'验收审核'})
    a.request('/builder/candidates/'+c['id']+':decide',{'decision':'Reject','reason':'重复审核'},expected=409)
    check('AT31 候选仅允许审核一次',True)
    a.request('/students/'+s['id']+'/child-sessions',{})
    a.request('/domain-events',expected=403)
    a.request('/content',expected=403)
    a.request('/students/'+s['id']+'/mastery',expected=403)
    check('孩子会话无管理权限',True)
    a.request('/auth/login',{'userName':'acceptance-a'+suffix,'password':password})
    a.request('/me')
    tomorrow=(datetime.now(ZoneInfo('Asia/Shanghai')).date()+timedelta(days=1)).isoformat()
    rev=a.request('/students/'+s['id']+'/plans/'+tomorrow+':generate',{})
    a.request('/plans/'+rev['id']+'/tasks',{'title':'必做验收','minutes':50,'resourceRef':'纸质作业','type':'Schoolwork'})
    v=a.request('/students/'+s['id']+'/plans/'+tomorrow)
    check('AT24 API 必做 50/预算30，超载20，无可选任务',v['revision']['overflow']==20 and len(v['tasks'])==1)
    a.request('/plans/'+rev['id']+':publish',{'previewHash':v['revision']['inputHash']},expected=422)
    check('超载发布需要明确确认',True)
    a.request('/plans/'+rev['id']+':publish',{'previewHash':v['revision']['inputHash'],'confirmWarnings':True})
    png=base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1cAAAAASUVORK5CYII=')
    f=a.request('/files',{'name':'wrong.png','mimeType':'image/png','base64':base64.b64encode(png).decode()},expected=201)
    b.request('/files/'+f['id'],expected=404)
    check('AT29 私有文件跨家庭 404',True)
    a.request('/students/'+s['id']+'/paper-wrongs',{'stem':'纸质错题','answer':'不知道','fileId':f['id']},expected=201)
    check('纸质图片待归因保存',len(a.request('/students/'+s['id']+'/paper-wrongs'))==1)
    export=a.request('/students/'+s['id']+'/export')
    check('导出保留源作答与活动世代',len(export['attempts'])==1 and len(export['generations'])==2)
    # Published content cannot be rewritten through a reused revision identifier.
    changed=json.loads(d['payload']);changed['questions'][0]['answer']='99999'
    newdraft=a.request('/content/drafts',{'title':'不可变验收','catalog':changed})
    a.request('/content/drafts/'+newdraft['id']+':review',{})
    p=a.request('/content/drafts/'+newdraft['id']+'/preview')
    a.request('/content/drafts/'+newdraft['id']+':publish',{'previewHash':p['hash']},expected=422)
    check('已发布修订不可原地覆盖',True)
    deletion=a.request('/students/'+s['id']+'/delete-preview')
    a.request('/students/'+s['id']+':delete',{'previewHash':deletion['previewHash'],'password':password,'confirm':'永久删除学生'})
    a.request('/students/'+s['id']+'/catalog',expected=404)
    check('隐私删除级联清理学习记录',not a.request('/students'))
    print(f'{len(passed)} API checks passed')

if __name__=='__main__':main()
