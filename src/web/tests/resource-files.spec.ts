import {test,expect} from '@playwright/test';
test('私有学习文件上传保存，固定计划选择及孩子任务下载',async({page,context})=>{
 let etag='';async function call(path:string,body?:unknown,status=200){const r=await context.request.fetch('/api/v1'+path,{method:body===undefined?'GET':'POST',headers:{'X-Learning-Request':'1','If-Match':etag,'Idempotency-Key':crypto.randomUUID()},...(body===undefined?{}:{data:body})});expect(r.status()).toBe(status);etag=r.headers()['etag']||etag;return r.json();}
 await call('/auth/register',{userName:'resource-file-ui-'+Date.now(),password:'resource-file-browser-2026'},201);
 const student=await call('/students',{name:'材料上传页面验收'},201),draft=await call('/content/fixture',{});
 await page.goto('/');await page.getByRole('button',{name:/内容与发布/}).click();await page.locator('.content-row').filter({hasText:draft.title}).getByRole('button',{name:'编辑',exact:true}).click();
 const editor=page.getByLabel('内容草稿编辑器'),resource=editor.getByLabel('资源 1',{exact:true});
 await resource.getByLabel('资源名称',{exact:true}).fill('私有学习图片验收');await resource.getByLabel('纸笔材料与执行说明',{exact:true}).fill('阅读原创验收图片');
 const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXioAAAAASUVORK5CYII=','base64');
 let resume!:()=>void;const gate=new Promise<void>(resolve=>{resume=resolve});let sent!:()=>void;const started=new Promise<void>(resolve=>{sent=resolve});
 await page.route('**/api/v1/content/resource-files',async route=>{sent();await gate;await route.continue();});
 await resource.getByLabel('上传私有学习文件（PDF、图片、WAV或MP3）',{exact:true}).setInputFiles({name:'原创验收.png',mimeType:'image/png',buffer:png});await started;
 await expect(editor.getByRole('button',{name:'保存并退回待审核'})).toBeDisabled();await expect(editor.getByRole('button',{name:'取消',exact:true})).toBeDisabled();resume();
 await expect(resource).toContainText('已关联私有材料');const material=resource.getByRole('link',{name:'核对已关联文件 ↗'});const materialResponse=await context.request.get(await material.getAttribute('href')||'');expect(materialResponse.status()).toBe(200);expect(await materialResponse.body()).toEqual(png);await page.unroute('**/api/v1/content/resource-files');
 const uploadSaved=await call('/content');const before=uploadSaved.drafts.find((d:any)=>d.id===draft.id);expect(JSON.parse(before.payload).resources[0].fileId).toBeUndefined();
 // Own upload advances only the editor's captured version; a subsequent save is real.
 await editor.getByRole('button',{name:'保存并退回待审核'}).click();await expect(editor).toHaveCount(0);
 const saved=(await call('/content')).drafts.find((d:any)=>d.id===draft.id),r=JSON.parse(saved.payload).resources[0];expect(r.fileId).toBeTruthy();expect(r.fileSnapshotHash).toMatch(/^[a-f0-9]{64}$/);
 await call(`/content/drafts/${draft.id}:review`,{});const preview=await call(`/content/drafts/${draft.id}/preview`);const release=await call(`/content/drafts/${draft.id}:publish`,{previewHash:preview.hash});await call(`/students/${student.id}/content/${release.id}:bind`,{});
 const day=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Shanghai'}).format(new Date());const plan=await call(`/students/${student.id}/plans/${day}:generate`,{});
 await page.getByRole('button',{name:/进度与计划/}).click();await page.getByRole('combobox',{name:'任务类型',exact:true}).selectOption('Resource');await page.getByRole('combobox',{name:'计划版本讲解材料',exact:true}).selectOption(r.id);await page.getByLabel('追加必做任务',{exact:true}).fill('文件阅读任务验收');await expect(page.getByLabel('执行说明',{exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'加入草稿',exact:true}).click();await expect(page.locator('.plan-panel')).toContainText('文件阅读任务验收');
 const state=await call(`/students/${student.id}/plans/${day}`),task=state.tasks.find((t:any)=>t.title==='文件阅读任务验收');expect(task.resourceId).toBe(r.id);expect(task.resourceRevisionId).toBe(r.revisionId);
 await page.getByRole('button',{name:'确认并发布',exact:true}).click();await expect(page.locator('.plan-panel')).toContainText('已发布');
 await call(`/students/${student.id}/child-sessions`,{});await page.goto('/');await page.locator('.task-row').filter({hasText:'文件阅读任务验收'}).getByRole('button',{name:'开始',exact:true}).click();
 const link=page.getByRole('link',{name:/打开/});await expect(link).toHaveAttribute('href',`/api/v1/tasks/${task.id}/resource-file`);
 const download=await context.request.get(await link.getAttribute('href')||'');expect(download.status()).toBe(200);expect(await download.body()).toEqual(png);expect((await context.request.get(`/api/v1/files/${r.fileId}`)).status()).toBe(404);
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
