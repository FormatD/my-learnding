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
  await expect(current.getByText(/本次固定 1 项已审核别名/)).toBeVisible();await current.getByText('相近能力与支持题目（模拟排序）',{exact:true}).click();await expect(current.getByText(/命中已审核别名，优先列出/)).toContainText(process.env.BUILDER_ALIAS_NAME!);await expect(current.getByText(/关键词匹配：/).first()).toBeVisible();await expect(current.getByText(/本次综合关键词与模拟相似排序/)).toBeVisible();await expect(current.getByText(/原模拟相似分数/).first()).toBeVisible();await expect(current.getByRole('button',{name:'关联已有能力'}).first()).toBeDisabled();
  const editedName='当前候选校正，查看来源后仍保留',reason='先核对原审核别名依据，当前候选尚未提交。';await current.getByLabel('候选名称',{exact:true}).fill(editedName);await current.getByLabel('本次审核依据').fill(reason);
  if(process.env.BUILDER_ALIAS_CASE){
    const lookup=page.waitForResponse(r=>r.url().endsWith(`/builder/candidates/${process.env.BUILDER_ALIAS_SOURCE!}/review-context`));await current.getByRole('button',{name:'核对别名审核来源',exact:true}).click();expect((await lookup).status()).toBe(200);
    const source=current.getByRole('region',{name:'别名原审核来源'});
    if(process.env.BUILDER_ALIAS_CASE==='legacy'){await expect(source.getByText('审核依据：原记录未保存审核依据，不补造。',{exact:true})).toBeVisible();await expect(source.getByText('原记录未保存审核时间，不补造。',{exact:true})).toBeVisible();}
    else{await expect(current.getByText('原审核记录与本次固定别名来源不一致，请核对；不会替换本次检索依据。',{exact:true})).toBeVisible();await expect(source).toHaveCount(0);}
    await expect(current.getByLabel('候选名称',{exact:true})).toHaveValue(editedName);await expect(current.getByLabel('本次审核依据')).toHaveValue(reason);await page.setViewportSize({width:390,height:844});await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);await current.screenshot({path:`../../test-results/builder-alias-source-${process.env.BUILDER_ALIAS_CASE}.png`});return;
  }
  await page.evaluate(async()=>{const response=await fetch('/api/v1/me');const created=await fetch('/api/v1/students',{method:'POST',headers:{'Content-Type':'application/json','X-Learning-Request':'1','If-Match':response.headers.get('etag')!,'Idempotency-Key':crypto.randomUUID()},body:JSON.stringify({name:'别名来源读取前真实另一页写入'})});if(created.status!==201)throw new Error('另一页写入失败');});
  const lookup=page.waitForResponse(r=>r.url().endsWith(`/builder/candidates/${process.env.BUILDER_ALIAS_SOURCE!}/review-context`));await current.getByRole('button',{name:'核对别名审核来源',exact:true}).click();expect((await lookup).status()).toBe(200);
  const source=current.getByRole('region',{name:'别名原审核来源'});await expect(source.getByText('审核依据：隔离测试经实际审核接口确认别名来源',{exact:true})).toBeVisible();await expect(source.getByText('实际冻结检索来源',{exact:true})).toBeVisible();await expect(source.locator('blockquote')).toContainText('独立计算混合运算');await expect(current.getByLabel('候选名称',{exact:true})).toHaveValue(editedName);await expect(current.getByLabel('本次审核依据')).toHaveValue(reason);
  const stale=page.waitForResponse(r=>r.url().endsWith(`/builder/candidates/${process.env.BUILDER_ALIAS_ID!}:decide`));await current.getByRole('button',{name:'拒绝',exact:true}).click();expect((await stale).status()).toBe(412);await expect(current.getByLabel('候选名称',{exact:true})).toHaveValue(editedName);await expect(current.getByLabel('本次审核依据')).toHaveValue(reason);await expect(source).toBeVisible();
  await page.setViewportSize({width:390,height:844});await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);await current.screenshot({path:'../../test-results/builder-alias-phone.png'});
});
