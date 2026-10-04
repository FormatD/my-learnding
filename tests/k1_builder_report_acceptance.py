#!/usr/bin/env python3
import copy,json,sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'scripts'))
import k1_builder_report as adapter
from k1_evaluation import EvaluationError

def fixture(repaired=False):
    text='先乘除后加减。';protocol=dict(name='能力',subject='MATH',kcType='Procedure',gradeMin=3,gradeMax=3,measurableBehavior='按顺序计算',boundary='不含建模',sourceChunkIds=['chunk'],supportingQuotes=[text],modelScore=0)
    source=dict(id='source',familyId='family',text=text,hash=adapter.hashlib.sha256(text.encode()).hexdigest(),fileId=None)
    run=dict(id='run',familyId='family',type='Candidates',provider='Mock',model='fixture/1',promptVersion='kc-candidate/2',status='Completed',sourceId='source',inputHash='input')
    candidate=dict(id='candidate',familyId='family',runId='run',chunkId='chunk',name='能力',type='Procedure',behavior='按顺序计算',boundary='不含建模',quote=text,protocolPayload=json.dumps(protocol))
    result=dict(output=dict(schemaVersion='kc-candidate/2',candidates=[protocol]),calls=2 if repaired else 1,repaired=repaired)
    return dict(runs=[run],sources=[source],chunks=[dict(id='chunk',familyId='family',sourceId='source',locator='段落 1',text=text)],attempts=[dict(id='attempt',familyId='family',runId='run',status='Completed',protocolResult=json.dumps(result))],candidates=[candidate])
class Acceptance(unittest.TestCase):
    def test_first_and_repaired_remain_separate(self):
        for repaired in (False,True):
            state=fixture(repaired);before=copy.deepcopy(state);r=adapter.report(state,['run']);self.assertEqual(state,before);self.assertEqual(r['firstSchemaSuccess']['rate'],0 if repaired else 1);self.assertEqual(r['repairedSchemaSuccess']['rate'],1 if repaired else 0);self.assertEqual(r['sourceTraceability']['rate'],1);self.assertFalse(r['formalV1ExitProven']);self.assertEqual(r['semanticQuality'],'NotEvaluated');self.assertNotIn('quote',r['candidates'][0]['references'][0])
    def test_failed_queued_counted_separately(self):
        state=fixture()
        for status in ('Failed','Queued'):state['runs'].append({**state['runs'][0],'id':status,'status':status,'error':'INPUT_LIMIT' if status=='Failed' else None})
        r=adapter.report(state,['run','Failed','Queued']);self.assertEqual(r['counts']['failed'],1);self.assertEqual(r['counts']['queued'],1);self.assertEqual(r['firstSchemaSuccess']['denominator'],1)
    def test_legacy_unknown_suppresses_rate(self):
        state=fixture();state['attempts'][0]['protocolResult']=None;state['candidates'][0]['protocolPayload']=None;r=adapter.report(state,['run']);self.assertIsNone(r['firstSchemaSuccess']['rate']);self.assertIsNone(r['sourceTraceability']['rate']);self.assertEqual(r['counts']['completedProtocolUnknown'],1);self.assertEqual(r['counts']['candidateProtocolUnknown'],1);self.assertEqual(r['sourceTraceability']['denominator'],1)
    def test_invalid_sources_stay_in_denominator(self):
        for mutation in (lambda s:s['chunks'][0].update(sourceId='other'),lambda s:s['chunks'][0].update(familyId='other'),lambda s:s['chunks'][0].update(text='different text'),lambda s:s['chunks'].clear(),lambda s:s['candidates'][0].update(quote='different quote')):
            state=fixture();mutation(state);r=adapter.report(state,['run']);self.assertEqual(r['sourceTraceability']['denominator'],1);self.assertEqual(r['sourceTraceability']['numerator'],0);self.assertEqual(r['candidates'][0]['sourceStatus'],'InvalidRecordedPairs')
        state=fixture();state['sources'][0]['fileId']='pdf-file';state['sources'][0]['hash']='file-hash';r=adapter.report(state,['run']);self.assertEqual(r['runs'][0]['sourceHashCheck'],'OriginalFileHashNotRecomputed')
    def test_ambiguous_or_changed_protocols_refused(self):
        for mutation in (lambda s:s['runs'][0].update(provider='Other'),lambda s:s['sources'][0].update(hash='wrong'),lambda s:s['attempts'].append({**s['attempts'][0],'id':'other'}),lambda s:s['candidates'][0].update(type='Expression'),lambda s:s['candidates'][0].update(familyId='other'),lambda s:s['runs'][0].update(status='Failed')):
            state=fixture();mutation(state)
            with self.assertRaises(EvaluationError):adapter.report(state,['run'])
        state=fixture();record=json.loads(state['attempts'][0]['protocolResult']);record['calls']=2;state['attempts'][0]['protocolResult']=json.dumps(record)
        with self.assertRaises(EvaluationError):adapter.report(state,['run'])
        with self.assertRaises(EvaluationError):adapter.report(fixture(),['run','run'])
if __name__=='__main__':unittest.main()
