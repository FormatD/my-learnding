"""Real paper intake -> reviewed original question -> confirmed evidence -> correction."""
import json,time,uuid
from api_acceptance import Client,TODAY
from datetime import date,timedelta

def main():
    c=Client();other=Client();password='paper-private-'+uuid.uuid4().hex
    for client in [c,other]:client.request('/auth/register',{'userName':'paper-'+uuid.uuid4().hex[:12],'password':password},expected=201);client.request('/me')
    sid=c.request('/students',{'name':'纸质归因验收'},expected=201)['id'];d=c.request('/content/fixture',{});catalog=json.loads(d['payload'])
    def publish(draft):
        c.request('/content/drafts/'+draft['id']+':review',{});p=c.request('/content/drafts/'+draft['id']+'/preview');return c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':p['hash']})
    r1=publish(d);c.request('/students/'+sid+'/content/'+r1['id']+':bind',{})
    wrong=c.request('/students/'+sid+'/paper-wrongs',{'stem':'9 + 2 × 4 = ?','answer':'44','errorType':'Calculation'},expected=201);wid=wrong['id'];assert c.request('/students/'+sid+'/mastery')['masteries']==[]
    other.request('/paper-wrongs/'+wid,{'stem':'其他家庭','answer':'1'},method='PUT',expected=404)
    draft=c.request('/paper-wrongs/'+wid+'/draft',{'releaseId':r1['id'],'kcId':catalog['kcs'][0]['id'],'answer':'17','explanation':'先算2×4，再加9。','type':'Numeric'},expected=201)
    inp={'releaseId':r1['id'],'questionId':draft['questionId'],'result':'Incorrect','reason':'核对纸上原题与运算过程','sameQuestionConfirmed':True}
    c.request('/paper-wrongs/'+wid+'/preview',inp,expected=422)
    assert c.request('/students/'+sid+'/attempts')['attempts']==[]
    c.request('/paper-wrongs/'+wid+'/draft',{'releaseId':r1['id'],'kcId':catalog['kcs'][0]['id'],'answer':'17','explanation':'先乘后加'},expected=409)
    print('PASS 待归因和新建原题草稿均无证据；未发布题不能确认，重复草稿拒绝，跨家庭404')
    r2=publish(draft['draft']);c.request('/students/'+sid+'/content/'+r2['id']+':bind',{});inp['releaseId']=r2['id'];c.request('/paper-wrongs/'+wid+'/preview',{**inp,'sameQuestionConfirmed':False},expected=422)
    p=c.request('/paper-wrongs/'+wid+'/preview',inp)
    c.request('/paper-wrongs/'+wid,{'stem':wrong['stem'],'answer':'45','errorType':'Calculation'},method='PUT')
    c.request('/paper-wrongs/'+wid+'/confirm',{**inp,'previewHash':p['previewHash']},expected=412)
    p=c.request('/paper-wrongs/'+wid+'/preview',inp);key=str(uuid.uuid4());payload={**inp,'previewHash':p['previewHash']};confirmed=c.request('/paper-wrongs/'+wid+'/confirm',payload,key=key,expected=202)
    again=c.request('/paper-wrongs/'+wid+'/confirm',payload,key=key,expected=202);assert confirmed['attemptId']==again['attemptId']
    c.request('/paper-wrongs/'+wid+'/confirm',payload,expected=409)
    c.request('/paper-wrongs/'+wid,{'stem':wrong['stem'],'answer':'17'},method='PUT',expected=409)
    def settle():
        for _ in range(100):
            m=c.request('/students/'+sid+'/mastery')
            if m['pending']==0:return m
            time.sleep(.1)
        raise AssertionError('projection did not settle')
    m=settle();assert len(m['masteries'])==1 and m['masteries'][0]['beta']==3
    attempts=c.request('/students/'+sid+'/attempts')['attempts'];assert len(attempts)==1 and attempts[0]['answer']=='45' and attempts[0]['answerSource']=='ParentPaperConfirmed'
    reviews=c.request('/students/'+sid+'/reviews');assert len(reviews)==1 and reviews[0]['targetId']==draft['questionId'] and reviews[0]['stage']=='R1'
    due=(date.fromisoformat(TODAY)+timedelta(days=2)).isoformat();c.request('/students/'+sid+'/plans/'+due+':generate',{});tasks=c.request('/students/'+sid+'/plans/'+due)['tasks'];assert any(t['questionId']==draft['questionId'] and t['type']=='Review' for t in tasks)
    binding=next(b for b in c.request('/content/releases/'+r2['id']+'/mapping-sets')['bindings'] if b['ownerType']=='Question' and b['ownerId']==draft['questionId'])
    assert attempts[0]['mappingSetRevisionId']==binding['setRevisionId'] and attempts[0]['questionRevisionId']==next(q['revisionId'] for q in json.loads(r2['payload'])['questions'] if q['id']==draft['questionId'])
    assert c.request('/students/'+sid+'/mastery/'+m['masteries'][0]['kcId'])['evidence'][0]['mappingSetRevisionId']==binding['setRevisionId']
    assert confirmed['confirmedBy'] and confirmed['confirmedAt'] and confirmed['releaseId']==r2['id']
    print('PASS 原题明确确认、失效预览拒绝、幂等重试不双计；一条可信首次作答及原题R1日程')
    aid=confirmed['attemptId'];grade={'result':'Correct','reason':'重新核对纸质答案，录入误判'};p=c.request('/attempts/'+aid+'/grading-preview',grade);c.request('/attempts/'+aid+'/grading-revisions',{**grade,'previewHash':p['previewHash']},expected=202);m=settle();assert m['masteries'][0]['beta']==2 and m['masteries'][0]['alpha']==3
    assert not any(r['targetType']=='WrongQuestion' for r in c.request('/students/'+sid+'/reviews'))
    export=c.request('/students/'+sid+'/export');assert export['paperWrongs'][0]['attemptId']==aid and export['attempts'][0]['answer']=='45' and len(export['gradings'])==2
    assert export['sessions'][0]['mappingSetRevisionId']==binding['setRevisionId'] and any(x['id']==binding['setRevisionId'] for x in export['mappingSets'])
    assert any(x['setRevisionId']==binding['setRevisionId'] for x in export['mappingItems'])
    print('PASS 纸质代录支持判分更正与世代重放，原始答案和审核记录导出完整')
    c.request('/students/'+sid+'/child-sessions',{});c.request('/paper-wrongs/'+wid+'/preview',inp,expected=403);c.request('/students/'+sid+'/paper-wrongs',expected=403)
    print('PASS 孩子不能读取私有纸质记录或代录结果')
if __name__=='__main__':main()
