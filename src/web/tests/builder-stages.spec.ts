import {test,expect} from '@playwright/test';
const unsafe='<script>window.__stageUnsafe=1</script>';
test('阶段记录全部分页、相关数据独立翻页、安全展示原输入输出且查看不写入',async({page})=>{
 const posts:string[]=[];const rows=Array.from({length:23},(_,i)=>({id:'task-'+i,title:'受控来源 '+i,status:i===0?'Failed':'Completed',provider:i===1?'Mock':'LocalOmlx',createdAt:'2026-10-07T00:00:00Z'}));
 await page.route('**/api/v1/**',async route=>{
  const req=route.request(),url=new URL(req.url()),path=url.pathname.replace('/api/v1','');if(req.method()!=='GET')posts.push(path);let body:any;
  if(path==='/me')body={family:{ownerAccountId:'owner'},actor:{accountId:'owner',role:'Parent',roles:'ContentEditor'}};
  else if(path==='/students')body=[];
  else if(path==='/builder')body={sources:[],runs:[],attempts:[],candidates:[],libraries:[],provider:'controlled'};
  else if(path==='/content')body={drafts:[],releases:[]};
  else if(path==='/background-jobs/window')body={jobs:[],nextCursor:null};
  else if(path.startsWith('/builder/reference-images/'))body=[{id:'source',title:'受控教材原页',text:'原识别文字 '+unsafe,chunks:[{id:'chunk',locator:'段落1',text:'保存的分段'}],pages:[{id:'image-7',page:7,printedPage:'书内第3页',documentHash:'a'.repeat(64),imageHash:'b'.repeat(64),url:'/api/v1/content/source-images/image-7'},{id:'image-8',page:8,printedPage:'书内第4页',documentHash:'a'.repeat(64),imageHash:'c'.repeat(64),url:'/api/v1/content/source-images/image-8'}]}];
  else if(path.startsWith('/content/source-images/')){await route.fulfill({contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==','base64')});return;}
  else if(path==='/builder/stages')body=[{id:'extraction',title:'3 · 候选生成与召回',total:23,notice:'全部历史任务'},{id:'semantic',title:'5 · 语义建议',total:1,notice:'原输入和原返回'}];
  else if(path==='/builder/stages/extraction/tasks'){const p=Number(url.searchParams.get('page')||1);body={stage:'extraction',page:p,pageSize:20,total:23,items:rows.slice((p-1)*20,p*20)};}
  else if(path.startsWith('/builder/stages/extraction/tasks/task-')){const p=Number(url.searchParams.get('relatedPage')||1);body={stage:'extraction',id:path.split('/').at(-1),record:{model:'受控模型',inputHash:'原输入摘要',modelConfigPayload:JSON.stringify({sourceText:unsafe}),status:'Failed',error:'受控错误'},data:[{id:'calls',title:'每次模型调用与用量',total:21,page:p,pageSize:20,items:p===1?[{id:'call-1',status:'Failed',outputTokens:null}]:[{id:'call-21',status:'Returned',outputTokens:17}]},{id:'candidates',title:'原候选、召回及审核数据',total:0,page:1,pageSize:20,items:[]}]};}
  else if(path==='/builder/stages/semantic/tasks')body={stage:'semantic',page:1,pageSize:20,total:1,items:[{id:'semantic-task',title:'原语义任务',status:'Succeeded',provider:'LocalOmlx',createdAt:'2026-10-07T00:00:00Z'}]};
  else if(path==='/builder/stages/semantic/tasks/semantic-task')body={stage:'semantic',id:'semantic-task',record:{snapshot:'原固定输入 '+unsafe},data:[{id:'responses',title:'原模型返回与完整性',total:1,page:1,pageSize:20,items:[{id:'response',outputPayload:'原模型返回 '+unsafe,outputComplete:true}]}]};
  else {await route.fulfill({status:403,json:{title:'受控页面辅助请求未配置'}});return;}
  await route.fulfill({headers:{ETag:'"1"'},json:body});
 });
 await page.goto('/');await page.getByRole('button',{name:'✧　辅助建库',exact:true}).click();const panel=page.getByRole('region',{name:'建库阶段任务'});
 await expect(panel.getByRole('button',{name:'查看任务数据'})).toHaveCount(20);await expect(panel).toContainText('模拟流程');await expect(panel).toContainText('处理停止');await panel.getByRole('button',{name:'下一页任务',exact:true}).click();await expect(panel.getByRole('button',{name:'查看任务数据'})).toHaveCount(3);await expect(panel).toContainText('23 条 · 第 2 页');
 await panel.getByRole('button',{name:'查看任务数据'}).first().click();const dialog=page.getByRole('dialog',{name:'任务数据与原图复核'}),detail=dialog.getByRole('article',{name:'当前阶段任务数据'});await expect(detail).toContainText('原输入摘要');await expect(dialog.getByRole('img')).toBeVisible();await dialog.getByRole('button',{name:'下一张原图'}).click();await expect(dialog.getByRole('img')).toHaveAttribute('src','/api/v1/content/source-images/image-8');await dialog.getByRole('button',{name:'放大',exact:true}).click();await expect(dialog).toContainText('125%');expect(await dialog.evaluate(el=>{const r=el.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight})).toBe(true);await detail.getByText('展开相关字段',{exact:true}).click();await expect(detail).toContainText(unsafe);await detail.getByRole('button',{name:'下一页每次模型调用与用量'}).click();await expect(detail).toContainText('call-21');await expect(detail).toContainText('没有保存这类记录');
 await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await panel.getByRole('button',{name:'5 · 语义建议 · 1'}).click();await expect(detail).toHaveCount(0);await panel.getByRole('button',{name:'查看任务数据'}).click();await dialog.getByText('记录 · response',{exact:true}).click();await expect(dialog).toContainText('原模型返回 '+unsafe);
 expect(await page.evaluate(()=>(window as any).__stageUnsafe)).toBeUndefined();expect(posts).toEqual([]);
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});
test('切换任务拒绝失败后保留明确错误，不把旧数据当成新结果',async({page})=>{
 await page.route('**/api/v1/**',async route=>{
  const path=new URL(route.request().url()).pathname.replace('/api/v1','');let body:any;
  if(path==='/me')body={family:{ownerAccountId:'owner'},actor:{accountId:'owner',role:'Parent',roles:'ContentEditor'}};
  else if(path==='/students')body=[];else if(path==='/builder')body={sources:[],runs:[],attempts:[],candidates:[],libraries:[],provider:'controlled'};else if(path==='/content')body={drafts:[],releases:[]};else if(path==='/background-jobs/window')body={jobs:[],nextCursor:null};
  else if(path.startsWith('/builder/reference-images/'))body=[];
  else if(path==='/builder/stages')body=[{id:'extraction',title:'候选生成',total:1,notice:''}];else if(path==='/builder/stages/extraction/tasks')body={page:1,total:1,items:[{id:'failed',title:'受控任务',status:'Failed',provider:'Mock',createdAt:'2026-10-07T00:00:00Z'}]};
  else {await route.fulfill({status:404,json:{title:'原阶段记录已不可用'}});return;}
  await route.fulfill({headers:{ETag:'"1"'},json:body});
 });
 await page.goto('/');await page.getByRole('button',{name:'✧　辅助建库',exact:true}).click();const panel=page.getByRole('region',{name:'建库阶段任务'});await panel.getByRole('button',{name:'查看任务数据'}).click();await expect(panel.getByRole('alert')).toContainText('原阶段记录已不可用');await expect(panel.getByRole('article',{name:'当前阶段任务数据'})).toHaveCount(0);
});
