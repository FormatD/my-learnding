"""Candidate -> draft -> immutable published KC retains reviewed source provenance."""
import json
import time
import uuid
from api_acceptance import Client,TODAY

def main():
    c=Client();other=Client();password='private-'+uuid.uuid4().hex
    for client,prefix in [(c,'provenance'),(other,'isolated')]:client.request('/auth/register',{'userName':prefix+'-'+uuid.uuid4().hex[:12],'password':password},expected=201);client.request('/me')
    sid=c.request('/students',{'name':'来源追溯验收'},expected=201)['id']
    source=c.request('/content/sources',{'title':'家庭原创说明','text':'有括号的算式先算括号内部，再按运算顺序计算。'},expected=201)
    run=c.request('/builder/runs',{'sourceId':source['id']},expected=202)
    assert run['inputVersion']=='builder-input/2' and run['libraryReleaseId'] is None
    for _ in range(100):
        builder=c.request('/builder');candidates=[x for x in builder['candidates'] if x['runId']==run['id']]
        if candidates:break
        time.sleep(.1)
    candidate=candidates[0]
    c.request('/builder/candidates/'+candidate['id']+':decide',{'decision':'CreateDraft','reason':'未填真实定义'},expected=422)
    reviewed=c.request('/builder/candidates/'+candidate['id']+':decide',{'decision':'CreateDraft','reason':'家长核对原创文本和测量范围','name':'先计算小括号','behavior':'独立先算小括号内的加减，再计算除法','boundary':'仅一个小括号，不含嵌套，不推断建模能力'})
    assert reviewed['createdDraftId'] and reviewed['createdKCId'] and reviewed['reviewedBy'] and reviewed['reviewedAt']
    trace=c.request('/content/kcs/'+reviewed['createdKCId']+'/provenance')['citations'][0]
    assert trace['source']['id']==source['id'] and trace['citation']['quote'] in source['text'] and trace['citation']['locator']=='段落 1'
    assert trace['run']['inputHash']==run['inputHash'] and trace['review']['reviewReason']=='家长核对原创文本和测量范围'
    other.request('/content/kcs/'+reviewed['createdKCId']+'/provenance',expected=404)
    assert c.request('/students/'+sid+'/export')['contentProvenance']==[]
    print('PASS 候选审核留存来源、真实引文、定位、模型/输入版本、理由与操作者；跨家庭404')
    draft=next(d for d in c.request('/content')['drafts'] if d['id']==reviewed['createdDraftId']);catalog=json.loads(draft['payload']);kc=reviewed['createdKCId']
    catalog['questions']=[{'id':str(uuid.uuid4()),'revisionId':str(uuid.uuid4()),'stem':'(8 + 4) ÷ 3 = ?','answer':'4','explanation':'先算8+4=12，再算12÷3=4。','type':'Numeric','difficulty':'Medium','policy':'SingleKC','mappings':[{'kcId':kc,'role':'Primary','share':1,'mode':'WholeItem'}]}]
    catalog['lessons']=[{'id':str(uuid.uuid4()),'title':'小括号原创练习','sequence':1,'kcIds':[kc]}]
    c.request('/content/drafts/'+draft['id'],{'title':draft['title'],'catalog':catalog},method='PUT');c.request('/content/drafts/'+draft['id']+':review',{});p=c.request('/content/drafts/'+draft['id']+'/preview');release=c.request('/content/drafts/'+draft['id']+':publish',{'previewHash':p['hash']})
    assert c.request('/content/kcs/'+kc+'/provenance')['citations'][0]['createdDraftId']==draft['id']
    c.request('/students/'+sid+'/content/'+release['id']+':bind',{})
    export=c.request('/students/'+sid+'/export');assert any(t['kcId']==kc and t['source']['id']==source['id'] for t in export['contentProvenance'])
    print('PASS 草稿补题发布后仍可追溯，学生导出包含正式能力的引用记录')
    second=c.request('/builder/runs',{'sourceId':source['id']},expected=202)
    assert second['id']!=run['id'] and second['libraryReleaseId']==release['id']
    print('PASS 同一来源在不同内容库快照下建立独立任务，不复用旧输入结果')
    c.request('/students/'+sid+'/child-sessions',{})
    c.request('/content/kcs/'+kc+'/provenance',expected=403)
    print('PASS 孩子会话不能获取私有来源和审核记录')

if __name__=='__main__':main()
