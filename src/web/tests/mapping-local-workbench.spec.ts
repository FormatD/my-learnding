import {test,expect} from '@playwright/test';
const unsafe='<script>window.__mappingLocalUnsafe=1</script>';
async function fixture(page:any,owner=true,reviewed=false){
 const k={id:'kc',revisionId:'kc-r',name:'受控能力',behavior:'受控可观察行为',boundary:'受控范围'};
 const questions=Array.from({length:9},(_,i)=>({id:'q'+i,revisionId:'qr'+i,stem:'受控题目 '+i,answer:'5',explanation:'受控解释',policy:'SingleKC',mappings:[]}));
 const catalog={kcs:[k],questions,resources:[],lessons:[],relations:[]};
 const source={id:'draft',title:'受控来源草稿',version:1,payload:JSON.stringify(catalog),status:'Draft'};
 const release={id:'library',number:1,hash:'controlled-library-hash',payload:source.payload,withdrawn:false};
 const proposal={evidencePolicy:'SingleKC',items:[{kcId:k.id,kcRevisionId:k.revisionId,role:'Primary',coverageWeight:1,evidenceShare:1,evidenceMode:'WholeItem',step:null,sequence:1,modelScore:null,sourceRefs:['draft:draft/Question:qr0']}]};
 const model={ownerType:'Question',ownerId:'q0',ownerRevisionId:'qr0',decision:'Propose',reason:'受控模型原说明 '+unsafe,evidenceQuotes:['受控题目 0 '+unsafe],proposal};
 function detail(id:string){return {run:{id,provider:'LocalOmlx',model:'controlled-local',sourcePayload:source.payload,sourceDraftId:source.id,sourceDraftVersion:1,libraryReleaseId:release.id},library:[k],suggestions:[{id:id+'-s',ownerType:'Question',ownerId:'q0',ownerRevisionId:'qr0',ownerTitle:'受控题目 0',status:reviewed?'Accepted':'Pending',evidencePolicy:proposal.evidencePolicy,suggestedItems:JSON.stringify(proposal.items),matches:'[]',validationFlags:JSON.stringify(['LocalModelUnreviewed','HumanReviewRequired','NotQualityEvaluated']),modelResultPayload:JSON.stringify(model)}],decisions:reviewed?[{id:'decision',suggestionId:id+'-s',decision:'Accept',reason:'受控人工校正依据',reviewedAt:new Date().toISOString(),correctedPayload:JSON.stringify(proposal)}]:[],sets:[],items:[],quality:{suggested:1,pending:reviewed?0:1,accepted:reviewed?1:0,rejected:0,corrected:0,note:'没有独立人工质量评测'}};}
 let phase='empty',write:any=null,reconciled:any=null;
 const call={id:'call',preparationId:'prep',status:'Failed',budgetState:'Unresolved',model:'controlled-local',startedAt:new Date().toISOString(),retryRound:0,attemptNumber:1,billingStatus:'Unknown',inputTokens:7,outputTokens:null,errorCode:'PROVIDER_CALL_FAILED',elapsedMilliseconds:1000};
 const prep=()=>({id:'prep',jobId:'job',runId:'new-run',sourceTitle:source.title,status:phase==='ready'?'Succeeded':'Running'});
 await page.route('**/api/v1/**',async(route:any)=>{
  const req=route.request(),url=new URL(req.url()),path=url.pathname.replace('/api/v1','');let body:any;
  if(path==='/me')body={family:{ownerAccountId:'owner'},actor:{accountId:owner?'owner':'editor',role:'Parent',roles:'ContentEditor'}};
  else if(path==='/students')body=[];
  else if(path==='/content')body={drafts:[source],releases:[release]};
  else if(path==='/builder/mapping-preparations'&&req.method()==='POST'){write=req.postDataJSON();phase='running';await route.fulfill({status:202,headers:{ETag:'"2"'},json:prep()});return;}
  else if(path==='/builder/mapping-preparations')body=phase==='empty'?[]:[prep()];
  else if(path==='/builder/mapping-runs')body=[{id:'old-run',provider:'LocalOmlx',sourceTitle:source.title,createdAt:new Date().toISOString()},...(phase==='ready'?[{id:'new-run',provider:'LocalOmlx',sourceTitle:source.title,createdAt:new Date().toISOString()}]:[])];
  else if(path.startsWith('/builder/mapping-runs/'))body=detail(path.split('/').at(-1)!);
  else if(path==='/builder/mapping-calls')body={calls:[call],reconciliations:reconciled?[{callId:call.id,...reconciled}]:[],page:1,pageSize:20,total:1};
  else if(path==='/builder/mapping-calls/call:reconcile'){reconciled=req.postDataJSON();await route.fulfill({status:201,headers:{ETag:'"3"'},json:reconciled});return;}
  else if(path.includes('mapping-previews')||path.includes('independent-mappings'))body=[];
  else {await route.fulfill({status:403,json:{title:'受控页面未配置此辅助请求'}});return;}
  await route.fulfill({headers:{ETag:'"1"'},json:body});
 });
 await page.goto('/');const work=page.getByRole('region',{name:'映射建议与批量审核'});await expect(work).toBeVisible();
 return {work,ready:()=>{phase='ready'},write:()=>write,reconciled:()=>reconciled,call};
}
test('本机映射选择保留超限选择，理由引用安全显示，后台完成不覆盖未提交校正',async({page})=>{
 const f=await fixture(page),work=f.work;
 await work.getByText('准备一批建议',{exact:true}).click();await work.getByLabel('准备方式').selectOption('Mock');await work.getByLabel('映射来源草稿').selectOption('draft');await work.getByLabel('对照的正式能力库').selectOption('library');
 const choices=work.getByRole('checkbox',{name:/^题目 · 第/});for(let i=0;i<9;i++)await choices.nth(i).check();
 await work.getByLabel('准备方式').selectOption('LocalOmlx');await expect(work.getByText('本机模型每批最多8个对象，请调整本批选择。',{exact:true})).toBeVisible();await expect(work.getByRole('button',{name:'用本机模型准备映射建议'})).toBeDisabled();await expect(choices.nth(8)).toBeChecked();expect(f.write()).toBeNull();
 await choices.nth(8).uncheck();await work.getByRole('button',{name:'用本机模型准备映射建议'}).click();await expect.poll(()=>f.write()).not.toBeNull();expect(f.write().provider).toBe('LocalOmlx');expect(f.write().owners).toHaveLength(8);
 await work.getByLabel('查看建议运行').selectOption('old-run');const original=work.getByRole('region',{name:'原模型理由与引用'});await expect(original).toContainText('受控模型原说明 '+unsafe);await expect(original).toContainText('受控题目 0 '+unsafe);
 await work.getByLabel('本项审核依据').fill('尚未提交的人工核对');f.ready();await expect(work.getByText('另一批建议已完成。当前未提交校正保留；请完成或刷新当前审核后再查看新批次。',{exact:true})).toBeVisible();await expect(work.getByLabel('本项审核依据')).toHaveValue('尚未提交的人工核对');await expect(work.getByLabel('查看建议运行')).toHaveValue('old-run');
 await work.getByRole('button',{name:'查看映射调用记录',exact:true}).click();await expect(work.getByRole('region',{name:'映射模型调用记录'})).toContainText('7 / 未记录');await expect(work.getByLabel('本项审核依据')).toHaveValue('尚未提交的人工核对');expect(await page.evaluate(()=>(window as any).__mappingLocalUnsafe)).toBeUndefined();
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:'../../test-results/mapping-local-workbench-phone.png',fullPage:true});
});
test('负责人核对不修改原已知用量，未知留空；普通成员不能核对',async({page})=>{
 const f=await fixture(page);await f.work.getByRole('button',{name:'查看映射调用记录',exact:true}).click();const ledger=f.work.getByRole('region',{name:'映射模型调用记录'});
 await expect(ledger).toContainText('7 / 未记录');await ledger.getByLabel('已实际确认本机模型调用结束').check();await ledger.getByLabel('核对原因').fill('已核对受控本机任务结束');await ledger.getByLabel('本机任务或进程结束依据').fill('受控结束记录');await ledger.getByRole('button',{name:'追加映射调用核对'}).click();
 await expect(ledger.getByText('已追加负责人核对，原调用记录及未知值保留。',{exact:true})).toBeVisible();expect(f.reconciled()).toMatchObject({providerFinished:true,inputTokens:null,outputTokens:null});expect(f.call.inputTokens).toBe(7);expect(f.call.outputTokens).toBeNull();await expect(ledger.getByRole('button',{name:'追加映射调用核对'})).toHaveCount(0);
 await page.unroute('**/api/v1/**');const editor=await fixture(page,false);await editor.work.getByRole('button',{name:'查看映射调用记录',exact:true}).click();const readOnly=editor.work.getByRole('region',{name:'映射模型调用记录'});await expect(readOnly).toContainText('需要家庭负责人确认本机模型结束后核对。');await expect(readOnly.getByRole('button',{name:'追加映射调用核对'})).toHaveCount(0);
});

test('已审核项仍可核对原模型说明，原说明与人工决定分别显示',async({page})=>{
 const f=await fixture(page,true,true);await f.work.getByLabel('查看建议运行').selectOption('old-run');await f.work.getByText('查看已保存的决定与草稿',{exact:true}).click();await f.work.getByText('查看本项原模型理由与引用',{exact:true}).click();
 const original=f.work.getByRole('group',{name:'已审核项原模型说明'});await expect(original).toContainText('受控模型原说明 '+unsafe);await expect(original).toContainText('受控题目 0 '+unsafe);await expect(f.work).toContainText('受控人工校正依据');expect(await page.evaluate(()=>(window as any).__mappingLocalUnsafe)).toBeUndefined();
});
