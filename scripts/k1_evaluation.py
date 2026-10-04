#!/usr/bin/env python3
"""Freeze local reference evaluation inputs; score explicitly supplied labels and results."""
import argparse,copy,hashlib,json,math,re,unicodedata
from collections import Counter,defaultdict
from datetime import datetime
from pathlib import Path

class EvaluationError(Exception):pass

def check(value,code):
    if not value:raise EvaluationError(code)
def digest(value):return hashlib.sha256(json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':'),allow_nan=False).encode()).hexdigest()
def seal(value):return {**value,'hash':digest(value)}
def verify(value,format_):
    check(isinstance(value,dict) and value.get('format')==format_,'FORMAT_INVALID');check(value.get('hash')==digest({k:v for k,v in value.items() if k!='hash'}),'FROZEN_HASH_MISMATCH')
def template(stem):return re.sub(r'\s+','',re.sub(r'\d+','#',unicodedata.normalize('NFKC',stem)))
def freeze(catalog,source_bytes):
    qs=catalog['questions'];check(len(qs)>0 and len({q['id'] for q in qs})==len(qs),'QUESTION_ID_INVALID')
    parent={q['id']:q['id'] for q in qs}
    def find(x):
        while parent[x]!=x:parent[x]=parent[parent[x]];x=parent[x]
        return x
    buckets={}
    for q in qs:
        check(q.get('variantGroupId') and isinstance(q.get('stem'),str),'VARIANT_REFERENCE_REQUIRED')
        for key in [('variant',q['variantGroupId']),('template',template(q['stem']))]:
            if key in buckets:parent[find(q['id'])]=find(buckets[key])
            else:buckets[key]=q['id']
    groups=defaultdict(list)
    for q in qs:groups[find(q['id'])].append(q)
    ordered=sorted(groups.values(),key=lambda group:digest(sorted(q['id'] for q in group)))
    check(len(ordered)>=2,'INDEPENDENT_GROUPS_REQUIRED');held=max(1,math.ceil(len(ordered)*.3));holdout={q['id'] for group in ordered[:held] for q in group}
    items=[]
    for q in qs:
        input_=copy.deepcopy(q);input_.pop('mappings',None)
        items.append({'id':q['id'],'revisionId':q['revisionId'],'variantGroupId':q['variantGroupId'],'templateKey':template(q['stem']),'partition':'Holdout' if q['id'] in holdout else 'Development','sourceQuestionHash':digest(q),'input':input_,'inputHash':digest(input_)})
    result=seal({'format':'k1-evaluation/1','version':'mixed-operations-draft/1','referenceStatus':'AuthoredDraftNotHumanGold','sourceSha256':hashlib.sha256(source_bytes).hexdigest(),'groupPolicy':'VariantIdAndExactNumberNormalizedStem/1','library':catalog['kcs'],'items':items})
    validate_dataset(result);return result

def validate_dataset(data):
    verify(data,'k1-evaluation/1');check(data.get('referenceStatus')=='AuthoredDraftNotHumanGold','REFERENCE_STATUS_INVALID');items=data['items'];check(items and len({i['id'] for i in items})==len(items),'QUESTION_ID_INVALID')
    check(len({k['id'] for k in data['library']})==len(data['library']),'LIBRARY_ID_INVALID');seen={}
    for item in items:
        check(item['partition'] in ('Development','Holdout') and item['inputHash']==digest(item['input']) and 'mappings' not in item['input'],'INPUT_INVALID')
        check(item['templateKey']==template(item['input']['stem']) and item['id']==item['input']['id'] and item['revisionId']==item['input']['revisionId'] and item['variantGroupId']==item['input']['variantGroupId'],'INPUT_IDENTITY_INVALID')
        for key in [('variant',item['variantGroupId']),('template',item['templateKey'])]:
            check(key not in seen or seen[key]==item['partition'],'VARIANT_LEAKAGE');seen[key]=item['partition']
    check({i['partition'] for i in items}=={'Development','Holdout'},'PARTITION_REQUIRED')

def label_template(data,catalog):
    questions={q['id']:q for q in catalog['questions']}
    return {'format':'k1-labels/1','datasetHash':data['hash'],'referenceStatus':'PendingHumanReview','labels':[{'id':i['id'],'inputHash':i['inputHash'],'status':'Pending','validItem':None,'measurable':None,'primaryKCIds':None,'reviewer':None,'reviewedAt':None,'reason':None,'draftReferencePrimaryKCIds':sorted({m['kcId'] for m in questions[i['id']]['mappings'] if m['role']=='Primary' and m['mode'] in ('WholeItem','StepObserved') and m['share']>0})} for i in data['items']]}

def timestamp(value):
    check(isinstance(value,str),'REVIEW_TIMESTAMP_REQUIRED')
    try:parsed=datetime.fromisoformat(value.replace('Z','+00:00'))
    except ValueError:raise EvaluationError('REVIEW_TIMESTAMP_INVALID') from None
    check(parsed.tzinfo is not None,'REVIEW_TIMESTAMP_TIMEZONE_REQUIRED')
def text(value,code):check(isinstance(value,str) and bool(value.strip()),code)
def number(value,code):check(type(value) in (int,float) and math.isfinite(value) and value>=0,code)
def metric(n,d):return {'numerator':n,'denominator':d,'rate':n/d if d else None}

def evaluate(data,labels=None,results=None,partition='Holdout'):
    validate_dataset(data);check(partition in ('Development','Holdout'),'PARTITION_INVALID');selected={i['id']:i for i in data['items'] if i['partition']==partition};known={k['id'] for k in data['library']};allitems={i['id']:i for i in data['items']}
    approved={};pending=len(selected);labelhash=None
    if labels is not None:
        check(labels.get('format')=='k1-labels/1' and labels.get('datasetHash')==data['hash'],'LABELS_DATASET_MISMATCH');labelhash=digest(labels);ids=set()
        for row in labels['labels']:
            check(row['id'] in allitems and row['id'] not in ids,'LABEL_ID_INVALID');ids.add(row['id']);check(row.get('inputHash')==allitems[row['id']]['inputHash'],'LABEL_INPUT_CHANGED');check(row['status'] in ('Pending','Approved'),'LABEL_STATUS_INVALID')
            if row['status']=='Pending':continue
            text(row.get('reviewer'),'REVIEWER_REQUIRED');timestamp(row.get('reviewedAt'));text(row.get('reason'),'REVIEW_REASON_REQUIRED');check(type(row.get('validItem')) is bool and type(row.get('measurable')) is bool,'LABEL_ELIGIBILITY_REQUIRED')
            check(row['validItem'] or not row['measurable'],'INVALID_ITEM_MEASURABLE');targets=row.get('primaryKCIds');check(isinstance(targets,list) and len(set(targets))==len(targets) and set(targets)<=known,'GOLD_KC_INVALID');check((row['validItem'] and row['measurable'] and len(targets)>0) or (not row['validItem'] or not row['measurable']) and not targets,'GOLD_MEASUREMENT_INVALID')
            if row['id'] in selected:approved[row['id']]=row
        pending=len(selected)-len(approved)
    predictions={};result_hash=None;producer=None
    if results is not None:
        check(results.get('format')=='k1-results/1' and results.get('datasetHash')==data['hash'] and results.get('partition')==partition,'RESULTS_DATASET_MISMATCH');result_hash=digest(results);producer=results.get('producer');check(isinstance(producer,dict) and producer.get('kind') in ('Controlled','Mock','UserSuppliedProvider'),'PRODUCER_REQUIRED')
        for row in results['predictions']:
            check(row['id'] in selected and row['id'] not in predictions,'RESULT_ID_OR_PARTITION_INVALID');check(row.get('inputHash')==selected[row['id']]['inputHash'],'RESULT_INPUT_CHANGED');check(row['status'] in ('Mapped','Rejected','Skipped'),'RESULT_STATUS_INVALID');targets=row['primaryKCIds'];associations=row['mappedKCIds'];check(isinstance(targets,list) and len(set(targets))==len(targets) and set(targets)<=known and isinstance(associations,list) and len(set(associations))==len(associations) and set(associations)<=known and set(targets)<=set(associations),'PREDICTION_KC_INVALID');check((row['status']=='Mapped' and bool(associations)) or row['status']!='Mapped' and not targets and not associations,'PREDICTION_MAPPING_INVALID')
            predictions[row['id']]=row
    valid={id:row for id,row in approved.items() if row['validItem']};measurable={id:row for id,row in valid.items() if row['measurable']};mapped={id for id,row in predictions.items() if row['status']=='Mapped'};correct={id for id,gold in measurable.items() if id in mapped and set(predictions[id]['primaryKCIds'])==set(gold['primaryKCIds'])};by_type={}
    for kind in sorted({i['input']['type'] for i in selected.values()}):
        ids={id for id in measurable if selected[id]['input']['type']==kind};by_type[kind]={'primaryAccuracy':metric(len(ids&correct),len(ids)),'mappingCoverage':metric(len({id for id in valid if selected[id]['input']['type']==kind}&mapped),sum(selected[id]['input']['type']==kind for id in valid))}
    # Missing/rejected/skipped predictions count as incorrect and uncovered; no silent denominator shrink.
    report={'format':'k1-report/1','datasetHash':data['hash'],'labelsHash':labelhash,'resultsHash':result_hash,'partition':partition,'referenceStatus':data['referenceStatus'],'producer':producer,'evidenceStatus':'UserSuppliedRecordsNotIndependentlyVerified','formalV1ExitProven':False,'selectedItems':len(selected),'approvedItems':len(approved),'pendingItems':pending,'predictionItems':len(predictions),'missingPredictionItems':len(selected)-len(predictions),'primaryAccuracy':metric(len(correct),len(measurable)) if labels else None,'mappingCoverage':metric(len(set(valid)&mapped),len(valid)) if labels else None,'byQuestionType':by_type if labels else {},'sourceTraceability':None,'firstSchemaSuccess':None,'repairedSchemaSuccess':None,'reviewMinutesPer100':None,'duplicateRate':None,'orphanRate':None,'goldComplete':bool(labels) and pending==0,'qualityGate':{'status':'NotEvaluated','primaryAccuracyTarget':.85,'firstSchemaTarget':.95,'duplicateRateTarget':.05,'reviewMinutesTarget':30,'traceabilityTarget':1}}
    if results is None:return report
    candidates=results.get('candidates',[]);trace_ok=0
    for row in candidates:
        check(row['questionId'] in selected,'CITATION_PARTITION_INVALID');citations=row.get('citations',[]);ok=bool(citations)
        for citation in citations:
            item=selected[row['questionId']];quote=citation.get('quote');ok=ok and citation.get('inputHash')==item['inputHash'] and citation.get('locator')=='question:'+item['id'] and isinstance(quote,str) and bool(quote.strip()) and quote in item['input']['stem']
        trace_ok+=int(ok)
    report['sourceTraceability']=metric(trace_ok,len(candidates))
    tasks=results.get('tasks',[]);taskids=set();completed=[]
    for task in tasks:
        text(task.get('id'),'TASK_ID_REQUIRED');check(task['id'] not in taskids,'TASK_ID_DUPLICATE');taskids.add(task['id']);check(task['status'] in ('Completed','Failed'),'TASK_STATUS_INVALID');check(type(task.get('firstSchemaPassed')) is bool and type(task.get('repairedSchemaPassed')) is bool,'TASK_SCHEMA_FACT_REQUIRED');check(not task['firstSchemaPassed'] or not task['repairedSchemaPassed'],'TASK_REPAIR_FACT_CONFLICT')
        if task['status']=='Completed':check(task['firstSchemaPassed'] or task['repairedSchemaPassed'],'COMPLETED_TASK_WITHOUT_VALID_SCHEMA');completed.append(task)
    report['taskCounts']={'completed':len(completed),'failed':len(tasks)-len(completed),'total':len(tasks)};report['firstSchemaSuccess']=metric(sum(t['firstSchemaPassed'] for t in completed),len(completed));report['repairedSchemaSuccess']=metric(sum(t['repairedSchemaPassed'] for t in completed),len(completed))
    times=[]
    for id in valid:
        review=predictions.get(id,{}).get('review')
        if review is None:continue
        text(review.get('reviewer'),'TIMING_REVIEWER_REQUIRED');timestamp(review.get('recordedAt'));number(review.get('initialSeconds'),'INITIAL_TIME_INVALID');number(review.get('correctionSeconds'),'CORRECTION_TIME_INVALID');times.append(review['initialSeconds']+review['correctionSeconds'])
    if valid and len(times)==len(valid):report['reviewMinutesPer100']={'seconds':sum(times),'items':len(valid),'minutesPer100':sum(times)/60/len(valid)*100,'basis':'ExplicitFirstReviewAndCorrectionTiming'}
    audit=results.get('libraryAudit')
    if audit is not None:
        text(audit.get('reviewer'),'LIBRARY_REVIEWER_REQUIRED');timestamp(audit.get('reviewedAt'));text(audit.get('reason'),'LIBRARY_REVIEW_REASON_REQUIRED');nodes=audit['nodes'];check(len({n['id'] for n in nodes})==len(nodes),'LIBRARY_NODE_DUPLICATE');measured=[]
        for node in nodes:
            check(node['nodeKind'] in ('Measurable','Directory') and type(node['published']) is bool and type(node['redundantDuplicate']) is bool,'LIBRARY_NODE_INVALID');refs=node['formalAssociations'];check(isinstance(refs,list) and all(r.get('type') in ('Question','Lesson','Resource') and isinstance(r.get('id'),str) and r['id'] for r in refs),'LIBRARY_ASSOCIATION_INVALID')
            if node['published'] and node['nodeKind']=='Measurable':measured.append(node)
        report['duplicateRate']=metric(sum(n['redundantDuplicate'] for n in measured),len(measured));report['orphanRate']=metric(sum(not n['formalAssociations'] for n in measured),len(measured))
    complete=report['goldComplete'] and report['primaryAccuracy']['denominator']>0 and report['firstSchemaSuccess']['denominator']>0 and report['sourceTraceability']['denominator']>0 and report['duplicateRate'] is not None and report['duplicateRate']['denominator']>0 and report['reviewMinutesPer100'] is not None
    if complete:
        passed=report['primaryAccuracy']['rate']>=.85 and report['firstSchemaSuccess']['rate']>=.95 and report['sourceTraceability']['rate']==1 and report['duplicateRate']['rate']<=.05 and report['reviewMinutesPer100']['minutesPer100']<=30
        report['qualityGate']['status']='SuppliedRecordsMeetTargets' if passed else 'SuppliedRecordsBelowTargets'
    return report

def write(path,value):
    path=Path(path);check(not path.exists(),'OUTPUT_ALREADY_EXISTS');path.parent.mkdir(parents=True,exist_ok=True);
    with path.open('x') as output:output.write(json.dumps(value,ensure_ascii=False,indent=2,allow_nan=False)+'\n')
def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    p=sub.add_parser('freeze');p.add_argument('--catalog',required=True);p.add_argument('--output',required=True);p.add_argument('--labels',required=True)
    p=sub.add_parser('inputs');p.add_argument('--dataset',required=True);p.add_argument('--partition',choices=['Development','Holdout'],required=True);p.add_argument('--output',required=True)
    p=sub.add_parser('review');p.add_argument('--dataset',required=True);p.add_argument('--labels',required=True);p.add_argument('--output',required=True)
    p=sub.add_parser('score');p.add_argument('--dataset',required=True);p.add_argument('--partition',choices=['Development','Holdout'],default='Holdout');p.add_argument('--labels');p.add_argument('--results');p.add_argument('--output',required=True)
    args=parser.parse_args()
    try:
        if args.command=='freeze':
            raw=Path(args.catalog).read_bytes();catalog=json.loads(raw);data=freeze(catalog,raw);check(not Path(args.output).exists() and not Path(args.labels).exists(),'OUTPUT_ALREADY_EXISTS');write(args.output,data);write(args.labels,label_template(data,catalog));print(json.dumps({'hash':data['hash'],'partitions':dict(Counter(i['partition'] for i in data['items']))}));return
        data=json.loads(Path(args.dataset).read_text());validate_dataset(data)
        if args.command=='review':
            labels=json.loads(Path(args.labels).read_text());evaluate(data,labels);html=Path(__file__).with_name('k1_evaluation_review.html').read_text();embedded=lambda value:json.dumps(value,ensure_ascii=True,allow_nan=False).replace('<','\\u003c')
            check(not Path(args.output).exists(),'OUTPUT_ALREADY_EXISTS');target=Path(args.output);target.parent.mkdir(parents=True,exist_ok=True)
            with target.open('x') as output:output.write(html.replace('__DATA_JSON__',embedded(data)).replace('__LABEL_JSON__',embedded(labels)))
            return
        if args.command=='inputs':write(args.output,{'format':'k1-inputs/1','datasetHash':data['hash'],'partition':args.partition,'library':data['library'],'items':[{k:i[k] for k in ('id','revisionId','input','inputHash')} for i in data['items'] if i['partition']==args.partition]});return
        report=evaluate(data,json.loads(Path(args.labels).read_text()) if args.labels else None,json.loads(Path(args.results).read_text()) if args.results else None,args.partition);write(args.output,report);print(json.dumps({'status':report['qualityGate']['status'],'pendingItems':report['pendingItems'],'formalV1ExitProven':False}))
    except (EvaluationError,OSError,ValueError,KeyError,TypeError) as error:
        print(json.dumps({'errorCode':str(error) if isinstance(error,EvaluationError) else 'EVALUATION_INPUT_INVALID'}));raise SystemExit(1)
if __name__=='__main__':main()
