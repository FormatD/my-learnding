"""Opt-in local model comparison; original fixtures only, no learning DB writes."""
import argparse,hashlib,json,math,subprocess,time,urllib.request
from pathlib import Path
root=Path(__file__).resolve().parent.parent
p=argparse.ArgumentParser();p.add_argument('--baseline-only',action='store_true');p.add_argument('--unverified-artifact',action='store_true',help='Ephemeral comparison only: explicitly record unknown artifact identity, never create a production index');p.add_argument('--model',default='embeddinggemma-2-bf16');p.add_argument('--qwen',default='Qwen3.5-9B-MLX-4bit');p.add_argument('--output',default='.local/embedding-comparison');a=p.parse_args()
out=(root/a.output).resolve();out.mkdir(parents=True,exist_ok=True)
key=(root/'.local/omlx-api-key.txt').read_text().strip()
def call(path,body=None,timeout=180):
 req=urllib.request.Request('http://127.0.0.1:8000'+path,data=None if body is None else json.dumps(body,ensure_ascii=False).encode(),headers={'Authorization':'Bearer '+key,'Content-Type':'application/json'})
 with urllib.request.urlopen(req,timeout=timeout) as r:return json.load(r)
def save(name,data):
 target=out/name;target.write_text(json.dumps(data,ensure_ascii=False,indent=2));target.chmod(0o600)
status=call('/v1/models/status');save('status-before.json',status);save('runtime.json',call('/openapi.json')['info'])
fixture=root/'tests/embedding-comparison/math-fixture.json'
metrics=[];data={'results':[]}
if not a.baseline_only:
 model=next(m for m in status['models'] if m['id']==a.model)
 folder=Path(model['model_path']);manifest=[]
 for f in sorted(folder.rglob('*')):
  if not f.is_file() or '.cache' in f.parts or f.suffix not in ['.json','.safetensors','.jinja']:continue
  h=hashlib.sha256()
  with f.open('rb') as stream:
   for chunk in iter(lambda:stream.read(8*1024*1024),b''):h.update(chunk)
  manifest.append({'file':str(f.relative_to(folder)),'sha256':h.hexdigest(),'bytes':f.stat().st_size})
 if not manifest or not any(x['file'].endswith('.safetensors') for x in manifest):
  if not a.unverified_artifact:raise RuntimeError('Actual model artifact files are not readable; cannot declare a verified digest or index')
  digest='unverified-experiment/'+str(time.time_ns());save('artifact-manifest.json',{'verified':False,'digest':None,'experimentalSpaceVersion':digest,'reason':'CLI cannot read actual model artifacts. Ephemeral comparison only; production persistence forbidden.'})
 else:
  digest='sha256:'+hashlib.sha256(json.dumps(manifest,sort_keys=True,separators=(',',':')).encode()).hexdigest();save('artifact-manifest.json',{'verified':True,'digest':digest,'files':manifest})
 subprocess.run([str(root/'.tools/dotnet/dotnet'),str(root/'tests/embedding-comparison/bin/Debug/net10.0/Learning.EmbeddingComparison.dll'),str(fixture),a.model,str(root/'.local/omlx-api-key.txt'),str(out),digest],check=True,timeout=420)
 data=json.loads((out/'retrieval-results.json').read_text())
 for result in data['results']:
  for method in ['baseline','semantic','hybrid']:
   rows=result['rows'];n=len(rows)
   metrics.append({'recipe':result['recipe'],'method':method,'top1':sum(r[method][0]==r['expected'] for r in rows)/n,'recall3':sum(r['expected'] in r[method][:3] for r in rows)/n,'mrr':sum(1/(r[method].index(r['expected'])+1) if r['expected'] in r[method] else 0 for r in rows)/n})
else:
 subprocess.run([str(root/'.tools/dotnet/dotnet'),str(root/'tests/embedding-comparison/bin/Debug/net10.0/Learning.EmbeddingComparison.dll'),str(fixture),'baseline',str(root/'.local/omlx-api-key.txt'),str(out),'not-used'],check=True,timeout=30)
 rows=json.loads((out/'baseline-results.json').read_text());n=len(rows)
 metrics.append({'recipe':'current-production','method':'baseline','top1':sum(r['baseline'][0]==r['expected'] for r in rows)/n,'recall3':sum(r['expected'] in r['baseline'][:3] for r in rows)/n,'mrr':sum(1/(r['baseline'].index(r['expected'])+1) if r['expected'] in r['baseline'] else 0 for r in rows)/n})
print('retrieval metrics',json.dumps(metrics),flush=True)
f=json.loads(fixture.read_text());started=time.perf_counter()
public_fixture={'library':f['library'],'queries':[{k:q[k] for k in ('id','name','behavior','boundary')} for q in f['queries']]}
request={'model':a.qwen,'temperature':0,'max_tokens':2048,'stream':False,'response_format':{'type':'json_object'},'chat_template_kwargs':{'enable_thinking':False},'messages':[{'role':'system','content':'你是数学能力检索器。对每条查询，从给定能力库选择最匹配的三个能力，按测量行为与边界排序。不要只按话题。只输出JSON对象：{"results":[{"query":"q01","ranking":["能力id1","能力id2","能力id3"]}]}。必须覆盖每条查询，能力id必须来自给定库，不要说明。'},{'role':'user','content':json.dumps(public_fixture,ensure_ascii=False)}]}
try:
 response=call('/v1/chat/completions',request,timeout=240);elapsed=(time.perf_counter()-started)*1000;save('qwen-response.json',response)
 assert response['model']==a.qwen and response['choices'][0]['finish_reason']=='stop'
 rows=json.loads(response['choices'][0]['message']['content'])['results'];ids={q['id'] for q in f['queries']};allowed={k['id'] for k in f['library']}
 assert len(rows)==len(ids) and {r['query'] for r in rows}==ids
 assert all(len(r['ranking'])==3 and len(set(r['ranking']))==3 for r in rows)
 by={r['query']:r['ranking'] for r in rows};n=len(ids)
 qmetric={'model':a.qwen,'top1':sum(by[q['id']][0]==q['expected'] for q in f['queries'])/n,'recall3':sum(q['expected'] in by[q['id']] for q in f['queries'])/n,'invalidIds':[{'query':r['query'],'id':v} for r in rows for v in r['ranking'] if v not in allowed],'batchElapsedMs':elapsed,'usage':response.get('usage'),'note':'Expected labels withheld from prompt. One batch of 24 explicit-choice queries; not comparable to per-query embedding latency; measured here, not durable production calls.'}
except Exception as e:qmetric={'model':a.qwen,'error':type(e).__name__,'batchElapsedMs':(time.perf_counter()-started)*1000,'note':'Failure is not a quality score; request timeout does not prove model stopped.'}
save('comparison-summary.json',{'queries':len(f['queries']),'library':len(f['library']),'retrieval':metrics,'qwen':qmetric,'embeddingBatches':[{'recipe':r['recipe'],'milliseconds':r['elapsedMs'],'usage':r['Usage']} for r in data['results']]});save('status-after.json',call('/v1/models/status'));print('qwen metrics',json.dumps(qmetric),flush=True)
