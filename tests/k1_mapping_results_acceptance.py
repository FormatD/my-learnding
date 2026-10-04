#!/usr/bin/env python3
"""Boundary tests for saved-response normalization; fixtures are explicitly controlled."""
import copy,json,sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'scripts'))
import k1_evaluation as e
import k1_mapping_results as adapter
DATA=json.loads((ROOT/'docs/evaluation/mixed-operations-draft-v1.json').read_text());LABELS=json.loads((ROOT/'docs/evaluation/mixed-operations-labels-template-v1.json').read_text());ITEMS=[i for i in DATA['items'] if i['partition']=='Holdout'];KC=DATA['library'][0];OTHER=DATA['library'][1]
def payload(value):return json.dumps(value,ensure_ascii=False,separators=(',',':'))
def fixture():
    source=json.loads((ROOT/'docs/content/mixed-operations-original-v1.json').read_text());raw=payload(source);run=dict(id='run',familyId='family',status='Completed',provider='Mock',model='mock/local-char-bigram/1/128/normalization-1',promptVersion='mapping-suggestion/1',sourceDraftId='draft',sourcePayload=raw,sourceHash=adapter.raw_hash(raw),libraryHash='library-hash',inputHash='input-hash');suggestions=[]
    for i,item in enumerate(ITEMS[:3]):
        ref=f"draft:draft/Question:{item['revisionId']}";items=[dict(kcId=KC['id'],kcRevisionId=KC['revisionId'],role='Primary',coverageWeight=1,evidenceShare=1,evidenceMode='WholeItem',step=None,sequence=1,modelScore=None,sourceRefs=[ref])]
        suggestions.append(dict(id='suggestion-'+str(i),familyId='family',runId='run',ownerType='Question',ownerId=item['id'],ownerRevisionId=item['revisionId'],evidencePolicy='SingleKC',suggestedItems=payload(items),status='Pending'))
    return dict(run=run,library=copy.deepcopy(DATA['library']),suggestions=suggestions,decisions=[])
def decide(detail,index,accept=True):
    suggestion=detail['suggestions'][index];original={'evidencePolicy':suggestion['evidencePolicy'],'items':json.loads(suggestion['suggestedItems'])};corrected=copy.deepcopy(original)
    corrected['items'][0].update(kcId=OTHER['id'],kcRevisionId=OTHER['revisionId']);raw=payload(corrected) if accept else '';decision=dict(id='decision-'+str(index),familyId='family',suggestionId=suggestion['id'],decision='Accept' if accept else 'Reject',reviewerId='controlled reviewer',reviewedAt='2026-10-05T10:00:00+08:00',reason='Fixture, not formal gold',originalPayloadHash=adapter.raw_hash(payload(original)),correctedPayload=raw,correctedPayloadHash=adapter.raw_hash(raw) if accept else '')
    suggestion['status']='Accepted' if accept else 'Rejected';detail['decisions'].append(decision)
class Acceptance(unittest.TestCase):
    def test_original_and_corrected_are_separate(self):
        detail=fixture();decide(detail,0);decide(detail,1,False);before=copy.deepcopy(detail)
        original=adapter.normalize(DATA,detail);reviewed=adapter.normalize(DATA,detail,stage='ReviewedMapping');self.assertEqual(detail,before)
        byid={r['id']:r for r in original['predictions']};corrected={r['id']:r for r in reviewed['predictions']}
        self.assertEqual(byid[ITEMS[0]['id']]['primaryKCIds'],[KC['id']]);self.assertEqual(corrected[ITEMS[0]['id']]['primaryKCIds'],[OTHER['id']]);self.assertEqual(corrected[ITEMS[1]['id']]['status'],'Rejected');self.assertEqual(corrected[ITEMS[2]['id']]['status'],'Skipped');self.assertEqual(byid[ITEMS[1]['id']]['status'],'Mapped');self.assertNotIn('tasks',reviewed);self.assertNotIn('review',reviewed['predictions'][0]);self.assertEqual(reviewed['capture']['detailHash'],e.digest(detail))
        labels=copy.deepcopy(LABELS)
        for row in labels['labels']:
            if row['id'] in {i['id'] for i in ITEMS[:3]}:row.update(status='Approved',validItem=True,measurable=True,primaryKCIds=[OTHER['id']],reviewer='controlled',reviewedAt='2026-10-05T10:00:00+08:00',reason='Fixture only')
        report=e.evaluate(DATA,labels,reviewed);self.assertEqual(report['primaryAccuracy'],e.metric(1,3));self.assertEqual(report['mappingCoverage'],e.metric(1,3));self.assertFalse(report['formalV1ExitProven']);self.assertEqual(report['qualityGate']['status'],'NotEvaluated')
    def test_context_not_primary_measurement(self):
        detail=fixture();s=detail['suggestions'][0];items=json.loads(s['suggestedItems']);items[0].update(role='Context',evidenceShare=0,evidenceMode='None');s.update(suggestedItems=payload(items),evidencePolicy='NoEvidence');row=next(r for r in adapter.normalize(DATA,detail)['predictions'] if r['id']==s['ownerId']);self.assertEqual(row['primaryKCIds'],[]);self.assertEqual(row['mappedKCIds'],[KC['id']]);self.assertEqual(row['status'],'Mapped')
    def test_invalid_original_remains_rejected(self):
        detail=fixture();s=detail['suggestions'][0];items=json.loads(s['suggestedItems']);items[0]['sourceRefs']=['wrong'];s['suggestedItems']=payload(items)
        row=next(r for r in adapter.normalize(DATA,detail)['predictions'] if r['id']==s['ownerId']);self.assertEqual(row['status'],'Rejected');self.assertEqual(row['mappedKCIds'],[])
    def test_snapshot_family_revision_and_partition_refusal(self):
        for mutate in (lambda d:d['run'].update(sourceHash='wrong'),lambda d:d['run'].update(provider='Manual'),lambda d:d['run'].update(status='Queued'),lambda d:d['library'][0].update(behavior='changed'),lambda d:d['suggestions'][0].update(familyId='other'),lambda d:d['suggestions'][0].update(ownerRevisionId='other'),lambda d:d['suggestions'].append(copy.deepcopy(d['suggestions'][0])),lambda d:d['suggestions'][0].update(status='Accepted')):
            detail=fixture();mutate(detail)
            with self.assertRaises(e.EvaluationError):adapter.normalize(DATA,detail)
        detail=fixture();source=json.loads(detail['run']['sourcePayload']);next(q for q in source['questions'] if q['id']==ITEMS[0]['id'])['stem']='changed';detail['run']['sourcePayload']=payload(source);detail['run']['sourceHash']=adapter.raw_hash(payload(source))
        with self.assertRaises(e.EvaluationError):adapter.normalize(DATA,detail)
        with self.assertRaises(e.EvaluationError):adapter.normalize(DATA,fixture(),partition='Development')
    def test_invalid_proposals_do_not_count_as_mapped(self):
        self.assertFalse(adapter.proposal_valid([],ITEMS[0]['input'],{},'reference'))
        for change in (dict(sequence=0),dict(role='Unknown'),dict(evidenceShare=1.1),dict(coverageWeight=.1234567),dict(role='Context'),dict(kcRevisionId='foreign'),dict(step='unexpected'),dict(modelScore=True)):
            detail=fixture();s=detail['suggestions'][0];items=json.loads(s['suggestedItems']);items[0].update(change);s['suggestedItems']=payload(items)
            row=next(r for r in adapter.normalize(DATA,detail)['predictions'] if r['id']==s['ownerId']);self.assertEqual(row['status'],'Rejected');self.assertEqual(row['mappedKCIds'],[])
    def test_review_hash_and_mapping_refusal(self):
        for change in (dict(correctedPayloadHash='wrong'),dict(originalPayloadHash='wrong'),dict(familyId='other'),dict(reason=''),dict(reviewedAt='2026-10-05')):
            detail=fixture();decide(detail,0);detail['decisions'][0].update(change)
            with self.assertRaises(e.EvaluationError):adapter.normalize(DATA,detail)
        detail=fixture();decide(detail,0);corrected=json.loads(detail['decisions'][0]['correctedPayload']);corrected['items'][0]['kcRevisionId']='foreign';raw=payload(corrected);detail['decisions'][0].update(correctedPayload=raw,correctedPayloadHash=adapter.raw_hash(raw))
        with self.assertRaises(e.EvaluationError):adapter.normalize(DATA,detail)
if __name__=='__main__':unittest.main()
