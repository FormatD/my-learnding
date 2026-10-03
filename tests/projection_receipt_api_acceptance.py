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
        assert event['studentId']==sid and event['processedAt'] and job['studentId']==sid and job['targetGenerationId'] and gen['studentId']==sid
        checkpoint=next(x for x in data['AssessmentCheckpoint'] if x['id']==row['checkpointId']);assert checkpoint['studentId']==sid and checkpoint['familyId']==row['familyId'] and checkpoint['generationId']==gen['id'] and checkpoint['inputHash']==row['inputHash']
        assert any(r['id']==row['id'] for r in data['ConsumerReceipt'])
        assert any(r['id']==row['id'] for r in individual['consumerReceipts']) and any(o['id']==row['eventId'] for o in individual['outbox']) and any(j['id']==row['jobId'] for j in individual['backgroundJobs'])
    status=c.request('/students/'+sid+'/assessment-consumption');cursor=status['cursor'];assert cursor and status['pendingModern']==0
    source=next(e for e in data['DomainEvent'] if e['id']==cursor['lastEventId']);receipt=next(r for r in data['ConsumerReceipt'] if r['id']==cursor['lastReceiptId']);applied=next(e for e in data['DomainEvent'] if e['id']==cursor['lastAppliedEventId']);advance=json.loads(applied['payload'])['data']['consumption']
    assert source['studentId']==sid and source['dispatchTarget']=='assessment/1' and source['eventSequence']==cursor['lastEventSequence'] and receipt['eventId']==source['id'] and receipt['inputHash']==cursor['inputHash'] and applied['eventType']=='AssessmentApplied' and applied['aggregateId']==cursor['generationId']
    assert advance['version']=='assessment-consumption/1' and advance['throughSequence']==cursor['lastEventSequence'] and advance['lastReceiptId']==receipt['id'] and receipt['id'] in advance['verifiedReceiptIds']
    assert any(x==cursor for x in individual['assessmentConsumerCursors']) and any(x==cursor for x in data['AssessmentConsumerCursor'])
    print('PASS real assessment receipts bind private event/student/job/result hash and export without inventing prior history')
