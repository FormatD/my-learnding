import {test,expect} from '@playwright/test';
test('建库总览分开统计真实候选与模拟候选，失败及处理中不冒充完成',async({page})=>{
 const sources=['a','b','c'].map(id=>({id,title:'来源'+id,text:'待核对教材文本',hash:'123456789012',usageScope:'FamilyOnly'}));
 const builder={provider:'LocalOmlx',sources,runs:[{id:'r3',sourceId:'c',type:'Candidates',provider:'LocalOmlx',model:'local',status:'Queued'},{id:'r2',sourceId:'b',type:'Candidates',provider:'LocalOmlx',model:'local',status:'Failed',error:'BUILDER_NEEDS_REPAIR'},{id:'r1',sourceId:'a',type:'Candidates',provider:'LocalOmlx',model:'local',status:'Completed'},{id:'mock',sourceId:'b',type:'Candidates',provider:'Mock',model:'fixture',status:'Completed'}],candidates:[{id:'real',runId:'r1',name:'待核对能力',status:'Pending'},{id:'simulation',runId:'mock',name:'模拟能力',status:'Accepted'}],libraries:[],attempts:[]};
 let writes=0;page.on('request',r=>{if(r.url().includes('/api/v1/')&&r.method()!=='GET')writes++});
 await page.route('**/api/v1/**',async route=>{const path=new URL(route.request().url()).pathname;let body:any={};
 if(path.endsWith('/me'))body={family:{ownerAccountId:'owner'},actor:{accountId:'editor',role:'Parent',roles:'ContentEditor'}};
 else if(path.endsWith('/students'))body=[];
 else if(path.endsWith('/builder'))body=builder;
 else if(path.endsWith('/content'))body={drafts:[],releases:[]};
 else if(path.includes('/background-jobs'))body={jobs:[{inputRef:'r3',type:'BuilderCandidates',status:'Running'}],attempts:[],total:1};
 else if(path.includes('/budget'))body={};
 else {await route.fulfill({status:400,json:{title:'测试未配置此辅助记录'}});return;}
 await route.fulfill({json:body});});
 await page.goto('/');await page.getByRole('button',{name:/辅助建库/}).click();const region=page.getByRole('region',{name:'建库流程与进度'});
 await expect(region).toContainText('1 / 3 份来源生成成功');await expect(region).toContainText('0 / 1 项已审核');await expect(region).toContainText('尚未形成可用的正式内容');await expect(region).toContainText('正在处理或排队 1 项');await expect(region).toContainText('候选结构或来源校验未通过');await expect(region).toContainText('模拟任务 1 次、模拟候选 1 项');
 await region.getByRole('button',{name:'审核候选',exact:true}).click();await expect(page.getByRole('region',{name:'候选逐项审核'})).toBeVisible();
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect(writes).toBe(0);
});
