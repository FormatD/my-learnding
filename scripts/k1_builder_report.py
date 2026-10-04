#!/usr/bin/env python3
"""Report persisted candidate protocol and citation facts from a saved local Builder response."""
import argparse,hashlib,json
from pathlib import Path
from collections import Counter
from k1_evaluation import check,EvaluationError,digest,metric,write

def payload(value):
    check(isinstance(value,str),'PROTOCOL_RECORD_INVALID')
    try:return json.loads(value)
    except ValueError:raise EvaluationError('PROTOCOL_RECORD_INVALID') from None

def unique(rows):
    check(isinstance(rows,list) and all(isinstance(r,dict) and 'id' in r for r in rows),'RECORDS_INVALID');result={r['id']:r for r in rows};check(len(result)==len(rows),'RECORD_ID_DUPLICATE');return result

def report(state,run_ids):
    check(run_ids and len(set(run_ids))==len(run_ids),'RUN_SELECTION_INVALID');runs=unique(state['runs']);sources=unique(state['sources']);chunks=unique(state['chunks']);attempts=unique(state['attempts']);candidates=unique(state['candidates']);rows=[];all_candidates=[];first=repair=unknown=0
    for id in run_ids:
        check(id in runs,'RUN_NOT_FOUND');run=runs[id];check(run['type']=='Candidates' and run['provider']=='Mock' and run['model']=='fixture/1' and run['promptVersion'] in ('kc-candidate/1','kc-candidate/2'),'RUN_CONFIGURATION_UNSUPPORTED');check(run['status'] in ('Completed','Failed','Queued'),'RUN_STATUS_INVALID')
        family=run['familyId'];source=sources.get(run['sourceId']);check(source is not None and source['familyId']==family,'SOURCE_OWNER_MISMATCH')
        # Text uploads hash text. PDF sources hash the original file, which this response does not contain.
        source_check='OriginalFileHashNotRecomputed' if source.get('fileId') else 'TextHashVerified'
        if not source.get('fileId'):check(hashlib.sha256(source['text'].encode()).hexdigest()==source['hash'],'SOURCE_HASH_MISMATCH')
        owned=[a for a in attempts.values() if a['runId']==id];outputs=[c for c in candidates.values() if c['runId']==id];check(all(a['familyId']==family for a in owned) and all(c['familyId']==family for c in outputs),'RUN_OWNER_MISMATCH')
        completed=[a for a in owned if a['status']=='Completed'];record=None
        if run['status']=='Completed':
            check(len(completed)<=1,'COMPLETED_ATTEMPT_AMBIGUOUS')
            if completed and completed[0].get('protocolResult') is not None:
                record=payload(completed[0]['protocolResult']);check(isinstance(record,dict) and type(record.get('repaired')) is bool and type(record.get('calls')) is int and record['calls']==(2 if record['repaired'] else 1),'PROTOCOL_CALL_FACT_INVALID')
                envelope=record['output'];check(envelope['schemaVersion']==run['promptVersion'] and isinstance(envelope['candidates'],list),'PROTOCOL_VERSION_MISMATCH')
                protocols=[payload(c['protocolPayload']) for c in outputs];check(Counter(digest(p) for p in protocols)==Counter(digest(p) for p in envelope['candidates']),'PROTOCOL_CANDIDATE_MISMATCH')
                first+=not record['repaired'];repair+=record['repaired']
            else:unknown+=1
        else:check(not completed and not outputs,'NONCOMPLETED_OUTPUT_PRESENT')
        trace=[]
        for candidate in outputs:
            protocol=payload(candidate['protocolPayload']) if candidate.get('protocolPayload') is not None else None
            ids=protocol.get('sourceChunkIds') if isinstance(protocol,dict) else None;quotes=protocol.get('supportingQuotes') if isinstance(protocol,dict) else None
            if protocol is not None:
                check(run['promptVersion']!='kc-candidate/2' or (candidate['name']==protocol['name'] and candidate['type']==protocol['kcType'] and candidate['behavior']==protocol['measurableBehavior'] and candidate['boundary']==protocol['boundary']),'CANDIDATE_FIELDS_CHANGED')
            # Missing legacy protocol is reported unknown; never manufacture pairs from the display quote.
            known=protocol is not None;valid=False;refs=[]
            if known and isinstance(ids,list) and isinstance(quotes,list) and 1<=len(ids)<=10 and len(ids)==len(quotes) and len(set(ids))==len(ids):
                valid=ids[0]==candidate['chunkId'] and quotes[0]==candidate['quote']
                for chunk_id,quote in zip(ids,quotes):
                    chunk=chunks.get(chunk_id);ok=chunk is not None and chunk['familyId']==family and chunk['sourceId']==source['id'] and isinstance(quote,str) and bool(quote.strip()) and len(quote)<=1000 and quote in chunk['text'] and chunk['text'] in source['text'];valid=valid and ok
                    refs.append({'chunkId':chunk_id,'locator':chunk['locator'] if chunk else None,'chunkHash':hashlib.sha256(chunk['text'].encode()).hexdigest() if chunk else None,'quoteHash':hashlib.sha256(quote.encode()).hexdigest() if isinstance(quote,str) else None,'valid':ok})
            item={'candidateId':candidate['id'],'runId':id,'protocolHash':digest(protocol) if protocol else None,'sourceStatus':'VerifiedRecordedPairs' if valid else 'InvalidRecordedPairs' if known else 'UnknownLegacyProtocol','references':refs};trace.append(item);all_candidates.append(item)
        rows.append({'runId':id,'status':run['status'],'error':run.get('error'),'inputHash':run['inputHash'],'promptVersion':run['promptVersion'],'sourceId':source['id'],'sourceHash':source['hash'],'sourceHashCheck':source_check,'attempts':len(owned),'completedAttemptId':completed[0]['id'] if completed else None,'schemaEvidence':'RepairedProtocolAccepted' if record and record['repaired'] else 'FirstProtocolAccepted' if record else 'UnknownCompletedProtocol' if run['status']=='Completed' else 'NoCompletedProtocol','protocolResultHash':digest(record) if record else None,'candidateCount':len(trace)})
    statuses=Counter(r['status'] for r in rows);completed_count=statuses['Completed'];trace_known=sum(c['sourceStatus']!='UnknownLegacyProtocol' for c in all_candidates);valid=sum(c['sourceStatus']=='VerifiedRecordedPairs' for c in all_candidates)
    first_metric=metric(first,completed_count);repair_metric=metric(repair,completed_count);trace_metric=metric(valid,len(all_candidates))
    if unknown:first_metric['rate']=repair_metric['rate']=None
    if trace_known<len(all_candidates):trace_metric['rate']=None
    return {'format':'k1-builder-report/1','captureHash':digest(state),'selectionHash':digest(sorted(run_ids)),'provider':'Mock','recordVerification':'LocalSavedResponseChecksNotIndependentServerAttestation','formalV1ExitProven':False,'semanticQuality':'NotEvaluated','counts':{'selectedRuns':len(rows),'completed':completed_count,'failed':statuses['Failed'],'queued':statuses['Queued'],'completedProtocolUnknown':unknown,'firstAccepted':int(first),'repairAccepted':int(repair),'candidates':len(all_candidates),'candidateProtocolUnknown':len(all_candidates)-trace_known},'firstSchemaSuccess':first_metric,'repairedSchemaSuccess':repair_metric,'sourceTraceability':trace_metric,'runs':rows,'candidates':all_candidates,'notice':'Persisted successful protocol facts only; raw first responses are not independently revalidated. Unknown legacy facts suppress rates. No mapping accuracy, human gold, semantic quality, time, price or formal V1 claim.'}

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--builder',required=True);p.add_argument('--run-id',action='append',required=True);p.add_argument('--output',required=True);a=p.parse_args()
    try:
        result=report(json.loads(Path(a.builder).read_text()),a.run_id);write(a.output,result);print(json.dumps({'counts':result['counts'],'formalV1ExitProven':False}))
    except (EvaluationError,OSError,ValueError,KeyError,TypeError) as error:
        print(json.dumps({'errorCode':str(error) if isinstance(error,EvaluationError) else 'BUILDER_REPORT_INPUT_INVALID'}));raise SystemExit(1)
if __name__=='__main__':main()
