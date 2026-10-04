import {test,expect} from '@playwright/test';
test.skip(!process.env.BUILDER_ALIAS_USER,'由隔离接口验收提供实际审核别名');
test('固定审核别名展示实际来源与人工核对提示',async({page})=>{
  await page.goto('/');await page.getByLabel('家长用户名').fill(process.env.BUILDER_ALIAS_USER!);await page.getByLabel('家长密码').fill(process.env.BUILDER_ALIAS_PASSWORD!);await page.getByRole('button',{name:'登录',exact:true}).click();await page.getByRole('button',{name:/辅助建库/}).click();
  const region=page.getByRole('region',{name:'候选逐项审核'});
  const row=region.locator('.content-row').filter({has:page.getByRole('heading',{name:process.env.BUILDER_ALIAS_NAME!,exact:true})}).filter({has:page.getByRole('button',{name:'核对此候选'})});
  // Several immutable runs retain the same source/name. Select the actual new pending result by its review request.
  await expect(row).toHaveCount(4);const buttons=await row.getByRole('button',{name:'核对此候选'}).all();let found=false;
  for(const button of buttons){const response=page.waitForResponse(r=>r.url().includes('/review-context')&&r.request().method()==='GET');await button.click();const result=await response;if(result.url().includes(process.env.BUILDER_ALIAS_ID!)){found=true;break;}}
  expect(found).toBe(true);const current=region.getByRole('article',{name:'当前候选核对'});
  await expect(current.getByText(/本次固定 1 项已审核别名/)).toBeVisible();await current.getByText('相近能力与支持题目（模拟排序）',{exact:true}).click();await expect(current.getByText(/命中已审核别名，优先列出/)).toContainText(process.env.BUILDER_ALIAS_SOURCE!);await expect(current.getByText(/原模拟相似分数/).first()).toBeVisible();await expect(current.getByRole('button',{name:'关联已有能力'}).first()).toBeDisabled();
  await page.setViewportSize({width:390,height:844});await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);await current.screenshot({path:'../../test-results/builder-alias-phone.png'});
});
