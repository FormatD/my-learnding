#!/usr/bin/env python3
"""Normalize a locally saved MappingRunDetail API response for frozen evaluation."""
import argparse,hashlib,json,math
from pathlib import Path
import k1_evaluation as evaluation
from k1_evaluation import check,EvaluationError,digest

def raw_hash(value):return hashlib.sha256(value.encode()).hexdigest()
def json_payload(value,code):
    check(isinstance(value,str),code)
    try:return json.loads(value)
    except ValueError:raise EvaluationError(code) from None

def proposal_valid(proposal,question,library,reference):
    if not isinstance(proposal,dict):return False
    items=proposal.get('items');policy=proposal.get('evidencePolicy')
    if not isinstance(items,list) or len(items)>20 or policy not in ('NoEvidence','SingleKC','ObservedSteps'):return False
    keys=set();sequences=[];total=0;whole=0;steps=0
    for item in items:
        if not isinstance(item,dict) or item.get('kcId') not in library or item.get('kcRevisionId')!=library[item['kcId']]['revisionId'] or item.get('sourceRefs')!=[reference]:return False
        role=item.get('role');mode=item.get('evidenceMode');share=item.get('evidenceShare');weight=item.get('coverageWeight');step=item.get('step');sequence=item.get('sequence');score=item.get('modelScore')
        if role not in ('Primary','Secondary','Prerequisite','Context') or mode not in ('None','WholeItem','StepObserved') or type(sequence) is not int:return False
        for value in (share,weight):
            if type(value) not in (int,float) or not math.isfinite(value) or not 0<=value<=1 or abs(value*1e6-round(value*1e6))>1e-7:return False
        if score is not None and (type(score) not in (int,float) or not math.isfinite(score) or not -1<=score<=1):return False
        if not (step is None or isinstance(step,str)):return False
        key=(item['kcId'],mode,step)
        if key in keys:return False
        keys.add(key);sequences.append(sequence)
        if role in ('Prerequisite','Context') and (mode!='None' or share!=0):return False
        if mode=='None' and (share!=0 or step is not None) or mode!='None' and share<=0:return False
        if mode!='None':total+=share
        if mode=='WholeItem':whole+=1
        if mode=='StepObserved':
            steps+=1
            if not isinstance(step,str) or not step.strip() or len(step)>100:return False
        if policy=='NoEvidence' and mode!='None':return False
        if policy=='SingleKC' and (mode not in ('None','WholeItem') or step is not None):return False
    return sorted(sequences)==list(range(1,len(items)+1)) and total<=1+1e-12 and (policy!='SingleKC' or whole==1) and (policy!='ObservedSteps' or question['type'] in ('ShortAnswer','MultiStep') and steps>0 and whole==0)

def normalize(data,detail,partition='Holdout',stage='OriginalSuggestion'):
    evaluation.validate_dataset(data);check(partition in ('Development','Holdout'),'PARTITION_INVALID');check(stage in ('OriginalSuggestion','ReviewedMapping'),'STAGE_INVALID')
    run=detail['run'];check(run['status']=='Completed','RUN_NOT_COMPLETED');check(run['provider']=='Mock' and run['model']=='mock/local-char-bigram/1/128/normalization-1' and run['promptVersion']=='mapping-suggestion/1','EVALUATION_PROVIDER_UNSUPPORTED')
    source=json_payload(run['sourcePayload'],'SOURCE_PAYLOAD_INVALID');check(raw_hash(run['sourcePayload'])==run['sourceHash'],'SOURCE_HASH_MISMATCH')
    library={k['id']:k for k in detail['library']};check(len(library)==len(detail['library']) and digest(sorted(library.values(),key=lambda k:k['id']))==digest(sorted(data['library'],key=lambda k:k['id'])),'FROZEN_LIBRARY_MISMATCH')
    questions={q['id']:q for q in source['questions']};check(len(questions)==len(source['questions']),'SOURCE_QUESTION_DUPLICATE');selected={i['id']:i for i in data['items'] if i['partition']==partition}
    decisions={};suggestions={s['id']:s for s in detail['suggestions']};check(len(suggestions)==len(detail['suggestions']),'SUGGESTION_ID_DUPLICATE')
    for decision in detail['decisions']:
        id=decision['suggestionId'];check(id in suggestions and id not in decisions and decision['decision'] in ('Accept','Reject'),'REVIEW_DECISION_INVALID');evaluation.text(decision.get('reviewerId'),'REVIEWER_REQUIRED');evaluation.timestamp(decision.get('reviewedAt'));evaluation.text(decision.get('reason'),'REVIEW_REASON_REQUIRED');decisions[id]=decision
    predictions=[];owners=set()
    for suggestion in detail['suggestions']:
        check(suggestion['runId']==run['id'] and suggestion['familyId']==run['familyId'],'RUN_OWNER_MISMATCH');check(suggestion['ownerType']=='Question','NON_QUESTION_EVALUATION_UNSUPPORTED');id=suggestion['ownerId'];check(id in selected and id not in owners,'RESULT_ID_OR_PARTITION_INVALID');owners.add(id);item=selected[id]
        check(id in questions and suggestion['ownerRevisionId']==item['revisionId'],'QUESTION_REVISION_MISMATCH');input_={k:v for k,v in questions[id].items() if k!='mappings'};check(digest(input_)==item['inputHash'],'QUESTION_INPUT_CHANGED')
        original={'evidencePolicy':suggestion['evidencePolicy'],'items':json_payload(suggestion['suggestedItems'],'SUGGESTED_PAYLOAD_INVALID')};reference=f"draft:{run['sourceDraftId']}/Question:{item['revisionId']}";decision=decisions.get(suggestion['id']);status=suggestion['status']
        check(status in ('Pending','Accepted','Rejected') and (status=='Pending' and decision is None or status=='Accepted' and decision is not None and decision['decision']=='Accept' or status=='Rejected' and decision is not None and decision['decision']=='Reject'),'REVIEW_STATE_MISMATCH')
        corrected=None
        if decision:
            check(decision['familyId']==run['familyId'],'REVIEW_FAMILY_MISMATCH')
            # Hash the original as the server serialized it. The raw items substring
            # preserves decimal notation, key order and System.Text.Json escaping.
            expected=raw_hash('{"evidencePolicy":'+json.dumps(suggestion['evidencePolicy'])+',"items":'+suggestion['suggestedItems']+'}')
            check(decision['originalPayloadHash']==expected,'ORIGINAL_REVIEW_HASH_MISMATCH')
            if decision['decision']=='Accept':
                check(raw_hash(decision['correctedPayload'])==decision['correctedPayloadHash'],'CORRECTED_HASH_MISMATCH');corrected=json_payload(decision['correctedPayload'],'CORRECTED_PAYLOAD_INVALID');check(proposal_valid(corrected,input_,library,reference),'CORRECTED_MAPPING_INVALID')
            else:check(decision['correctedPayload']=='' and decision['correctedPayloadHash']=='','REJECTED_PAYLOAD_PRESENT')
        proposal=original if stage=='OriginalSuggestion' else corrected
        state='Skipped' if proposal is None and status=='Pending' else 'Rejected'
        if proposal is not None and proposal_valid(proposal,input_,library,reference) and proposal['items']:state='Mapped'
        mappings=proposal['items'] if state=='Mapped' else []
        row={'id':id,'inputHash':item['inputHash'],'status':state,'primaryKCIds':sorted({m['kcId'] for m in mappings if m['role']=='Primary' and m['evidenceMode'] in ('WholeItem','StepObserved') and m['evidenceShare']>0}),'mappedKCIds':sorted({m['kcId'] for m in mappings}),'provenance':{'runId':run['id'],'runInputHash':run['inputHash'],'suggestionId':suggestion['id'],'suggestedItemsHash':raw_hash(suggestion['suggestedItems']),'reviewDecisionId':decision['id'] if decision else None,'reviewStatus':status,'stage':stage,'sourceReference':reference}}
        predictions.append(row)
    result={'format':'k1-results/1','datasetHash':data['hash'],'partition':partition,'producer':{'kind':'Mock','provider':run['provider'],'model':run['model'],'promptVersion':run['promptVersion'],'stage':stage},'predictions':sorted(predictions,key=lambda r:r['id']),'capture':{'format':'mapping-evaluation-capture/1','detailHash':digest(detail),'runId':run['id'],'sourceHash':run['sourceHash'],'libraryHash':run['libraryHash'],'runInputHash':run['inputHash'],'stage':stage,'verification':'LocalSavedResponseChecksNotIndependentServerAttestation'},'notice':'Mock mapping only. No inferred schema, citations, audit, human gold or review duration. Formal V1 exit remains unproven.'}
    evaluation.evaluate(data,results=result,partition=partition);return result

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--dataset',required=True);parser.add_argument('--detail',required=True);parser.add_argument('--partition',choices=['Development','Holdout'],default='Holdout');parser.add_argument('--stage',choices=['OriginalSuggestion','ReviewedMapping'],required=True);parser.add_argument('--output',required=True);args=parser.parse_args()
    try:
        data=json.loads(Path(args.dataset).read_text());detail=json.loads(Path(args.detail).read_text());result=normalize(data,detail,args.partition,args.stage);evaluation.write(args.output,result);print(json.dumps({'predictionItems':len(result['predictions']),'stage':args.stage,'formalV1ExitProven':False}))
    except (EvaluationError,OSError,ValueError,KeyError,TypeError) as error:
        print(json.dumps({'errorCode':str(error) if isinstance(error,EvaluationError) else 'EVALUATION_INPUT_INVALID'}));raise SystemExit(1)
if __name__=='__main__':main()
