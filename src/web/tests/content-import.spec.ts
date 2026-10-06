import {test,expect} from '@playwright/test';
import {execFileSync} from 'node:child_process';
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';
test('题包先预览再保存，实际提交后的响应失败原命令重试，不发布或丢失草稿编辑',async({page,context})=>{
 let version='';
 async function call(path:string,body?:unknown){const r=await context.request.fetch('/api/v1'+path,{method:body===undefined?'GET':'POST',headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID(),'If-Match':version},...(body===undefined?{}:{data:body})});expect(r.ok()).toBeTruthy();version=r.headers()['etag']||version;return r.json();}
 await call('/auth/register',{userName:'import-content-'+Date.now(),password:'test-content-'+crypto.randomUUID()});
 const fixture=await call('/content/fixture',{}),catalog=JSON.parse(fixture.payload),before=await call('/content');
 await page.goto('/');await page.getByRole('button',{name:/内容与发布/}).click();
 const importer=page.getByRole('region',{name:'导入本地题包'}),file=importer.getByLabel('选择本地题包文件');await expect(file).toBeEnabled();
 let posts=0;page.on('request',r=>{if(r.url().endsWith('/content/drafts')&&r.method()==='POST')posts++;});
 await file.setInputFiles({name:'bad.json',mimeType:'application/json',buffer:Buffer.from('{"title":"坏文件","catalog":{"kcs":[]}}')});
 await expect(importer.getByRole('alert')).toBeVisible();expect(posts).toBe(0);
 await file.setInputFiles({name:'large.json',mimeType:'application/json',buffer:Buffer.alloc(2_000_001)});await expect(importer.getByRole('alert')).toContainText('最多 2 MB');expect(posts).toBe(0);
 const payload={title:'导入的原创题包 · 待审核',catalog};
 await file.setInputFiles({name:'pack.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(payload))});
 await expect(importer.getByText('9 个能力 · 20 道题 · 9 份资源 · 4 个课时',{exact:true})).toBeVisible();expect(posts).toBe(0);
 let lost=false;const commands:{body:string|null,key:string|undefined,version:string|undefined}[]=[];
 await page.route('**/api/v1/content/drafts',async route=>{
  if(route.request().method()!=='POST'){await route.continue();return;}
  commands.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],version:route.request().headers()['if-match']});
  if(!lost){lost=true;const real=await route.fetch();expect(real.ok()).toBeTruthy();await route.fulfill({status:503,contentType:'application/problem+json',body:JSON.stringify({title:'测试：提交成功但响应暂未收到'})});}else await route.continue();
 });
 await importer.getByRole('button',{name:'保存为待审核草稿',exact:true}).click();await expect(importer.getByRole('alert')).toContainText('响应暂未收到');
 await expect(importer.getByRole('heading',{name:payload.title,exact:true})).toBeVisible();
 await importer.getByRole('button',{name:'保存为待审核草稿',exact:true}).click();
 const editor=page.getByRole('region',{name:'内容草稿编辑器'});await expect(editor).toBeVisible();await expect(file).toBeDisabled();expect(commands).toHaveLength(2);expect(commands[1]).toEqual(commands[0]);
 const after=await call('/content');expect(after.drafts.length).toBe(before.drafts.length+1);expect(after.releases).toEqual(before.releases);const saved=after.drafts.find((d:any)=>d.title===payload.title);expect(saved.status).toBe('Draft');expect(JSON.parse(saved.payload)).toEqual(catalog);expect(after.drafts.find((d:any)=>d.id===fixture.id)).toEqual(fixture);
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
 await editor.getByRole('button',{name:'取消',exact:true}).click();await expect(file).toBeEnabled();await expect(importer.getByRole('button',{name:'保存为待审核草稿',exact:true})).toHaveCount(0);
});
test('两页除法原创题包能经真实导入保存，步骤测量和最多至少边界保留，仍待审核',async({page,context})=>{
 const root=resolve('../..'),output=resolve(root,'.local/textbook-workflow/division-original-draft');execFileSync('python3',[resolve(root,'scripts/prepare_division_pack.py'),'--output',output]);const buffer=readFileSync(resolve(output,'content-pack.json')),pack=JSON.parse(buffer.toString());
 const registered=await context.request.post('/api/v1/auth/register',{headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID()},data:{userName:'division-import-'+Date.now(),password:'test-only-'+crypto.randomUUID()}});expect(registered.ok()).toBeTruthy();
 await page.goto('/');await page.getByRole('button',{name:/内容与发布/}).click();const importer=page.getByRole('region',{name:'导入本地题包'});await expect(importer.getByLabel('选择本地题包文件')).toBeEnabled();await importer.getByLabel('选择本地题包文件').setInputFiles({name:'division.json',mimeType:'application/json',buffer});
 await expect(importer.getByText('3 个能力 · 20 道题 · 4 份资源 · 2 个课时',{exact:true})).toBeVisible();await importer.getByRole('button',{name:'保存为待审核草稿',exact:true}).click();await expect(page.getByRole('region',{name:'内容草稿编辑器'})).toBeVisible();
 const content=await (await context.request.get('/api/v1/content')).json(),draft=content.drafts.find((d:any)=>d.title===pack.title);expect(JSON.parse(draft.payload)).toEqual(pack.catalog);expect(draft.status).toBe('Draft');expect(content.releases).toHaveLength(0);expect(draft.reviewedBy).toBeNull();
 const preview=await context.request.get('/api/v1/content/drafts/'+draft.id+'/preview');expect(preview.ok()).toBeTruthy();expect((await preview.json()).errors).toEqual([]);
 const q=JSON.parse(draft.payload).questions;expect(q.filter((x:any)=>x.policy==='ObservedSteps')).toHaveLength(8);expect(q.filter((x:any)=>x.policy==='SingleKC')).toHaveLength(12);
});
