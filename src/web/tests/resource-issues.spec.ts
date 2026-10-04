import {test,expect} from '@playwright/test';
test('孩子报告资源问题，家长可见，原任务与学习证据不变',async({page,context,browser})=>{
 let etag='';async function call(path:string,body?:unknown,status?:number,key=crypto.randomUUID()){const r=await context.request.fetch('/api/v1'+path,{method:body===undefined?'GET':'POST',headers:{'X-Learning-Request':'1','If-Match':etag,'Idempotency-Key':key},...(body===undefined?{}:{data:body})});if(status)expect(r.status()).toBe(status);else expect(r.ok()).toBeTruthy();etag=r.headers()['etag']||etag;return r.json();}
 await call('/auth/register',{userName:'resource-ui-'+Date.now(),password:'resource-browser-private-2026'});const student=await call('/students',{name:'资源报告验收'}),other=await call('/students',{name:'另一个学生'});
 const draft=await call('/content/fixture',{});await call(`/content/drafts/${draft.id}:review`,{});const preview=await call(`/content/drafts/${draft.id}/preview`);const release=await call(`/content/drafts/${draft.id}:publish`,{previewHash:preview.hash});await call(`/students/${student.id}/content/${release.id}:bind`,{});
 const day=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Shanghai'}).format(new Date());const plan=await call(`/students/${student.id}/plans/${day}:generate`,{});
 const task=await call(`/plans/${plan.id}/tasks`,{title:'资源打开问题测试',minutes:5,resourceRef:'原创学习纸质资源',type:'Resource'});
 await call(`/tasks/${task.id}/resource-issues`,{reason:'未发布不得提交'},409);
 const questionTask=await call(`/plans/${plan.id}/tasks`,{title:'题目不可报告为资源',minutes:5,resourceRef:'受控题目',type:'Practice',questionId:JSON.parse(release.payload).questions[0].id});
 const state=await call(`/students/${student.id}/plans/${day}`);await call(`/plans/${plan.id}:publish`,{previewHash:state.revision.inputHash,confirmWarnings:true});
 await call(`/tasks/${questionTask.id}/resource-issues`,{reason:'题目任务拒绝资源报告'},422);
 await call(`/tasks/${task.id}/resource-issues`,{reason:'   '},422);await call(`/tasks/${task.id}/resource-issues`,{reason:'a'.repeat(501)},422);
 const parent=await browser.newContext({storageState:await context.storageState()});
 try{
 await call(`/students/${student.id}/child-sessions`,{});await page.goto('/');await page.locator('.task-row').filter({hasText:'资源打开问题测试'}).getByRole('button',{name:'开始',exact:true}).click();
 await expect.poll(async()=>(await call(`/students/${student.id}/today`)).tasks.find((t:any)=>t.id===task.id).status).toBe('InProgress');
 const beforeTask=(await call(`/students/${student.id}/today`)).tasks.find((t:any)=>t.id===task.id);
 const panel=page.getByLabel('报告资源问题',{exact:true});await panel.getByRole('button',{name:'资源打不开或不合适'}).click();await expect(panel.getByRole('button',{name:'告诉家长'})).toBeDisabled();await panel.getByLabel('资源遇到什么问题？').fill('纸质材料缺页，无法继续学习');await panel.getByRole('button',{name:'取消',exact:true}).click();
 const before=await parent.request.get('/api/v1/students/'+student.id+'/resource-issues');expect(await before.json()).toEqual([]);
 await panel.getByRole('button',{name:'资源打不开或不合适'}).click();
 const reportPath='**/api/v1/tasks/'+task.id+'/resource-issues';await page.route(reportPath,async route=>{const response=await route.fetch();expect(response.status()).toBe(201);await route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({title:'报告响应中断，请重试'})});});
 await panel.getByRole('button',{name:'告诉家长'}).click();await expect(panel.getByRole('alert')).toHaveText('报告响应中断，请重试');await expect(panel.getByLabel('资源遇到什么问题？')).toHaveValue('纸质材料缺页，无法继续学习');
 const persisted=await parent.request.get('/api/v1/students/'+student.id+'/resource-issues');expect(await persisted.json()).toHaveLength(1);
 await page.unroute(reportPath);await panel.getByRole('button',{name:'告诉家长'}).click();await expect(panel).toContainText('已告诉家长');
 const afterTask=(await call(`/students/${student.id}/today`)).tasks.find((t:any)=>t.id===task.id);expect(afterTask).toEqual(beforeTask);
 await call(`/students/${student.id}/resource-issues`,undefined,403);await call(`/students/${other.id}/resource-issues`,undefined,403);
 const unknown=await call(`/tasks/${crypto.randomUUID()}/resource-issues`,{reason:'不能报告未知任务'},404);expect(unknown.code).toBe('NOT_FOUND');
 const parentPage=await parent.newPage();await parentPage.goto('/');const inbox=parentPage.getByRole('region',{name:'孩子报告的资源问题'});await expect(inbox).toContainText('纸质材料缺页，无法继续学习');await expect(inbox).toContainText('资源打开问题测试');
 const exported=await parent.request.get('/api/v1/students/'+student.id+'/export');const data=await exported.json();expect(data.attempts).toEqual([]);expect(data.evidence).toEqual([]);const audit=data.studentAudit.filter((r:any)=>r.action==='ResourceIssueReported');expect(audit).toHaveLength(1);expect(JSON.parse(audit[0].details).taskId).toBe(task.id);
 const retryKey=crypto.randomUUID();const first=await call(`/tasks/${task.id}/resource-issues`,{reason:'幂等接口验收'},201,retryKey);const again=await call(`/tasks/${task.id}/resource-issues`,{reason:'幂等接口验收'},201,retryKey);expect(again).toEqual(first);await call(`/tasks/${task.id}/resource-issues`,{reason:'修改原请求'},409,retryKey);
 const finalExport=await parent.request.get('/api/v1/students/'+student.id+'/export');expect((await finalExport.json()).studentAudit.filter((r:any)=>r.action==='ResourceIssueReported')).toHaveLength(2);
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
 }finally{await parent.close();}
 const foreign=await browser.newContext();try{const register=await foreign.request.post('/api/v1/auth/register',{headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID()},data:{userName:'resource-other-'+Date.now(),password:'resource-other-private-2026'}});expect(register.status()).toBe(201);const denied=await foreign.request.post('/api/v1/tasks/'+task.id+'/resource-issues',{headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID()},data:{reason:'其他家庭不可报告'}});expect(denied.status()).toBe(404);expect((await foreign.request.get('/api/v1/students/'+student.id+'/resource-issues')).status()).toBe(404);}finally{await foreign.close();}
});
