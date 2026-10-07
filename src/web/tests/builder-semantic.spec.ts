import {test,expect} from '@playwright/test';
const unsafe='<script>window.__semanticUnsafe=1</script>';
async function fixture(page:any,owner=true,failed=false){
 const candidate={id:'candidate',runId:'run',name:'受控候选',behavior:'原始可测行为',boundary:'原始边界',status:'Pending',suggestedAction:'NeedsReview',protocolPayload:'{}'};
 const fragment={id:'fragment',text:'受控原始引用 '+unsafe,locator:'明确受控段落'};
 const context={candidate,run:{provider:'Mock',model:'fixture/1',promptVersion:'fixture',modelConfigPayload:'{}'},protocol:{subject:'MATH',kcType:'Procedure',gradeMin:3,gradeMax:3,modelScore:0,sourceChunkIds:['fragment'],supportingQuotes:[fragment.text]},source:{title:'受控来源',usageScope:'SyntheticWorkflowFixture',fragments:[fragment]},matches:[],notice:'仅用于页面工作流验证'};
 let phase=failed?'Failed':'Empty',posts:any[]=[],reconciliation:any=null,retryDenied=failed;
 const prep=()=>({id:'prep',candidateId:candidate.id,runId:'run',jobId:'job',status:phase,attemptCount:1,retryRound:0,createdAt:new Date().toISOString(),error:phase==='Failed'?'PROVIDER_CALL_FAILED':null});
 const call={id:'call',preparationId:'prep',model:'controlled-model',status:failed?'Failed':'Returned',budgetState:failed?'Unresolved':'Settled',inputTokens:11,outputTokens:failed?null:5,retryRound:0,attemptNumber:1};
 const result={candidateId:candidate.id,decision:'NeedsReview',kcId:null,kcRevisionId:null,reason:'原模型中文理由 '+unsafe,candidateQuotes:[{sourceChunkId:'fragment',text:fragment.text}],definitionQuotes:[]};
 await page.route('**/api/v1/**',async(route:any)=>{
  const req=route.request(),path=new URL(req.url()).pathname.replace('/api/v1','');let body:any;
  if(req.method()==='POST')posts.push({path,body:req.postDataJSON()});
  if(path==='/me')body={family:{ownerAccountId:'owner'},actor:{accountId:owner?'owner':'editor',role:'Parent',roles:'ContentEditor'}};
  else if(path==='/students')body=[];
  else if(path==='/content')body={drafts:[],releases:[]};
  else if(path==='/background-jobs/window')body={jobs:[],nextCursor:null};
  else if(path==='/builder/stages')body=[];
  else if(path==='/builder/stages/extraction/tasks')body={items:[],total:0,page:1};
  else if(path.startsWith('/builder/reference-images/'))body=[{id:'source',title:'受控来源',text:fragment.text,chunks:[fragment],pages:[]}];
  else if(path==='/builder')body={sources:[],runs:[],attempts:[],candidates:[candidate],libraries:[],provider:'controlled'};
  else if(path==='/builder/candidates/candidate/review-context')body=context;
  else if(path==='/builder/candidates/candidate/semantic-preparations'){phase='Running';await route.fulfill({status:202,headers:{ETag:'"2"'},json:prep()});return;}
  else if(path==='/builder/semantic-preparations')body={items:phase==='Empty'?[]:[prep()],total:phase==='Empty'?0:1,page:1,pageSize:20};
  else if(path==='/builder/semantic-preparations/prep')body={preparation:prep(),input:{candidate:{name:candidate.name,measurableBehavior:candidate.behavior,boundary:candidate.boundary},matches:[]},model:{model:'controlled-model'},result:phase==='Succeeded'?result:null,suggestion:phase==='Succeeded'?{id:'suggestion'}:null};
  else if(path==='/builder/semantic-calls')body={calls:phase==='Empty'?[]:[call],reconciliations:reconciliation?[{callId:'call',...reconciliation}]:[],responses:phase==='Succeeded'?[{id:'response',callId:'call',outputComplete:true}]:[],total:phase==='Empty'?0:1};
  else if(path==='/builder/semantic-responses/response')body={outputPayload:JSON.stringify(result)};
  else if(path==='/builder/semantic-calls/call:reconcile'){const b=req.postDataJSON();if(b.inputTokens!==11){await route.fulfill({status:422,json:{title:'已知Token不能改写'}});return;}reconciliation=b;retryDenied=false;await route.fulfill({status:201,json:b});return;}
  else if(path==='/builder/semantic-preparations/prep:retry'){if(retryDenied){await route.fulfill({status:422,json:{title:'原调用尚未确认结束，请先核对'}});return;}phase='Running';await route.fulfill({status:202,json:prep()});return;}
  else if(path==='/background-jobs/job:cancel'){phase='Cancelled';await route.fulfill({json:{status:phase}});return;}
  else {await route.fulfill({status:403,json:{title:'此受控页面辅助请求未配置'}});return;}
  await route.fulfill({headers:{ETag:'"1"'},json:body});
 });
 await page.goto('/');await page.getByRole('button',{name:'✧　辅助建库',exact:true}).click();await page.getByRole('button',{name:'核对此候选'}).click();const panel=page.getByRole('region',{name:'候选语义建议'});await expect(panel).toBeVisible();
 return {panel,posts,ready:()=>{phase='Succeeded'},call,reconciled:()=>reconciliation};
}
test('语义建议原理由/引用安全显示，后台完成保留候选未提交审核内容',async({page})=>{
 const f=await fixture(page);await page.getByLabel('候选名称',{exact:true}).fill('尚未提交的候选修改');await page.getByLabel('本次审核依据',{exact:true}).fill('我正在核对的人工依据');
 await f.panel.getByRole('button',{name:'对照原图与来源文字'}).click();const evidence=page.getByRole('dialog',{name:'原图与来源复核'});await expect(evidence).toContainText('受控原始引用 '+unsafe);await evidence.getByRole('button',{name:'关闭复核面板'}).click();await expect(page.getByLabel('候选名称',{exact:true})).toHaveValue('尚未提交的候选修改');await expect(page.getByLabel('本次审核依据',{exact:true})).toHaveValue('我正在核对的人工依据');
 await f.panel.getByRole('button',{name:'用本机模型准备语义建议'}).click();await expect(f.panel).toContainText('正在处理');f.ready();await f.panel.getByRole('button',{name:'刷新语义建议'}).click();
 await expect(f.panel.getByRole('region',{name:'原模型语义建议'})).toContainText('原模型中文理由 '+unsafe);await expect(f.panel).toContainText('受控原始引用 '+unsafe);await expect(page.getByLabel('候选名称',{exact:true})).toHaveValue('尚未提交的候选修改');await expect(page.getByLabel('本次审核依据',{exact:true})).toHaveValue('我正在核对的人工依据');
 await f.panel.getByText('原调用与返回记录',{exact:true}).click();await f.panel.getByRole('button',{name:'查看原模型返回'}).click();await expect(f.panel.locator('pre')).toContainText(unsafe);expect(await page.evaluate(()=>(window as any).__semanticUnsafe)).toBeUndefined();expect(f.posts.filter((p:any)=>p.path.includes(':decide'))).toHaveLength(0);
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});
test('未知调用禁止直接重试，错误保留；负责人核对保留已知用量和未知空值',async({page})=>{
 const f=await fixture(page,true,true);await f.panel.getByLabel('本次停止或重试依据').fill('已核对原输入，明确恢复');await f.panel.getByRole('button',{name:'按原输入人工重试'}).click();await expect(f.panel.getByRole('alert')).toContainText('原调用尚未确认结束');
 await f.panel.getByText('原调用与返回记录',{exact:true}).click();await f.panel.getByLabel('已实际确认本机模型调用结束').check();await f.panel.getByLabel('核对原因',{exact:true}).fill('已观察受控进程结束');await f.panel.getByLabel('实际结束依据').fill('受控进程实际结束记录');await f.panel.getByRole('button',{name:'追加语义调用核对'}).click();await expect(f.panel).toContainText('已追加负责人结束核对');expect(f.reconciled()).toMatchObject({providerFinished:true,inputTokens:11,outputTokens:null});expect(f.call.outputTokens).toBeNull();
 await f.panel.getByRole('button',{name:'按原输入人工重试'}).click();await expect(f.panel).toContainText('正在处理');expect(f.posts.filter((p:any)=>p.path.endsWith(':retry'))).toHaveLength(2);
});
test('普通内容成员可查看原调用但没有负责人结束核对按钮',async({page})=>{
 const f=await fixture(page,false,true);await f.panel.getByText('原调用与返回记录',{exact:true}).click();await expect(f.panel).toContainText('需家庭负责人核对调用是否实际结束');await expect(f.panel.getByRole('button',{name:'追加语义调用核对'})).toHaveCount(0);
});
