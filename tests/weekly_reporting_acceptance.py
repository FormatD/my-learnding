"""Historical periods, published-only denominators, task histories and exact adjustment reasons."""
import json,uuid,urllib.request
from datetime import date,timedelta,datetime
from zoneinfo import ZoneInfo
from api_acceptance import Client,TODAY,BASE

def main():
    c=Client();password='private-'+uuid.uuid4().hex;c.request('/auth/register',{'userName':'weekly-'+uuid.uuid4().hex[:12],'password':password},expected=201);c.request('/me');sid=c.request('/students',{'name':'周报验收'},expected=201)['id']
    draft=c.request('/content/fixture',{});catalog=json.loads(draft['payload']);c.request('/content/drafts/'+draft['id']+':review',{});p=c.request('/content/drafts/'+draft['id']+'/preview');r=c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':p['hash']});c.request('/students/'+sid+'/content/'+r['id']+':bind',{})
    old_day=(date.fromisoformat(TODAY)-timedelta(days=10)).isoformat()
    c.request('/students/'+sid+'/school-progress/'+old_day+'/'+catalog['lessons'][0]['id'],{},method='PUT')
    rev=c.request('/students/'+sid+'/plans/'+old_day+':generate',{});manual=c.request('/plans/'+rev['id']+'/tasks',{'title':'真实原始固定活动','minutes':1,'resourceRef':'纸上看一道例题','mandatory':False,'type':'Resource'})
    rev=c.request('/students/'+sid+'/plans/'+old_day)['revision'];before=c.request('/students/'+sid+'/plans/'+old_day)['tasks'];c.request('/plans/'+rev['id']+':publish',{'previewHash':rev['inputHash']})
    c.request('/tasks/'+manual['id']+':transition',{'status':'InProgress'});c.request('/tasks/'+manual['id']+':transition',{'status':'Completed'})
    c.request('/students/'+sid+'/availability/'+old_day,{'minutes':20,'reserved':0},method='PUT');newrev=c.request('/students/'+sid+'/plans/'+old_day+':generate',{});items=c.request('/students/'+sid+'/plans/'+old_day)['tasks'];remove=next(t for t in items if t['id']!=manual['id'] and not t['mandatory'])
    kept=[t['id'] for t in items if t['id']!=remove['id']];locked=[t['id'] for t in items if t['locked'] and t['id'] in kept]
    c.request('/plans/'+newrev['id']+':adjust',{'taskIds':kept,'lockedIds':[str(uuid.uuid4())],'reason':'非法锁定'},expected=422)
    reason='学校作业较多，减少这次选题；原任务保留在历史'
    key=str(uuid.uuid4());adjust={'taskIds':kept,'lockedIds':locked,'reason':reason};newrev=c.request('/plans/'+newrev['id']+':adjust',adjust,key=key);c.request('/plans/'+newrev['id']+':adjust',adjust,key=key)
    report=c.request('/students/'+sid+'/weekly-summary?end='+old_day)
    assert report['original']['total']==len(before) and report['original']['completed']==1 and report['adjusted']['total']==len(before)
    assert len(report['adjustments'])==1 and not report['adjustments'][0]['published'] and all(p['revisionId']!=newrev['id'] for t in report['tasks'] for p in t['placements'])
    details=report['adjustments'][0]['details'];assert details['reason']==reason and details['beforeTaskIds']==[t['id'] for t in items] and details['afterTaskIds']==kept and details['removedTaskIds']==[remove['id']]
    c.request('/plans/'+newrev['id']+':publish',{'previewHash':newrev['inputHash']});published=c.request('/students/'+sid+'/weekly-summary?end='+old_day)
    assert published['adjusted']['total']==len(kept) and published['original']['total']==len(before) and published['adjustments'][0]['published']
    print('PASS 草稿调整不改变已发布分母；明确发布后原始任务、移除原因及一次幂等审计保留')
    c.request('/students/'+sid+'/availability/'+old_day,{'minutes':15,'reserved':0},method='PUT');third=c.request('/students/'+sid+'/plans/'+old_day+':generate',{});c.request('/plans/'+third['id']+':publish',{'previewHash':third['inputHash']})
    history=c.request('/students/'+sid+'/weekly-summary?end='+old_day)
    intermediate=[p for t in history['tasks'] for p in t['placements'] if p['revisionId']==newrev['id']];assert intermediate and all(not p['current'] for p in intermediate)
    assert sum(t['taskId']==manual['id'] for t in history['tasks'])==1 and len(next(t for t in history['tasks'] if t['taskId']==manual['id'])['placements'])==3
    final_tasks=c.request('/students/'+sid+'/plans/'+old_day)['tasks'];skip=next(t for t in final_tasks if t['status']=='Ready');skip_reason='孩子今日状态不佳，保留未完成记录'
    c.request('/tasks/'+skip['id']+':transition',{'status':'Skipped','reason':skip_reason});after_skip=c.request('/students/'+sid+'/weekly-summary?end='+old_day)
    assert any(a['details']['taskId']==skip['id'] and a['details']['to']=='Skipped' and a['details']['reason']==skip_reason for a in after_skip['transitions'])
    assert after_skip['original']['total']==len(before) and after_skip['adjusted']['completed']==1
    remaining=next(t for t in final_tasks if t['status']=='Ready' and t['id']!=skip['id']);target=(date.fromisoformat(old_day)+timedelta(days=2)).isoformat();defer_reason='家长确认后顺延这项任务'
    c.request('/tasks/'+remaining['id']+':defer',{'date':target,'reason':defer_reason});after_defer=c.request('/students/'+sid+'/weekly-summary?end='+old_day)
    deferred=next(a for a in after_defer['adjustments'] if a['details']['kind']=='TaskDeferral');assert deferred['published'] and deferred['details']['deferredTo']==target and deferred['details']['reason']==defer_reason and deferred['details']['removedTaskIds']==[remaining['id']]
    print('PASS 跳过理由与家长顺延具体原因可回看，不把未完成改成完成')
    print('PASS 三次发布保留中间版本任务，固定任务不重复计数')
    c.request('/students/'+sid+'/parent-burden',{'date':old_day,'category':'Daily','minutes':5},expected=201);c.request('/students/'+sid+'/parent-burden',{'date':TODAY,'category':'ContentReview','minutes':12},expected=201)
    current=c.request('/students/'+sid+'/weekly-summary');old=c.request('/students/'+sid+'/weekly-summary?end='+old_day)
    assert current['original']['total']==0 and current['parentBurden']['dailyMinutes']==0 and current['parentBurden']['contentReviewMinutes']==12
    assert old['parentBurden']['dailyMinutes']==5 and old['parentBurden']['contentReviewMinutes']==0 and old['currentDate']==TODAY
    empty=c.request('/students/'+sid+'/weekly-summary?end='+(date.fromisoformat(TODAY)-timedelta(days=90)).isoformat());assert empty['tasks']==[] and empty['adjustments']==[] and empty['original']['total']==0
    for end in [(date.fromisoformat(TODAY)+timedelta(days=1)).isoformat(),'0001-01-01']:c.request('/students/'+sid+'/weekly-summary?end='+end,expected=422)
    print('PASS 往周/本周计划及投入严格分期，空周保持缺口，未来/过早日期拒绝')
    zone='Pacific/Kiritimati';zone_day=datetime.now(ZoneInfo(zone)).date().isoformat();z=c.request('/students',{'name':'跨日时区验收','timeZone':zone},expected=201);zreport=c.request('/students/'+z['id']+'/weekly-summary?end='+zone_day);assert zreport['end']==zone_day and zreport['currentDate']==zone_day and zreport['timeZone']==zone
    spec=c.request('/openapi.json');assert 'WeeklySummary' in spec['components']['schemas']
    print('PASS 往周日期按学生时区确定，当前接口类型生成同源文档')
    other=Client();other.request('/auth/register',{'userName':'weekly-other-'+uuid.uuid4().hex[:10],'password':password},expected=201);other.request('/me');other.request('/students/'+sid+'/weekly-summary?end='+old_day,expected=404)
    exported=json.loads(c.http.open(urllib.request.Request(BASE+'/students/'+sid+'/export')).read());assert exported['studentAudit'] and all(a['studentId']==sid for a in exported['studentAudit'])
    delete=c.request('/students/'+sid+'/delete-preview');c.request('/students/'+sid+':delete',{'previewHash':delete['previewHash'],'password':password,'confirm':'永久删除学生'})
    assert not any(a.get('studentId')==sid for a in c.request('/audit'))
    print('PASS 学生导出保留具体说明；真实删除清除所属说明而非只隐藏周报')
    c.request('/students/'+z['id']+'/child-sessions',{});c.request('/students/'+sid+'/weekly-summary?end='+old_day,expected=403)
    print('PASS 往周查询同样执行家庭隔离与孩子权限')
if __name__=='__main__':main()
