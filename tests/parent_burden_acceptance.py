"""Real user-reported burden records; no inferred browser timing or usage days."""
import concurrent.futures,json,uuid,urllib.request,io,zipfile
from datetime import date,timedelta
from api_acceptance import Client,TODAY,BASE

def main():
    c=Client();password='private-'+uuid.uuid4().hex;c.request('/auth/register',{'userName':'burden-'+uuid.uuid4().hex[:12],'password':password},expected=201);c.request('/me')
    sid=c.request('/students',{'name':'家长投入验收'},expected=201)['id'];other_student=c.request('/students',{'name':'另一学生'},expected=201)['id']
    spec=c.request('/openapi.json');schema=spec['components']['schemas']['ParentBurdenSummary'];assert {'method','dailyMinutes','contentReviewMinutes','recordedDays','recordCount','records','history'}<=set(schema['properties'])
    assert c.request('/students/'+sid+'/weekly-summary')['parentBurden']['recordCount']==0
    data={'date':TODAY,'category':'Daily','minutes':7.5,'note':'核对作业'};key=str(uuid.uuid4())
    first=c.request('/students/'+sid+'/parent-burden',data,key=key,expected=201);repeat=c.request('/students/'+sid+'/parent-burden',data,key=key,expected=201);assert first['id']==repeat['id'] and first['method']=='ParentReported'
    c.request('/students/'+sid+'/parent-burden',dict(data,category='ContentReview',minutes=12),expected=201)
    w=c.request('/students/'+sid+'/weekly-summary')['parentBurden'];assert (w['dailyMinutes'],w['contentReviewMinutes'],w['recordedDays'],w['recordCount'])==(7.5,12,1,2)
    assert c.request('/students/'+other_student+'/parent-burden')['recordCount']==0
    print('PASS 无记录保持缺口，重复提交仅一条，日常/集中审核分开且学生隔离')
    for change in [{'minutes':0},{'minutes':240.1},{'minutes':1.11},{'date':(date.fromisoformat(TODAY)+timedelta(days=1)).isoformat()},{'date':(date.fromisoformat(TODAY)-timedelta(days=28)).isoformat()},{'category':'Inferred'},{'note':'x'*501}]:
        c.request('/students/'+sid+'/parent-burden',data|change,expected=422)
    c.request('/students/'+other_student+'/parent-burden',data|{'date':(date.fromisoformat(TODAY)-timedelta(days=27)).isoformat()},expected=201)
    print('PASS 未来/过期日期、无效分类、超长说明和伪造时长精度拒绝')
    correction=dict(data,minutes=3,reason='包含了休息时间，按真实投入更正');path='/parent-burden/'+first['id']+':correct'
    c.request(path,dict(correction,reason=''),expected=422)
    results=[]
    def correct():
        # Shared cookie identity, separate request opener and last etag.
        clone=Client();clone.jar=c.jar;clone.http=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(clone.jar));clone.etag=c.etag
        try:return clone.request(path,correction,expected=201)
        except AssertionError as e:assert '409' in str(e) or '412' in str(e);return None
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:results=list(pool.map(lambda _:correct(),range(2)))
    assert sum(r is not None for r in results)==1;latest=next(r for r in results if r)
    records=c.request('/students/'+sid+'/parent-burden');assert records['dailyMinutes']==3 and len(records['history'])==3 and records['recordCount']==2
    original=next(r for r in records['history'] if r['id']==first['id']);assert original['minutes']==7.5 and original['note']=='核对作业'
    c.request(path,correction,expected=409)
    print('PASS 更正必须有原因，并发只能一条替代，原始记录保持不变且不重复统计时长')
    key2=str(uuid.uuid4());change=dict(data,date=(date.fromisoformat(TODAY)-timedelta(days=10)).isoformat(),minutes=2,reason='实际投入日期为十天前')
    newer=c.request('/parent-burden/'+latest['id']+':correct',change,key=key2,expected=201);c.request('/parent-burden/'+latest['id']+':correct',change,key=key2,expected=201)
    w=c.request('/students/'+sid+'/weekly-summary')['parentBurden'];assert w['dailyMinutes']==0 and w['contentReviewMinutes']==12 and w['recordCount']==1
    all_records=c.request('/students/'+sid+'/parent-burden');assert all_records['dailyMinutes']==2 and len(all_records['history'])==4 and all_records['recordCount']==2
    raw=json.loads(c.http.open(urllib.request.Request(BASE+'/students/'+sid+'/export')).read());assert len(raw['parentBurden'])==4
    archive=zipfile.ZipFile(io.BytesIO(c.http.open(urllib.request.Request(BASE+'/family/export')).read()));manifest=json.loads(archive.read('manifest.json'));assert len(manifest['data']['ParentBurdenRecord'])==5
    print('PASS 更正到周外不计入本周；28天列表、学生导出及全家ZIP保留完整多次更正链')
    stranger=Client();stranger.request('/auth/register',{'userName':'burden-other-'+uuid.uuid4().hex[:10],'password':password},expected=201);stranger.request('/me')
    stranger.request('/students/'+sid+'/parent-burden',expected=404);stranger.request('/parent-burden/'+newer['id']+':correct',change,expected=404)
    print('PASS 跨家庭读取与更正均404')
    c.request('/students/'+sid+'/child-sessions',{})
    c.request('/students/'+sid+'/parent-burden',expected=403);c.request('/students/'+sid+'/parent-burden',data,expected=403)
    print('PASS 孩子不可读取或提交家长投入')
if __name__=='__main__':main()
