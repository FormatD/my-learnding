"""Actual manual background rebuild, result provenance, reuse and private data controls."""
import io,json,time,uuid,zipfile
import api_acceptance
from api_acceptance import Client

def verify(c):
    c.request('/me');sid=c.request('/students',{'name':'后台重建接口验收'},expected=201)['id'];path='/students/'+sid
    c.request(path+'/mastery:rebuild',{'reason':''},expected=422)
    c.request(path+'/mastery:rebuild',{'reason':'x'*4001},expected=422)
    initial=c.request(path+'/assessment-consumption');assert initial=={'cursor':None,'pendingModern':0,'pendingLegacy':0,'legacyWithoutReceipt':0}
    first=c.request(path+'/mastery:rebuild',{'reason':'核对原始记录完整评估'},expected=202);assert first['status']=='Queued' and first['result'] is None and first['attemptCount']==0
    def settled(request):
        for _ in range(100):
            result=c.request(path+'/rebuild-requests/'+request['id'])
            if result['status']=='Succeeded':return result
            assert result['status'] not in ['Failed','Cancelled'],result
            time.sleep(.1)
        raise AssertionError('rebuild did not settle')
    applied=settled(first);actual=applied['result'];assert actual['generationId']==first['targetGenerationId'] and not actual['reusedGeneration'] and c.request(path+'/mastery')['generation']==actual['generationId']
    second=c.request(path+'/mastery:rebuild',{'reason':'核对同输入是否复用'},expected=202);reused=settled(second)['result'];assert reused['reusedGeneration'] and reused['generationId']==actual['generationId'] and second['targetGenerationId']!=actual['generationId']
    c.request(path+'/rebuild-requests/'+first['id']+':retry',{'reason':'重复恢复完成任务'},expected=409)
    other=Client();other.request('/auth/register',{'userName':'rebuild-other-'+uuid.uuid4().hex,'password':'rebuild-private-'+uuid.uuid4().hex},expected=201);other.request('/me');other.request(path+'/rebuild-requests',expected=404);other.request(path+'/rebuild-requests/'+first['id'],expected=404)
    individual=c.request(path+'/export');assert len(individual['rebuildRequests'])==2 and len(individual['rebuildResults'])==2 and len(individual['generations'])==1 and not individual['evidence']
    with c.http.open(api_acceptance.BASE+'/family/export') as r,zipfile.ZipFile(io.BytesIO(r.read())) as archive:data=json.loads(archive.read('manifest.json'))['data']
    for request in [first,second]:
        saved=next(r for r in data['AssessmentRebuildRequest'] if r['id']==request['id']);job=next(j for j in data['BackgroundJob'] if j['id']==request['jobId']);result=next(r for r in data['AssessmentRebuildResult'] if r['requestId']==request['id']);event=next(e for e in data['DomainEvent'] if e['id']==result['appliedEventId']);f=json.loads(saved['snapshot'])
        assert job['status']=='Succeeded' and job['type']=='AssessmentRebuild' and job['studentId']==sid and job['targetGenerationId']==saved['targetGenerationId']==f['targetGenerationId'] and job['inputPayload']==saved['snapshot'] and job['inputHash']==saved['snapshotHash'] and f['inputMode']=='LatestCommittedUnderLock'
        assert event['studentId']==sid and event['aggregateId']==result['generationId'] and json.loads(event['payload'])['data']['requestId']==request['id']
        assert any(a['jobId']==job['id'] and a['status']=='Succeeded' for a in data['JobLeaseAttempt'])
    c.request(path+'/mastery:full-rebuild',{'reason':''},expected=422)
    full=c.request(path+'/mastery:full-rebuild',{'reason':'明确从原始记录完整核对'},expected=202);complete=settled(full)['result'];assert not complete['reusedGeneration'] and complete['generationId']==full['targetGenerationId'] and complete['generationId']!=actual['generationId'] and complete['inputHash']==actual['inputHash']
    after=c.request(path+'/export');assert len(after['generations'])==2 and len(after['assessmentCheckpoints'])==2
    request=next(r for r in after['rebuildRequests'] if r['id']==full['id']);assert json.loads(request['snapshot'])['forceFull']
    current=next(g for g in after['generations'] if g['id']==complete['generationId']);assert current['calculationMode']=='FullStream' and current['processedInputCount']==0 and current['incrementalBaseGenerationId'] is None
    print('PASS explicit complete source rebuild creates real new result despite identical input hash, fixed force descriptor and private checkpoint export; original snapshots retained')
    c.request(path+'/child-sessions',{});c.request(path+'/rebuild-requests',expected=403);c.request(path+'/rebuild-requests/'+first['id'],expected=403);c.request(path+'/mastery:rebuild',{'reason':'孩子不能重建'},expected=403);c.request(path+'/mastery:full-rebuild',{'reason':'孩子不能完整重建'},expected=403)
    print('PASS actual 202 rebuild/result/event/claim links and exports, identical inputs reuse generation, completed retry rejected and family/child isolation')
