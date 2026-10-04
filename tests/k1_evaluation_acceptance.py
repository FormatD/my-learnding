#!/usr/bin/env python3
import copy,importlib.util,json,os,subprocess,tempfile,threading,unittest
from functools import partial
from http.server import SimpleHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('evaluation',ROOT/'scripts/k1_evaluation.py');e=importlib.util.module_from_spec(spec);spec.loader.exec_module(e)
DATA=json.loads((ROOT/'docs/evaluation/mixed-operations-draft-v1.json').read_text());LABELS=json.loads((ROOT/'docs/evaluation/mixed-operations-labels-template-v1.json').read_text());IDS=[i for i in DATA['items'] if i['partition']=='Holdout'];KC=DATA['library'][0]['id'];OTHER=DATA['library'][1]['id']
STAMP='2026-10-05T10:00:00+08:00'
def approved(all_=False):
    labels=copy.deepcopy(LABELS)
    for row in labels['labels']:
        if row['id'] in {i['id'] for i in (IDS if all_ else IDS[:4])}:row.update(status='Approved',validItem=True,measurable=True,primaryKCIds=[KC],reviewer='Controlled test only',reviewedAt=STAMP,reason='Fixture, not human gold')
    return labels

def prediction(item,primary=None,status='Mapped'):
    return dict(id=item['id'],inputHash=item['inputHash'],status=status,primaryKCIds=[KC] if primary is None else primary,mappedKCIds=[KC] if status=='Mapped' else [])
def results():return dict(format='k1-results/1',datasetHash=DATA['hash'],partition='Holdout',producer={'kind':'Controlled'},predictions=[])
def review():return dict(reviewer='Controlled test only',recordedAt=STAMP,initialSeconds=4,correctionSeconds=2)
def audit():return dict(reviewer='Controlled test only',reviewedAt=STAMP,reason='Fixture only',nodes=[dict(id='formal',nodeKind='Measurable',published=True,redundantDuplicate=False,formalAssociations=[dict(type='Question',id=IDS[0]['id'])])])
class Acceptance(unittest.TestCase):
    def test_frozen_reference_and_no_gold_leakage(self):
        raw=(ROOT/'docs/content/mixed-operations-original-v1.json').read_bytes();catalog=json.loads(raw)
        self.assertEqual(e.freeze(catalog,raw),DATA);self.assertEqual(len(DATA['items']),160);self.assertEqual(len(IDS),56)
        for key in ('variantGroupId','templateKey'):
            dev={i[key] for i in DATA['items'] if i['partition']=='Development'};self.assertFalse(dev&{i[key] for i in IDS})
        self.assertTrue(all('mappings' not in i['input'] for i in DATA['items']));self.assertEqual(LABELS,e.label_template(DATA,catalog));self.assertTrue(all(r['status']=='Pending' for r in LABELS['labels']))
        report=e.evaluate(DATA,LABELS);self.assertEqual(report['pendingItems'],56);self.assertIsNone(report['primaryAccuracy']['rate']);self.assertFalse(report['formalV1ExitProven']);self.assertEqual(report['qualityGate']['status'],'NotEvaluated')
    def test_missing_rejected_context_denominators(self):
        labels=approved();rows={r['id']:r for r in labels['labels']};rows[IDS[3]['id']].update(measurable=False,primaryKCIds=[])
        res=results();res['predictions']=[prediction(IDS[0]),prediction(IDS[1],[],status='Rejected'),prediction(IDS[3],[])]
        report=e.evaluate(DATA,labels,res);self.assertEqual(report['primaryAccuracy'],e.metric(1,3));self.assertEqual(report['mappingCoverage'],e.metric(2,4));self.assertEqual(report['missingPredictionItems'],53);self.assertFalse(report['goldComplete'])
        res['predictions'][0]['primaryKCIds']=[];self.assertEqual(e.evaluate(DATA,labels,res)['primaryAccuracy'],e.metric(0,3))
        self.assertEqual(sum(v['primaryAccuracy']['denominator'] for v in report['byQuestionType'].values()),3)
    def test_schema_and_citation_facts(self):
        res=results();res['tasks']=[dict(id='a',status='Completed',firstSchemaPassed=True,repairedSchemaPassed=False),dict(id='b',status='Completed',firstSchemaPassed=False,repairedSchemaPassed=True),dict(id='c',status='Failed',firstSchemaPassed=False,repairedSchemaPassed=False)]
        cite=dict(inputHash=IDS[0]['inputHash'],locator='question:'+IDS[0]['id'],quote=IDS[0]['input']['stem'][:3])
        res['candidates']=[dict(questionId=IDS[0]['id'],citations=[cite]),dict(questionId=IDS[0]['id'],citations=[{**cite,'quote':'nonexistent quote'}]),dict(questionId=IDS[1]['id'],citations=[])]
        report=e.evaluate(DATA,approved(),res);self.assertEqual(report['firstSchemaSuccess'],e.metric(1,2));self.assertEqual(report['repairedSchemaSuccess'],e.metric(1,2));self.assertEqual(report['sourceTraceability'],e.metric(1,3));self.assertEqual(report['taskCounts'],dict(completed=2,failed=1,total=3))
        res['tasks'][0]['repairedSchemaPassed']=True
        with self.assertRaises(e.EvaluationError):e.evaluate(DATA,approved(),res)
    def test_review_timing_is_explicit_and_complete(self):
        res=results();res['predictions']=[{**prediction(i),'review':review()} for i in IDS[:4]]
        self.assertEqual(e.evaluate(DATA,approved(),res)['reviewMinutesPer100']['minutesPer100'],10)
        res['predictions'][0].pop('review');self.assertIsNone(e.evaluate(DATA,approved(),res)['reviewMinutesPer100'])
        res['predictions'][-1]['review']['initialSeconds']=float('nan')
        with self.assertRaises((e.EvaluationError,ValueError)):e.evaluate(DATA,approved(),res)
    def test_directory_and_unpublished_not_measured(self):
        res=results();res['libraryAudit']=audit();base=res['libraryAudit']['nodes'][0];res['libraryAudit']['nodes'] += [{**base,'id':'duplicate','redundantDuplicate':True,'formalAssociations':[]},{**base,'id':'directory','nodeKind':'Directory','redundantDuplicate':True,'formalAssociations':[]},{**base,'id':'draft','published':False,'redundantDuplicate':True,'formalAssociations':[]}]
        report=e.evaluate(DATA,approved(),res);self.assertEqual(report['duplicateRate'],e.metric(1,2));self.assertEqual(report['orphanRate'],e.metric(1,2));self.assertIsNone(e.evaluate(DATA,approved(),results())['duplicateRate'])
    def test_supplied_pass_never_proves_formal_exit(self):
        res=results();res.update(predictions=[{**prediction(i),'review':review()} for i in IDS],tasks=[dict(id='controlled',status='Completed',firstSchemaPassed=True,repairedSchemaPassed=False)],candidates=[dict(questionId=IDS[0]['id'],citations=[dict(inputHash=IDS[0]['inputHash'],locator='question:'+IDS[0]['id'],quote=IDS[0]['input']['stem'])])],libraryAudit=audit())
        report=e.evaluate(DATA,approved(True),res);self.assertEqual(report['qualityGate']['status'],'SuppliedRecordsMeetTargets');self.assertFalse(report['formalV1ExitProven']);self.assertEqual(report['producer']['kind'],'Controlled')
    def test_drift_and_invalid_facts_rejected(self):
        broken=copy.deepcopy(DATA);broken['items'][0]['input']['stem']='changed'
        with self.assertRaises(e.EvaluationError):e.evaluate(broken)
        leaked=copy.deepcopy(DATA);item=next(i for i in leaked['items'] if i['partition']=='Development');item['partition']='Holdout';leaked=e.seal({k:v for k,v in leaked.items() if k!='hash'})
        with self.assertRaises(e.EvaluationError):e.evaluate(leaked)
        for change in (dict(inputHash='wrong'),dict(validItem=False,measurable=True,primaryKCIds=[]),dict(reviewedAt='2026-10-05'),dict(primaryKCIds=['foreign']),dict(reason='')):
            labels=approved();next(r for r in labels['labels'] if r['id']==IDS[0]['id']).update(change)
            with self.assertRaises(e.EvaluationError):e.evaluate(DATA,labels)
        res=results();res['predictions']=[prediction(next(i for i in DATA['items'] if i['partition']=='Development'))]
        with self.assertRaises(e.EvaluationError):e.evaluate(DATA,approved(),res)
        labels=approved();labels['labels'].append(copy.deepcopy(labels['labels'][0]))
        with self.assertRaises(e.EvaluationError):e.evaluate(DATA,labels)
    def test_cli_immutable_outputs_and_private_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            target=Path(directory)/'input.json';args=['python3',str(ROOT/'scripts/k1_evaluation.py'),'inputs','--dataset',str(ROOT/'docs/evaluation/mixed-operations-draft-v1.json'),'--partition','Holdout','--output',str(target)]
            subprocess.run(args,check=True,capture_output=True);raw=target.read_bytes();self.assertEqual(len(json.loads(raw)['items']),56);self.assertNotIn(b'draftReferencePrimaryKCIds',raw);self.assertNotIn(b'primaryKCIds',raw)
            proc=subprocess.run(args,capture_output=True);self.assertNotEqual(proc.returncode,0);self.assertEqual(target.read_bytes(),raw)

def browser():
    class Quiet(SimpleHTTPRequestHandler):
        def log_message(self,*args):pass
    with tempfile.TemporaryDirectory() as directory:
        page=Path(directory)/'review.html';subprocess.run(['python3',str(ROOT/'scripts/k1_evaluation.py'),'review','--dataset',str(ROOT/'docs/evaluation/mixed-operations-draft-v1.json'),'--labels',str(ROOT/'docs/evaluation/mixed-operations-labels-template-v1.json'),'--output',str(page)],check=True)
        server=ThreadingHTTPServer(('127.0.0.1',0),partial(Quiet,directory=directory));thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        try:
            env=dict(os.environ,EVALUATION_REVIEW_URL=f'http://127.0.0.1:{server.server_port}/review.html');subprocess.run(['npm','run','test:e2e','--','tests/k1-evaluation-review.spec.ts','--workers=1'],cwd=ROOT/'src/web',env=env,check=True)
        finally:server.shutdown();server.server_close();thread.join()
if __name__=='__main__':
    import sys
    use_browser='--browser' in sys.argv
    suite=unittest.defaultTestLoader.loadTestsFromTestCase(Acceptance);result=unittest.TextTestRunner(verbosity=2).run(suite)
    if not result.wasSuccessful():raise SystemExit(1)
    if use_browser:browser()
