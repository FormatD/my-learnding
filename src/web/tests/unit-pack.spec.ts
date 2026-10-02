import {test,expect} from '@playwright/test';
test('单元原创题包按钮只创建待审核草稿',async({page,context})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();
  await page.getByLabel('家长用户名').fill('unitpack-ui-'+Date.now());await page.getByLabel('家长密码').fill('unitpack-private-test-2026');
  await page.getByRole('button',{name:'创建私有家庭'}).click();await page.getByLabel('孩子昵称').fill('单元题包验收');
  await page.getByRole('button',{name:'创建学生',exact:true}).click();
  await page.getByRole('button',{name:'准备单元原创题包（待审核）'}).click();
  await expect(page.getByText('原创单元草稿 · 混合运算（160题 / 15资源，印次与人工审核待确认）',{exact:true})).toBeVisible();
  const data=await (await context.request.get('/api/v1/content')).json();expect(data.releases).toHaveLength(0);expect(data.drafts).toHaveLength(1);
  const d=data.drafts[0];expect(d.status).toBe('Draft');expect(d.reviewedBy).toBeNull();const c=JSON.parse(d.payload);expect(c.questions).toHaveLength(160);expect(c.resources).toHaveLength(15);
  await page.screenshot({path:'../../test-results/unit-pack-tablet.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  await page.screenshot({path:'../../test-results/unit-pack-phone.png',fullPage:true});
});
