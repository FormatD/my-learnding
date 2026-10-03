"""Actual queued mapping work, frozen inputs and private job/result references."""
import io,json,time,uuid,zipfile,subprocess
import api_acceptance
from api_acceptance import Client

def verify(c,env,owner_credentials):
    library=next(r for r in c.request('/content')['releases'] if not r['withdrawn'] and all(x.get('revisionId') for x in json.loads(r['payload'])['resources']+json.loads(r['payload'])['lessons']))
    catalog=json.loads(library['payload']);source=c.request('/content/drafts',{'title':'后台映射接口验收','catalog':catalog});catalog=json.loads(source['payload'])
    owners=[{'ownerType':kind,'ownerId':row['id'],'ownerRevisionId':row['revisionId']} for kind,row in [('Question',catalog['questions'][0]),('Lesson',catalog['lessons'][0]),('Resource',catalog['resources'][0])]]
    request={'draftId':source['id'],'libraryReleaseId':library['id'],'owners':owners,'provider':'Mock'}
    c.request('/builder/mapping-preparations',{**request,'provider':'Cloud'},expected=422)
    c.request('/builder/mapping-preparations',{**request,'owners':owners+[owners[0]]},expected=422)
    p=c.request('/builder/mapping-preparations',request,expected=202);assert p['status']=='Queued' and p['attemptCount']==0
    assert c.request('/builder/mapping-preparations',request,expected=202)['id']==p['id']
    other=Client();other.request('/auth/register',{'userName':'mapping-queue-'+uuid.uuid4().hex,'password':'private-mapping-queue-'+uuid.uuid4().hex},expected=201);other.request('/me');other.request('/builder/mapping-preparations/'+p['id'],expected=404);assert not other.request('/builder/mapping-preparations')
    for _ in range(100):
        actual=c.request('/builder/mapping-preparations/'+p['id'])
        if actual['status']=='Succeeded':break
        assert actual['status'] not in ['Failed','Cancelled'],actual
        time.sleep(.1)
    else:raise AssertionError('mapping queue did not settle')
    detail=c.request('/builder/mapping-runs/'+p['runId']);assert len(detail['suggestions'])==3 and not detail['decisions'] and detail['run']['sourcePayload']==source['payload']
    job=next(j for j in c.request('/background-jobs')['jobs'] if j['id']==p['jobId']);assert job['type']=='MappingSuggestions' and job['status']=='Succeeded' and job['inputRef']==p['id']
    c.request('/builder/mapping-preparations/'+p['id']+':retry',{'reason':'不允许重复已完成任务'},expected=409)
    editor_credentials={'userName':'mapping-recovery-'+uuid.uuid4().hex,'password':'mapping-private-recovery-'+uuid.uuid4().hex}
    editor_member=c.request('/family/members',{**editor_credentials,'roles':['ContentEditor']},expected=201)
    editor=Client();editor.request('/auth/login',editor_credentials);editor.request('/me')
    owner_id=c.request('/me')['family']['ownerAccountId'];owner_membership=next(m for m in c.request('/family/members') if m['accountId']==owner_id)
    c.request('/family/members/'+owner_membership['id']+'/roles',{'roles':['Parent'],'reason':'隔离验收：原请求人撤销内容维护权限'},method='PUT')
    c.request('/auth/login',owner_credentials);c.request('/me');c.request('/builder/mapping-preparations',expected=403)
    # Controlled terminal-state gap exercises actual manual recovery; prior output and claim history remain real.
    assert env['PGDATABASE'].startswith('learning_fault_openapi_')
    job_id=str(uuid.UUID(p['jobId']))
    subprocess.run(['psql','-v','ON_ERROR_STOP=1','-c',f"""UPDATE "BackgroundJob" SET "Status"='Failed',"LastErrorCode"='JOB_REQUESTER_FORBIDDEN',"NextRunAt"=NULL WHERE "Id"='{job_id}' AND "Type"='MappingSuggestions' AND "Status"='Succeeded'"""],env=env,check=True,stdout=subprocess.DEVNULL)
    editor.request('/builder/mapping-preparations/'+p['id']+':retry',{'reason':''},expected=422)
    editor.request('/builder/mapping-preparations/'+p['id']+':retry',{'reason':'有权限成员核对冻结输入后恢复受控状态缺口'},expected=202)
    for _ in range(100):
        recovered=editor.request('/builder/mapping-preparations/'+p['id'])
        if recovered['status']=='Succeeded':break
        assert recovered['status']!='Failed',recovered
        time.sleep(.1)
    else:raise AssertionError('authorized manual mapping recovery did not settle')
    assert recovered['runId']==p['runId'] and recovered['retryRound']==1
    assert editor.request('/builder/mapping-runs/'+p['runId'])['suggestions']==detail['suggestions']
    with c.http.open(api_acceptance.BASE+'/family/export') as r,zipfile.ZipFile(io.BytesIO(r.read())) as archive:data=json.loads(archive.read('manifest.json'))['data']
    saved=next(x for x in data['MappingPreparation'] if x['id']==p['id']);f=json.loads(saved['snapshot']);assert f['runId']==p['runId'] and f['sourcePayload']==source['payload'] and saved['snapshot']==job['inputPayload'] and saved['snapshotHash']==job['inputHash']
    assert len([x for x in data['JobLeaseAttempt'] if x['jobId']==p['jobId'] and x['status']=='Succeeded'])==2
    assert saved['requestedBy']==owner_id and any(a['action']=='MappingPreparationManualRetry' and a['actorId']==editor_member['accountId'] and json.loads(a['details'])['snapshotHash']==saved['snapshotHash'] for a in data['Audit'])
    print('PASS controlled terminal gap recovered by real authorized member: original requester/input/output remain unchanged, actual retry authorization and claims retained')
    student=c.request('/students',{'name':'映射后台权限'},expected=201);c.request('/students/'+student['id']+'/child-sessions',{});c.request('/builder/mapping-preparations',expected=403);c.request('/builder/mapping-preparations/'+p['id'],expected=403);c.request('/builder/mapping-preparations',request,expected=403)
    print('PASS queued mapping preparation fixes original inputs, reuses request, commits job/output together, exports provenance and rejects child/other family')
