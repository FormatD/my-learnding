"""Parent overrides protect fixed tasks and reject stale browser writes."""
import json,uuid
from api_acceptance import Client,TODAY

def main():
    c=Client();username='adjust-'+uuid.uuid4().hex[:12];password='private-'+uuid.uuid4().hex;c.request('/auth/register',{'userName':username,'password':password},expected=201);c.request('/me');sid=c.request('/students',{'name':'家长调整验收'},expected=201)['id']
    d=c.request('/content/fixture',{});catalog=json.loads(d['payload']);c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview');r=c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p['hash']});c.request('/students/'+sid+'/content/'+r['id']+':bind',{});c.request('/students/'+sid+'/school-progress/'+TODAY+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    original=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});original_tasks=c.request('/students/'+sid+'/plans/'+TODAY)['tasks'];c.request('/plans/'+original['id']+':publish',{'previewHash':original['inputHash']});c.request('/students/'+sid+'/availability/'+TODAY,{'minutes':25,'reserved':0},method='PUT');draft=c.request('/students/'+sid+'/plans/'+TODAY+':generate',{});v=c.request('/students/'+sid+'/plans/'+TODAY);ids=[t['id'] for t in v['tasks']];target=ids[0];path='/plans/'+draft['id']+':adjust';stale=c.etag
    another=Client();another.request('/auth/login',{'userName':username,'password':password});another.request('/students/'+sid+'/plans/'+TODAY);another.request(path,{'taskIds':ids,'lockedIds':[target],'reason':'另一页面确认保留这项练习'})
    c.etag=stale;c.request(path,{'taskIds':ids[::-1],'lockedIds':[],'reason':'旧页面试图覆盖顺序与锁定'},expected=412)
    fresh=c.request('/students/'+sid+'/plans/'+TODAY);assert next(t for t in fresh['tasks'] if t['id']==target)['locked']
    audit=c.request('/students/'+sid+'/weekly-summary')['adjustments'];assert len(audit)==1
    print('PASS 另一页面锁定后，旧页面调整返回412，不覆盖顺序/锁定或多记审计')
    c.request(path,{'taskIds':[i for i in ids if i!=target],'lockedIds':[],'reason':'试图直接移除已锁定可选题'},expected=422)
    for invalid in [{'taskIds':None,'lockedIds':[]},{'taskIds':ids,'lockedIds':None}]:c.request(path,invalid|{'reason':'无效任务清单'},expected=422)
    for reason in ['', 'x'*1001]:c.request(path,{'taskIds':ids,'lockedIds':[target],'reason':reason},expected=422)
    c.request(path,{'taskIds':ids,'lockedIds':[],'reason':'先解除可选练习锁定'})
    mandatory=c.request('/plans/'+draft['id']+'/tasks',{'title':'保留必做学校任务','minutes':1,'resourceRef':'完成规定作业','type':'Schoolwork','mandatory':True})
    fresh=c.request('/students/'+sid+'/plans/'+TODAY);all_ids=[t['id'] for t in fresh['tasks']];locked=[t['id'] for t in fresh['tasks'] if t['locked']]
    c.request(path,{'taskIds':[i for i in all_ids if i!=mandatory['id']],'lockedIds':[],'reason':'不能移除必做'},expected=422)
    reason='学校作业较多，明确减少这项可选练习';kept=[i for i in all_ids if i!=target];key=str(uuid.uuid4());body={'taskIds':kept,'lockedIds':locked,'reason':reason};changed=c.request(path,body,key=key);c.request(path,body,key=key)
    now=c.request('/students/'+sid+'/plans/'+TODAY);assert [t['id'] for t in now['tasks']]==kept and any(t['id']==mandatory['id'] and t['mandatory'] for t in now['tasks'])
    assert c.request('/students/'+sid+'/weekly-summary')['adjusted']['total']==len(original_tasks)
    c.request('/plans/'+changed['id']+':publish',{'previewHash':changed['inputHash']});report=c.request('/students/'+sid+'/weekly-summary');assert report['original']['total']==len(original_tasks) and report['adjusted']['total']==len(kept)
    matching=[a for a in report['adjustments'] if a['details']['reason']==reason];assert len(matching)==1 and matching[0]['details']['removedTaskIds']==[target]
    print('PASS 先解锁再移除，可选变化只影响草稿；必做保留，重复请求/明确发布后原因与原始分母完整')
    print('PASS 空白或超长调整原因拒绝')
if __name__=='__main__':main()
