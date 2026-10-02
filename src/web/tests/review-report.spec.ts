import {test,expect} from '@playwright/test';
test('实际到期复习的通过率、更正与往周空值在平板和窄屏显示',async({page,context})=>{
  test.skip(!process.env.REVIEW_REPORT_STUDENT,'通过隔离计划验收的 --browser 选项提供真实日程和作答');
  let etag='';async function call(path:string,body?:unknown){const r=await context.request.fetch('/api/v1'+path,{method:body===undefined?'GET':'POST',headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID(),'If-Match':etag},...(body===undefined?{}:{data:body})});expect(r.ok()).toBeTruthy();etag=r.headers()['etag']||etag;return r.json();}
  await call('/auth/login',{userName:process.env.PLAN_TEST_USERNAME,password:process.env.PLAN_TEST_PASSWORD});
  const sid=process.env.REVIEW_REPORT_STUDENT!;
  await page.goto('/');await page.getByLabel('切换学生').selectOption(sid);await page.getByRole('button',{name:/每周回顾/}).click();
  const card=page.getByRole('region',{name:'复习通过率'});await expect(card.getByText('0 / 1 · 0%')).toBeVisible();await card.getByText('查看逐次复习判分依据',{exact:true}).click();await expect(card.getByRole('heading',{name:'首次答案未独立答对'})).toBeVisible();await expect(card.getByText('实际复习遇题 1 次，待判分 0 次。')).toBeVisible();
  const report=await call('/students/'+sid+'/weekly-summary');const aid=report.reviewPass.items[0].attemptId;
  for(let n=0;n<100;n++){if((await call('/students/'+sid+'/mastery')).pending===0)break;await page.waitForTimeout(100);}
  const grade={result:'Correct',reason:'隔离浏览器验收：追加更正首次判分'};const preview=await call('/attempts/'+aid+'/grading-preview',grade);await call('/attempts/'+aid+'/grading-revisions',{...grade,previewHash:preview.previewHash});
  await page.getByRole('button',{name:'查看所选一周',exact:true}).click();await expect(card.getByText('1 / 1 · 100%')).toBeVisible();await expect(card.getByRole('heading',{name:'已判分且独立通过'})).toBeVisible();
  await page.screenshot({path:'../../test-results/review-report-tablet.png',fullPage:true});await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);await page.screenshot({path:'../../test-results/review-report-phone.png',fullPage:true});
  await page.getByRole('button',{name:'前一周',exact:true}).click();await expect(card.getByText('0 / 0 · 暂无已判分复习')).toBeVisible();
});
