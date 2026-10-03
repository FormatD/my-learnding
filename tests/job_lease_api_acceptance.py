"""Actual durable job/claim records, private export and editor boundaries."""
import hashlib,io,json,time,zipfile
import api_acceptance
def verify(c):
    c.request('/background-jobs?page=0',expected=422);c.request('/background-jobs?pageSize=51',expected=422)
    for _ in range(100):
        result=c.request('/background-jobs?pageSize=50')
        if any(j['status']=='Succeeded' and j['type']=='BuilderCandidates' for j in result['jobs']):break
        time.sleep(.05)
    else:raise AssertionError('actual builder job receipt not visible')
    family=c.request('/me')['family']['id']
    for job in result['jobs']:
        assert job['familyId']==family and hashlib.sha256(job['inputPayload'].encode()).hexdigest()==job['inputHash']
        assert job['status'] in ['Queued','Running','Succeeded','Retrying','Failed','Cancelled'] and 0<=job['attemptCount']<=job['maxAttempts']
        if job['status']=='Running':assert job['leaseOwner'] and job['leaseExpiresAt'] and job['heartbeatAt']
        else:assert job['leaseOwner'] is None and job['leaseExpiresAt'] is None
    completed=next(j for j in result['jobs'] if j['status']=='Succeeded' and j['type']=='BuilderCandidates')
    assert any(a['jobId']==completed['id'] and a['status']=='Succeeded' for a in result['attempts'])
    with c.http.open(api_acceptance.BASE+'/family/export') as response,zipfile.ZipFile(io.BytesIO(response.read())) as archive:data=json.loads(archive.read('manifest.json'))['data']
    assert any(j['id']==completed['id'] for j in data['BackgroundJob']) and any(a['jobId']==completed['id'] for a in data['JobLeaseAttempt'])
    print('PASS actual builder durable job, frozen input hash, fenced success receipt and full private export')
