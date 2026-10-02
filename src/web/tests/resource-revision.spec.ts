import {test,expect} from '@playwright/test';

test('资源页面修改创建新修订，审核发布后保留旧资源快照',async({page,context})=>{
  let etag='';
  async function call(path:string,body?:unknown){
    const response=await context.request.fetch('/api/v1'+path,{method:body===undefined?'GET':'POST',headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID(),'If-Match':etag},...(body===undefined?{}:{data:body})});
    expect(response.ok()).toBeTruthy();etag=response.headers()['etag']||etag;return response.json();
  }
  await call('/auth/register',{userName:'resource-ui-'+Date.now(),password:'resource-private-browser-2026'});
  await call('/students',{name:'资源修订界面验收'});
  const draft=await call('/content/fixture',{});
  await call('/content/drafts/'+draft.id+':review',{});
  const preview=await call('/content/drafts/'+draft.id+'/preview');
  const old=await call('/content/drafts/'+draft.id+':publish',{previewHash:preview.hash});
  const original=JSON.parse(old.payload).resources[0];
  await page.goto('/');
  await page.getByRole('button',{name:/内容与发布/}).click();
  await page.getByRole('button',{name:'创建新修订',exact:true}).click();
  await page.getByRole('button',{name:'编辑',exact:true}).click();
  const editor=page.getByRole('region',{name:'内容草稿编辑器'});
  const resource=editor.locator('[aria-label="资源 1"]');
  const instruction='核对纸笔讲解步骤 <script>window.__resourceUnsafe=1</script>';
  await resource.getByLabel('纸笔材料与执行说明').fill(instruction);
  await resource.getByLabel('预计分钟').fill('12');
  await editor.getByRole('button',{name:'保存并退回待审核'}).click();
  await expect(page.getByText('草稿已保存，需要重新审核。',{exact:true})).toBeVisible();
  const saved=(await call('/content')).drafts.find((d:any)=>d.status!=='Published');
  const changed=JSON.parse(saved.payload).resources[0];
  expect(changed.id).toBe(original.id);expect(changed.revisionId).not.toBe(original.revisionId);
  expect(changed.paperReference).toBe(instruction);expect(changed.minutes).toBe(12);
  await page.getByRole('button',{name:'审核答案与映射后发布',exact:true}).click();
  await expect(page.getByText('内容已发布，可以绑定学生。',{exact:true})).toBeVisible();
  const releases=(await call('/content')).releases;
  expect(releases).toHaveLength(2);expect(releases.find((r:any)=>r.id===old.id).payload).toBe(old.payload);
  expect(JSON.parse(releases.find((r:any)=>r.id!==old.id).payload).resources[0]).toEqual(changed);
  expect(await page.evaluate(()=>(window as any).__resourceUnsafe)).toBeUndefined();
});
