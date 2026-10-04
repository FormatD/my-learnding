import {test,expect} from '@playwright/test';
test('发布版本可明确撤回，新绑定和新会话拒绝，既有会话与历史保留',async({page,context,browser})=>{
 let etag='';async function call(path:string,body?:unknown,status?:number){const r=await context.request.fetch('/api/v1'+path,{method:body===undefined?'GET':'POST',headers:{'X-Learning-Request':'1','If-Match':etag,'Idempotency-Key':crypto.randomUUID()},...(body===undefined?{}:{data:body})});if(status)expect(r.status()).toBe(status);else expect(r.ok()).toBeTruthy();etag=r.headers()['etag']||etag;return r.json();}
 await call('/auth/register',{userName:'withdraw-ui-'+Date.now(),password:'withdraw-browser-private-2026'});const student=await call('/students',{name:'撤回页面验收'});
 const draft=await call('/content/fixture',{});await call(`/content/drafts/${draft.id}:review`,{});const preview=await call(`/content/drafts/${draft.id}/preview`);const release=await call(`/content/drafts/${draft.id}:publish`,{previewHash:preview.hash});await call(`/students/${student.id}/content/${release.id}:bind`,{});
 const day=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Shanghai'}).format(new Date());const plan=await call(`/students/${student.id}/plans/${day}:generate`,{});const question=JSON.parse(release.payload).questions[0];
 const tasks=[];for(let i=0;i<2;i++)tasks.push(await call(`/plans/${plan.id}/tasks`,{title:'撤回会话验收'+i,minutes:5,resourceRef:'原创验收内容',type:'Practice',questionId:question.id}));
 const view=await call(`/students/${student.id}/plans/${day}`);await call(`/plans/${plan.id}:publish`,{previewHash:view.revision.inputHash,confirmWarnings:true});
 for(const task of tasks)await call(`/tasks/${task.id}:transition`,{status:'InProgress'});
 const session=await call(`/tasks/${tasks[0].id}/sessions`,{});const attempt=(await call(`/sessions/${session.sessionId}/attempts`,{clientSubmissionId:crypto.randomUUID(),answer:'999'})).attempt;
 await expect.poll(async()=>(await call(`/students/${student.id}/mastery`)).pending).toBe(0);
 const memberName='withdraw-editor-'+Date.now();await call('/family/members',{userName:memberName,password:'withdraw-editor-private-2026',roles:['ContentEditor']});
 const before=await call(`/students/${student.id}/export`);
 await page.goto('/');await page.getByRole('button',{name:/内容与发布/}).click();
 const row=page.getByLabel(`内容版本 ${release.number}`,{exact:true});await row.getByRole('button',{name:'撤回此版本',exact:true}).click();
 await expect(row.getByRole('button',{name:'确认撤回此版本',exact:true})).toBeDisabled();
 await row.getByLabel('撤回原因',{exact:true}).fill('核对后发现需要停止新增练习，保留原始记录');
 await page.screenshot({path:'../../test-results/withdrawal-form-tablet.png',fullPage:true});await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();await row.screenshot({path:'../../test-results/withdrawal-form-phone.png'});await page.setViewportSize({width:1024,height:1366});
 await row.getByRole('button',{name:'取消撤回'}).click();await expect(row.getByLabel('撤回原因',{exact:true})).toHaveCount(0);
 expect((await call('/content')).releases.find((r:any)=>r.id===release.id).withdrawn).toBe(false);
 await row.getByRole('button',{name:'撤回此版本',exact:true}).click();await expect(row.getByRole('button',{name:'确认撤回此版本',exact:true})).toBeDisabled();
 await row.getByLabel('已核对此版本及影响，确认撤回').check();
 const endpoint='**/api/v1/content/releases/'+release.id+':withdraw';await page.route(endpoint,route=>route.fulfill({status:422,contentType:'application/json',body:JSON.stringify({title:'受控撤回未提交'})}));
 await row.getByRole('button',{name:'确认撤回此版本',exact:true}).click();await expect(row.getByRole('alert')).toHaveText('受控撤回未提交');await expect(row.getByLabel('撤回原因',{exact:true})).toHaveValue('核对后发现需要停止新增练习，保留原始记录');
 await page.unroute(endpoint);await row.getByRole('button',{name:'确认撤回此版本',exact:true}).click();await expect(row).toContainText('已撤回 · 不再接受新绑定或新会话');await expect(row).toContainText('当前学生仍绑定此版本');await expect(row.getByRole('button',{name:'当前学生已绑定'})).toBeDisabled();await expect(row.getByRole('button',{name:'撤回此版本',exact:true})).toHaveCount(0);
 const after=await call(`/students/${student.id}/export`);expect(after.attempts.find((a:any)=>a.id===attempt.id)).toEqual(attempt);expect(after.evidence).toEqual(before.evidence);expect(before.sessions).toHaveLength(1);expect(after.sessions).toEqual(before.sessions);expect(before.evidence.length).toBeGreaterThan(0);
 const saved=(await call('/content')).releases.find((r:any)=>r.id===release.id);expect(saved.withdrawn).toBe(true);expect(saved.payload).toBe(release.payload);expect(saved.hash).toBe(release.hash);
 const refused=await call(`/students/${student.id}/plans/${day}:generate`,{},422);expect(refused.code).toBe('WITHDRAWN');const unchanged=await call(`/students/${student.id}/export`);expect(unchanged.plans).toEqual(after.plans);expect(unchanged.revisions).toEqual(after.revisions);expect(unchanged.tasks).toEqual(after.tasks);
 await call(`/students/${student.id}/content/${release.id}:bind`,{},422);await call(`/tasks/${tasks[1].id}/sessions`,{},422);
 expect((await call(`/tasks/${tasks[0].id}/sessions`,{})).sessionId).toBe(session.sessionId);
 await call(`/sessions/${session.sessionId}/attempts`,{clientSubmissionId:crypto.randomUUID(),answer:question.answer});
 await page.reload();await page.getByRole('button',{name:/内容与发布/}).click();await expect(row).toContainText('已撤回 · 不再接受新绑定或新会话');await expect(row.getByRole('button',{name:'当前学生已绑定'})).toBeDisabled();
 const editor=await browser.newContext();
 try{
  const login=await editor.request.post('/api/v1/auth/login',{headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID()},data:{userName:memberName,password:'withdraw-editor-private-2026'}});expect(login.ok()).toBeTruthy();
  const editorPage=await editor.newPage();await editorPage.goto('/');await expect(editorPage.getByLabel(`内容版本 ${release.number}`,{exact:true})).toBeVisible();await expect(editorPage.getByRole('button',{name:'撤回此版本',exact:true})).toHaveCount(0);
  const denied=await editor.request.post('/api/v1/content/releases/'+release.id+':withdraw',{headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID()},data:{reason:'无发布权限'}});expect(denied.status()).toBe(403);
 }finally{await editor.close();}
 const child=await browser.newContext({storageState:await context.storageState()});
 try{
  const entered=await child.request.post('/api/v1/students/'+student.id+'/child-sessions',{headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID()},data:{}});expect(entered.ok()).toBeTruthy();
  const denied=await child.request.post('/api/v1/content/releases/'+release.id+':withdraw',{headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID()},data:{reason:'孩子不可撤回'}});expect(denied.status()).toBe(403);
 }finally{await child.close();}
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
});
