import {test,expect} from '@playwright/test';

for(const provider of ['Mock','Manual'])test(provider+' 映射批量审核保留校正、拒绝过期覆盖，只生成待审核草稿',async({page,context})=>{
  let etag='';
  async function call(path:string,body?:unknown,method=body===undefined?'GET':'POST'){
    const response=await context.request.fetch('/api/v1'+path,{method,headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID(),'If-Match':etag},...(body===undefined?{}:{data:body})});
    expect(response.ok()).toBeTruthy();etag=response.headers()['etag']||etag;return response.json();
  }
  await call('/auth/register',{userName:'mapping-ui-'+Date.now(),password:'mapping-private-browser-2026'});
  const student=await call('/students',{name:'映射工作台界面验收'});
  let source=await call('/content/fixture',{});const catalog=JSON.parse(source.payload);
  const unsafe='<script>window.__mappingUnsafe=1</script>';
  catalog.questions[0].stem+=' '+unsafe;catalog.resources[0].paperReference+=' '+unsafe;
  source=await call('/content/drafts/'+source.id,{title:source.title,catalog},'PUT');
  await call('/content/drafts/'+source.id+':review',{});
  const preview=await call('/content/drafts/'+source.id+'/preview');
  const original=await call('/content/drafts/'+source.id+':publish',{previewHash:preview.hash});
  await call('/students/'+student.id+'/content/'+original.id+':bind',{});
  await page.goto('/');await page.getByRole('button',{name:/内容与发布/}).click();
  const work=page.getByRole('region',{name:'映射建议与批量审核'});
  await work.getByText('准备一批建议',{exact:true}).click();
  await work.getByLabel('准备方式').selectOption(provider);
  await work.getByLabel('映射来源草稿').selectOption(source.id);
  await work.getByLabel('对照的正式能力库').selectOption(original.id);
  await work.getByRole('checkbox',{name:/^题目 · 第 1 题 ·/}).check();
  await work.getByLabel('对象类型').selectOption('Resource');
  await work.getByRole('checkbox',{name:'资源 · '+catalog.resources[0].title,exact:true}).check();
  await work.getByLabel('对象类型').selectOption('Lesson');
  await work.getByRole('checkbox',{name:'课时 · '+catalog.lessons[0].title,exact:true}).check();
  await work.getByRole('button',{name:provider==='Manual'?'准备手工映射维护':'准备本地模拟映射建议'}).click();
  await expect(work.getByText(provider==='Manual'?'手工维护已准备，只带入原关联并固定输入；覆盖权重默认值须逐项核对。':'模拟建议已准备，原草稿和能力库版本已固定；请逐项核对。',{exact:true})).toBeVisible();
  const run=(await call('/builder/mapping-runs'))[0];const path='/builder/mapping-runs/'+run.id;expect(run.provider).toBe(provider);if(provider==='Manual')expect(run.model).toBe('None');
  await expect(work.getByText('共 3 项 · 待处理 3 项 · 接受 0 项 · 拒绝 0 项 · 接受时校正 0 项',{exact:true})).toBeVisible();
  const resource=work.getByRole('article',{name:'资源映射审核 · '+catalog.resources[0].title,exact:true});
  const reason='逐项核对原题与教学覆盖 '+unsafe;
  async function selectAndExplain(){
    const choices=work.getByRole('checkbox',{name:/^本批处理 ·/});
    for(let i=0;i<await choices.count();i++)await choices.nth(i).check();
    await work.getByLabel('选中项的共同审核依据').fill(reason);
    await work.getByRole('button',{name:'将方式与理由应用到选中项'}).click();
  }
  await selectAndExplain();await resource.getByLabel('教学覆盖权重').fill('0');
  const acknowledgement=work.getByRole('checkbox',{name:'已核对选中对象、能力范围和独立测量依据，确认本批决定'});
  await acknowledgement.check();await work.getByRole('button',{name:'提交本批人工决定'}).click();
  await expect(work.getByRole('alert')).toContainText('课时/资源至少关联一个覆盖能力');
  expect((await call(path)).decisions).toHaveLength(0);
  await expect(resource.getByLabel('本项审核依据')).toHaveValue(reason);
  await resource.getByLabel('教学覆盖权重').fill('0.8');await expect(work.getByRole('button',{name:'提交本批人工决定'})).toBeDisabled();
  // A second editor advances the family version while this page retains its original read version.
  await call('/content/drafts',{title:'另一页面保存的独立草稿',catalog});
  await acknowledgement.check();await work.getByRole('button',{name:'提交本批人工决定'}).click();
  await expect(work.getByRole('alert')).toContainText('数据已更新，请刷新后重新确认');
  await expect(resource.getByLabel('教学覆盖权重')).toHaveValue('0.8');await expect(resource.getByLabel('本项审核依据')).toHaveValue(reason);
  expect((await call(path)).decisions).toHaveLength(0);
  await work.getByRole('button',{name:'刷新映射审核'}).click();
  await expect(resource.getByLabel('本项审核依据')).toHaveValue('');
  await selectAndExplain();await resource.getByLabel('教学覆盖权重').fill('0.8');await acknowledgement.check();
  await work.getByRole('button',{name:'提交本批人工决定'}).click();
  await expect(work.getByText('本批决定已保存，接受项生成待审核草稿；尚未发布或改变学生学习。',{exact:true})).toBeVisible();
  const reviewed=await call(path);expect(reviewed.decisions).toHaveLength(3);expect(reviewed.sets).toHaveLength(3);expect(reviewed.quality.evaluationStatus).toBe('NotEvaluated');
  const draftId=reviewed.decisions[0].createdDraftId;const content=await call('/content');const generated=content.drafts.find((d:any)=>d.id===draftId);
  expect(generated.status).toBe('Draft');expect(generated.reviewedBy).toBeNull();expect(content.releases).toHaveLength(1);expect(content.releases[0].payload).toBe(original.payload);
  expect((await call('/students')).find((s:any)=>s.id===student.id).activeReleaseId).toBe(original.id);
  await work.getByText('查看已保存的决定与草稿',{exact:true}).click();await expect(work.getByText(reason,{exact:false}).first()).toBeVisible();
  expect(await page.evaluate(()=>(window as any).__mappingUnsafe)).toBeUndefined();
  await page.screenshot({path:'../../test-results/mapping-workbench-'+provider.toLowerCase()+'-tablet.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  await page.screenshot({path:'../../test-results/mapping-workbench-'+provider.toLowerCase()+'-phone.png',fullPage:true});
  await work.getByRole('button',{name:'继续编辑审核映射草稿'}).first().click();
  const editor=page.getByRole('region',{name:'内容草稿编辑器'});await expect(editor).toBeVisible();await editor.getByRole('button',{name:'取消',exact:true}).click();
  const row=page.locator('.content-row').filter({has:page.getByRole('heading',{name:generated.title,exact:true})});
  await row.getByRole('button',{name:'审核答案与映射后发布'}).click();
  await expect(page.getByText('内容已发布，可以绑定学生。',{exact:true})).toBeVisible();
  const releases=(await call('/content')).releases;expect(releases).toHaveLength(2);expect(releases.find((r:any)=>r.id===original.id).payload).toBe(original.payload);
  expect((await call('/students')).find((s:any)=>s.id===student.id).activeReleaseId).toBe(original.id);
});
