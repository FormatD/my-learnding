import {test,expect} from '@playwright/test';
test('家长投入分项填写与更正保留历史',async({page})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();await page.getByLabel('家长用户名').fill('burden-ui-'+Date.now());await page.getByLabel('家长密码').fill('private-burden-ui-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();
  await page.getByLabel('孩子昵称').fill('投入界面验收');await page.getByRole('button',{name:'创建学生',exact:true}).click();await page.getByRole('button',{name:/每周回顾/}).click();
  await expect(page.getByText('所选一周尚未记录投入，暂不能评价家长维护成本。')).toBeVisible();
  await page.getByLabel('实际投入（分钟，家长填写）').fill('7.5');await page.getByLabel('投入说明（可选）').fill('核对作业并调整安排');await page.getByRole('button',{name:'记录实际投入',exact:true}).click();
  await expect(page.getByText('所选一周已记录 1 天 · 日常维护 7.5 分钟 · 内容审核 0 分钟')).toBeVisible();
  await page.getByLabel('投入类别').selectOption('ContentReview');await page.getByLabel('实际投入（分钟，家长填写）').fill('12');await page.getByLabel('投入说明（可选）').fill('审核原创题目');await page.getByRole('button',{name:'记录实际投入',exact:true}).click();
  await expect(page.getByText('所选一周已记录 1 天 · 日常维护 7.5 分钟 · 内容审核 12 分钟')).toBeVisible();
  await page.getByRole('button',{name:'更正投入记录'}).first().click();await page.getByLabel('实际投入（分钟，家长填写）').fill('3');await page.getByLabel('投入更正原因').fill('扣除中途休息时间');await page.getByRole('button',{name:'保存投入更正',exact:true}).click();
  await expect(page.getByText('所选一周已记录 1 天 · 日常维护 3 分钟 · 内容审核 12 分钟')).toBeVisible();
  await page.reload();await page.getByRole('button',{name:/每周回顾/}).click();await expect(page.getByText('所选一周已记录 1 天 · 日常维护 3 分钟 · 内容审核 12 分钟')).toBeVisible();await page.getByText('保留的投入历史',{exact:true}).click();await expect(page.getByText(/7.5 分钟 · 核对作业并调整安排/)).toBeVisible();
  await page.screenshot({path:'../../test-results/parent-burden-tablet.png',fullPage:true});await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);await page.screenshot({path:'../../test-results/parent-burden-phone.png',fullPage:true});
});
