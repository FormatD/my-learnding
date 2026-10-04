import {test,expect} from '@playwright/test';
test('后台任务按游标翻页，失败保留原页，刷新回到第一页',async({page})=>{
 test.skip(!process.env.BACKGROUND_CURSOR_USER);
 await page.goto('/');await page.getByLabel('家长用户名').fill(process.env.BACKGROUND_CURSOR_USER!);await page.getByLabel('家长密码').fill(process.env.BACKGROUND_CURSOR_PASSWORD!);await page.getByRole('button',{name:'登录',exact:true}).click();
 const firstRead=page.waitForResponse(r=>r.url().includes('/background-jobs/window?')&&r.status()===200);await page.getByRole('button',{name:/辅助建库/}).click();const first=await (await firstRead).json();
 const panel=page.getByRole('region',{name:'后台任务保护'});await expect(panel.locator('article')).toHaveCount(20);
 const secondRead=page.waitForResponse(r=>r.url().includes('/background-jobs/window?')&&r.url().includes('cursor=')&&r.status()===200);await panel.getByRole('button',{name:'下一页'}).click();const second=await (await secondRead).json();expect(second.jobs.every((j:any)=>!first.jobs.some((f:any)=>f.id===j.id))).toBeTruthy();await expect(panel).toContainText('第 2 页');
 await page.route('**/api/v1/background-jobs/window?*',route=>route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({title:'受控后台分页失败'})}));await panel.getByRole('button',{name:'下一页'}).click();await expect(panel.getByRole('alert')).toHaveText('受控后台分页失败');await expect(panel).toContainText('第 2 页');await expect(panel.locator('article')).toHaveCount(20);await page.unroute('**/api/v1/background-jobs/window?*');
 await panel.getByRole('button',{name:'上一页'}).click();await expect(panel).toContainText('第 1 页');await expect(panel.getByRole('button',{name:'上一页'})).toBeDisabled();await panel.getByRole('button',{name:'下一页'}).click();await expect(panel).toContainText('第 2 页');await panel.getByRole('button',{name:'刷新后台任务'}).click();await expect(panel).toContainText('第 1 页');
});
