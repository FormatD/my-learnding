"""Read actual assessment receipts and verify their private event/job/result references."""
import io,json,time,zipfile
import api_acceptance
def verify(c,sid):
    c.request('/students/'+sid+'/consumer-receipts?page=0',expected=422)
    for _ in range(100):
        page=c.request('/students/'+sid+'/consumer-receipts')
        if page['total']:break
        time.sleep(.05)
    else:raise AssertionError('real assessment receipt not visible')
    with c.http.open(api_acceptance.BASE+'/family/export') as response,zipfile.ZipFile(io.BytesIO(response.read())) as archive:data=json.loads(archive.read('manifest.json'))['data']
    c.request('/domain-events?page=0',expected=422)
    journal=c.request('/domain-events?studentId='+sid+'&pageSize=50')
    assert journal['total'] and all(e['studentId']==sid for e in journal['events'])
    assert [e['eventSequence'] for e in journal['events']]==sorted([e['eventSequence'] for e in journal['events']],reverse=True)
    assert {'AttemptSubmitted','GradingConfirmed','AssessmentApplied','CorrectionConfirmed','TaskTransitioned','ProgressChanged','ContentReleasePublished'} <= {e['eventType'] for e in data['DomainEvent']}
    individual=c.request('/students/'+sid+'/export')
    for row in page['receipts']:
        assert row['studentId']==sid and row['consumerName']=='assessment/1'
        event=next(o for o in data['Outbox'] if o['id']==row['eventId']);job=next(j for j in data['BackgroundJob'] if j['id']==row['jobId']);gen=next(g for g in data['Generation'] if g['id']==row['generationId'])
        if event['domainEventId']:
            fact=next(e for e in data['DomainEvent'] if e['id']==event['domainEventId'])
            envelope=json.loads(fact['payload'])
            assert fact['studentId']==sid and fact['aggregateId']==event['attemptId'] and fact['dispatchTarget']=='assessment/1' and row['domainEventId']==fact['id'] and envelope['eventType']==fact['eventType']
            assert any(e['id']==fact['id'] for e in individual['domainEvents'])
        assert event['studentId']==sid and event['processedAt'] and job['studentId']==sid and job['targetGenerationId'] and gen['studentId']==sid and gen['inputHash']==row['inputHash']
        assert any(r['id']==row['id'] for r in data['ConsumerReceipt'])
        assert any(r['id']==row['id'] for r in individual['consumerReceipts']) and any(o['id']==row['eventId'] for o in individual['outbox']) and any(j['id']==row['jobId'] for j in individual['backgroundJobs'])
    print('PASS real assessment receipts bind private event/student/job/result hash and export without inventing prior history')
