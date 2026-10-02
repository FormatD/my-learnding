import {test,expect} from '@playwright/test';
import {localDate} from '../src/api';

test('预算可执行率保留已发布分母，家长顺延依据可回看',async({page,context})=>{
  let etag='';
  async function call(path:string,body?:unknown,method=body===undefined?'GET':'POST'){
    const response=await context.request.fetch('/api/v1'+path,{method,headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID(),'If-Match':etag},...(body===undefined?{}:{data:body})});
    expect(response.ok()).toBeTruthy();etag=response.headers()['etag']||etag;return response.json();
  }
  await call('/auth/register',{userName:'budget-ui-'+Date.now(),password:'budget-private-browser-2026'});
  const s=await call('/students',{name:'预算回顾界面验收'});
  const draft=await call('/content/fixture',{});await call('/content/drafts/'+draft.id+':review',{});const preview=await call('/content/drafts/'+draft.id+'/preview');const release=await call('/content/drafts/'+draft.id+':publish',{previewHash:preview.hash});await call('/students/'+s.id+'/content/'+release.id+':bind',{});
  const today=localDate('Asia/Shanghai');const rev=await call('/students/'+s.id+'/plans/'+today+':generate',{});
  const tasks=[];
  for(const title of ['预计内完成','超过预计','家长顺延','未执行'])tasks.push(await call('/plans/'+rev.id+'/tasks',{title,minutes:5,mandatory:true,type:'Resource',resourceRef:'在纸上阅读原创例题'}));
  const view=await call('/students/'+s.id+'/plans/'+today);await call('/plans/'+rev.id+':publish',{previewHash:view.revision.inputHash});
  for(const [index,minutes] of [[0,4],[1,6]]){await call('/tasks/'+tasks[index].id+':transition',{status:'InProgress'});await call('/tasks/'+tasks[index].id+':transition',{status:'Completed',actualMinutes:minutes});}
  const targetDate=new Date(today+'T12:00:00Z');targetDate.setUTCDate(targetDate.getUTCDate()+1);const target=targetDate.toISOString().slice(0,10);const reason='家长核对学校作业后顺延 <script>window.__budgetUnsafe=1</script>';
  await call('/tasks/'+tasks[2].id+':defer',{date:target,reason});
  await page.goto('/');await page.getByRole('button',{name:/每周回顾/}).click();
  const card=page.getByRole('region',{name:'预算可执行率'});
  await expect(card.getByText('2 / 4 · 50%')).toBeVisible();await expect(card.getByText('预计时长内完成 1 项，明确家长顺延 1 项；重叠只计一次。')).toBeVisible();
  await card.getByText('查看逐项预算依据',{exact:true}).click();
  await expect(card.getByRole('heading',{name:'超过预计 · 未计入'})).toBeVisible();await expect(card.getByRole('heading',{name:'未执行 · 未计入'})).toBeVisible();await expect(card.getByText(reason,{exact:false})).toBeVisible();
  expect(await page.evaluate(()=>(window as any).__budgetUnsafe)).toBeUndefined();
  await page.screenshot({path:'../../test-results/budget-report-tablet.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);await page.screenshot({path:'../../test-results/budget-report-phone.png',fullPage:true});
  await page.getByRole('button',{name:'前一周',exact:true}).click();await expect(card.getByText('0 / 0 · 暂无已发布任务')).toBeVisible();
});
