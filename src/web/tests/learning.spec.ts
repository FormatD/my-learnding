import { test, expect } from '@playwright/test';
import { localDate } from '../src/api';
// Keep the real authentication limiter enabled; pace this suite's setup logins.
let authTimes:number[]=[];
test.beforeEach(async({page},info)=>{
  authTimes=authTimes.filter(t=>Date.now()-t<61000);
  while(authTimes.length>=8){const wait=Math.min(60000,Math.max(1,61000-(Date.now()-authTimes[0])));info.setTimeout(info.timeout+wait);await new Promise(resolve=>setTimeout(resolve,wait));authTimes=authTimes.filter(t=>Date.now()-t<61000);}
  page.on('request',request=>{if(request.method()==='POST'&&/\/api\/v1\/auth\/(login|register)$/.test(request.url()))authTimes.push(Date.now());});
});
test('家长发布 → 平板作答 → 证据复习', async ({page})=>{
  const name='browser-'+Date.now();
  await page.goto('/');
  await page.getByRole('button',{name:'首次使用？创建家庭'}).click();
  await page.getByLabel('家长用户名').fill(name);
  await page.getByLabel('家长密码').fill('test-browser-private-2026');
  await page.getByRole('button',{name:'创建私有家庭'}).click();
  await page.getByLabel('孩子昵称').fill('小步同学');
  await page.getByRole('button',{name:'创建学生',exact:true}).click();
  await expect(page.getByRole('heading',{name:'先审核，再交给孩子'})).toBeVisible();
  await page.getByRole('button',{name:'创建混合运算样例'}).click();
  await page.getByRole('button',{name:'审核答案与映射后发布'}).click();
  await page.getByRole('button',{name:'绑定当前学生'}).click();
  await page.getByRole('button',{name:/进度与计划/}).click();
  await page.getByRole('combobox', {name:'课时',exact:true}).selectOption({label:'小熊购物 · 乘加、乘减'});
  await page.getByRole('button',{name:'确认今天学到这里'}).click();
  await page.getByRole('button',{name:'生成草稿',exact:true}).click();
  await page.getByRole('button',{name:'确认并发布'}).click();
  await page.getByRole('button',{name:/今日学习/}).click();
  await expect(page.getByRole('button',{name:'进入孩子模式 ↗'})).toBeEnabled();
  await page.screenshot({path:'../../test-results/today-tablet.png',fullPage:true});
  await page.evaluate(()=>(window as any).__parentDraftSentinel='parent-workspace');
  await page.getByRole('button',{name:'进入孩子模式 ↗'}).click();
  await expect(page.getByRole('button',{name:/内容与发布/})).toHaveCount(0);
  expect(await page.evaluate(()=>(window as any).__parentDraftSentinel)).toBeUndefined();
  await page.getByRole('button',{name:/开始学习/}).click();
  await page.getByLabel('你的答案').fill('999');
  await page.getByRole('button',{name:'提交答案',exact:true}).click();
  await expect(page.getByText('已保存。先看看思路，再试一次。')).toBeVisible();
  await page.screenshot({path:'../../test-results/answer-tablet.png',fullPage:true});
  await page.getByRole('button',{name:'完成这个任务 ✓'}).click();
  await expect(page.getByText('已完成',{exact:true}).first()).toBeVisible();
  await page.getByRole('button',{name:'返回家长登录'}).click();
  await page.getByLabel('家长用户名').fill(name);
  await page.getByLabel('家长密码').fill('test-browser-private-2026');
  await page.getByRole('button',{name:'登录',exact:true}).click();
  await page.getByRole('button',{name:/证据与复习/}).click();
  await expect(page.getByText('错题复习 · R1')).toBeVisible();
  await page.getByRole('button',{name:'查看依据'}).first().click();
  await expect(page.getByRole('heading',{name:'证据明细'})).toBeVisible();
  await page.getByRole('button',{name:/家庭设置/}).click();
  await page.getByLabel('昵称',{exact:true}).fill('另一位同学');
  await page.getByRole('button',{name:'创建',exact:true}).click();
  await expect(page.getByRole('combobox',{name:'切换学生'})).toHaveValue(/.+/);
  await expect(page.getByRole('combobox',{name:'切换学生'}).locator('option:checked')).toHaveText('另一位同学');
  await page.getByRole('button',{name:'绑定当前学生'}).click();
  await page.getByRole('button',{name:/进度与计划/}).click();
  await expect(page.getByRole('combobox',{name:'课时',exact:true})).toBeEnabled();
  await expect(page.getByRole('combobox',{name:'课时',exact:true})).toHaveValue('');
  await page.getByRole('combobox',{name:'切换学生'}).selectOption({label:'小步同学'});
  await expect(page.getByRole('button',{name:'确认今天学到这里'})).toBeEnabled();
  await expect(page.getByRole('combobox',{name:'课时',exact:true}).locator('option:checked')).toHaveText('小熊购物 · 乘加、乘减');
  await page.getByLabel('要纠正的记录').selectOption({index:1});
  await page.getByRole('combobox',{name:'课时',exact:true}).selectOption({label:'买文具 · 除加、除减'});
  await page.getByLabel('更正说明').fill('核对作业后确认实际已学到买文具');
  await page.getByRole('button',{name:'按上方所选课时更正'}).click();
  await expect(page.getByRole('combobox',{name:'课时',exact:true}).locator('option:checked')).toHaveText('买文具 · 除加、除减');
  await page.getByText(/进度更正记录 · 1/).click();
  await expect(page.getByText('核对作业后确认实际已学到买文具',{exact:true})).toBeVisible();
});

test('AT37 题面中的脚本和标签以普通文字显示', async ({page,context})=>{
  let etag='';
  async function call(path:string,data?:unknown,method=data===undefined?'GET':'POST'){
    const response=await context.request.fetch('/api/v1'+path,{method,headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID(),'If-Match':etag},...(data===undefined?{}:{data})});
    expect(response.ok()).toBeTruthy();etag=response.headers()['etag']||etag;return response.json();
  }
  authTimes.push(Date.now());await call('/auth/register',{userName:'escaping-'+Date.now(),password:'browser-private-test-2026'});
  const student=await call('/students',{name:'安全题面验收'});
  const draft=await call('/content/fixture',{}),catalog=JSON.parse(draft.payload);
  const payload='<script>window.__unsafe=1</script><img src="/__attack" onerror="window.__unsafe=1">';
  catalog.questions.forEach((q:any)=>{q.stem=payload;});
  await call('/content/drafts/'+draft.id,{title:'题面转义验收',catalog},'PUT');
  await call('/content/drafts/'+draft.id+':review',{});
  const preview=await call('/content/drafts/'+draft.id+'/preview');
  const release=await call('/content/drafts/'+draft.id+':publish',{previewHash:preview.hash});
  await call('/students/'+student.id+'/content/'+release.id+':bind',{});
  const date=localDate(student.timeZone);
  await call('/students/'+student.id+'/school-progress/'+date+'/'+catalog.lessons[0].id,{},'PUT');
  const revision=await call('/students/'+student.id+'/plans/'+date+':generate',{});
  await call('/plans/'+revision.id+':publish',{previewHash:revision.inputHash});
  await call('/students/'+student.id+'/child-sessions',{});
  await page.goto('/');await page.getByRole('button',{name:/开始学习/}).click();
  await expect(page.getByText(payload,{exact:true})).toBeVisible();
  await expect(page.locator('img[src="/__attack"]')).toHaveCount(0);
  expect(await page.evaluate(()=>(window as any).__unsafe)).toBeUndefined();
  await page.getByLabel('你的答案').fill('999');
  await page.getByRole('button',{name:'提交答案',exact:true}).click();
  await expect(page.getByText('已保存。先看看思路，再试一次。')).toBeVisible();
});

test('空白草稿补齐能力、测量题和资源后发布进入孩子学习',async({page})=>{
  const name='authoring-'+Date.now(),password='private-authoring-2026';
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();
  await page.getByLabel('家长用户名').fill(name);await page.getByLabel('家长密码').fill(password);await page.getByRole('button',{name:'创建私有家庭'}).click();
  await page.getByLabel('孩子昵称').fill('内容编辑验收');await page.getByRole('button',{name:'创建学生',exact:true}).click();
  await page.getByRole('button',{name:'新建空白内容草稿'}).click();
  await page.getByRole('button',{name:'新增独立能力'}).click();
  const kc=page.locator('[aria-label="能力 1"]');
  await kc.getByLabel('能力名称').fill('确定乘加运算顺序');await expect(kc.getByLabel('能力类型').locator('option')).toHaveCount(7);await kc.getByLabel('能力类型').selectOption('Strategy');
  await kc.getByLabel('独立可测行为').fill('独立先算乘法再算加法');
  await kc.getByLabel('测量边界与排除范围').fill('不含括号，表内乘法，不推断阅读能力');
  await page.getByRole('button',{name:'保存并退回待审核'}).click();
  await page.getByRole('button',{name:'审核答案与映射后发布'}).click();
  await expect(page.getByText(/没有经过审核的测量题/)).toBeVisible();
  await page.getByRole('button',{name:'编辑',exact:true}).click();
  await page.getByRole('button',{name:'新增测量题'}).click();
  const question=page.locator('[aria-label="题目 1"]');
  await question.getByLabel('题干',{exact:true}).fill('3 + 4 × 2 = ?');
  await question.getByLabel('参考答案').fill('11');await question.getByLabel('讲解说明').fill('先算4×2=8，再算3+8=11。');
  await question.getByLabel('提示内容').fill('先看一看哪一步是乘法。');
  await page.getByRole('button',{name:'新增讲解资源'}).click();
  const resource=page.locator('[aria-label="资源 1"]');
  await resource.getByLabel('资源名称').fill('乘加顺序纸笔示范');
  await resource.getByLabel('纸笔材料与执行说明').fill('准备纸笔，家长示范2+3×4，孩子圈出先算的步骤。');
  await page.getByRole('button',{name:'新增课时'}).click();
  await page.locator('[aria-label="课时 1"]').getByLabel('课时名称').fill('自编混合运算小练习');
  await expect(page.getByRole('button',{name:'审核答案与映射后发布'})).toBeDisabled();
  await page.screenshot({path:'../../test-results/content-editor-tablet.png',fullPage:true});
  await page.getByRole('button',{name:'保存并退回待审核'}).click();
  await page.getByRole('button',{name:'审核答案与映射后发布'}).click();
  await expect(page.getByRole('heading',{name:'内容版本 1',exact:true})).toBeVisible();
  const classified=await page.evaluate(async()=>{const data=await(await fetch('/api/v1/content')).json();return JSON.parse(data.releases[0].payload).kcs[0];});expect(classified.type).toBe('Strategy');
  await page.getByRole('button',{name:'绑定当前学生'}).click();
  await page.getByRole('button',{name:/进度与计划/}).click();
  await page.getByRole('combobox',{name:'课时',exact:true}).selectOption({label:'自编混合运算小练习'});
  await page.getByRole('button',{name:'确认今天学到这里'}).click();await page.getByRole('button',{name:'生成草稿',exact:true}).click();await page.getByRole('button',{name:'确认并发布'}).click();
  await page.getByRole('button',{name:/今日学习/}).click();await page.getByRole('button',{name:'进入孩子模式 ↗'}).click();await page.getByRole('button',{name:/开始学习/}).click();
  await expect(page.getByText('3 + 4 × 2 = ?',{exact:true})).toBeVisible();await page.getByLabel('你的答案').fill('11');await page.getByRole('button',{name:'提交答案',exact:true}).click();
  await expect(page.getByText('这次做对了！',{exact:true})).toBeVisible();await page.getByRole('button',{name:'完成这个任务 ✓'}).click();
  await page.getByRole('button',{name:'返回家长登录'}).click();await page.getByLabel('家长用户名').fill(name);await page.getByLabel('家长密码').fill(password);await page.getByRole('button',{name:'登录',exact:true}).click();
  await page.getByRole('button',{name:/内容与发布/}).click();await page.getByRole('button',{name:'创建新修订',exact:true}).click();await page.getByRole('button',{name:'编辑',exact:true}).click();
  await expect(page.locator('[aria-label="能力 1"]').getByLabel('独立可测行为')).toHaveAttribute('readonly','');await expect(page.locator('[aria-label="能力 1"]').getByLabel('能力类型')).toBeDisabled();await expect(page.locator('[aria-label="能力 1"]').getByLabel('能力类型')).toHaveValue('Strategy');
});

test('建库候选保留引用，审核草稿可继续编辑并发布',async({page})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();
  await page.getByLabel('家长用户名').fill('builder-trace-'+Date.now());await page.getByLabel('家长密码').fill('private-builder-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();
  await page.getByLabel('孩子昵称').fill('建库追溯验收');await page.getByRole('button',{name:'创建学生',exact:true}).click();
  await page.getByRole('button',{name:/辅助建库/}).click();
  const budget=page.getByRole('region',{name:'建库预算'});
  await budget.getByLabel('每日费用上限（USD）').fill('0.2');await budget.getByLabel('单次费用上限（USD）').fill('0.1');
  await budget.getByLabel('每日 Token 上限').fill('100');await budget.getByLabel('单次 Token 上限').fill('50');await budget.getByLabel('同时处理上限').fill('2');
  await budget.getByLabel('调整依据').fill('本地浏览器预算保存验收');await budget.getByRole('button',{name:'保存调用预算'}).click();
  await expect(budget.getByRole('status')).toContainText('预算已保存');await expect(budget.getByLabel('每日费用上限（USD）')).toHaveValue('0.2');
  const quote='有小括号时，先计算小括号里的加减，再计算除法。';
  await page.getByLabel('来源标题').fill('家长原创运算说明');await page.getByLabel('文本内容').fill(quote);await page.getByRole('button',{name:'保存来源'}).click();await page.getByRole('button',{name:'验证候选流程'}).click();
  await expect(async()=>{await page.getByRole('button',{name:'刷新',exact:true}).click();await expect(page.getByRole('button',{name:'核对此候选'})).toBeVisible();}).toPass();
  await expect(page.getByText('Mock / fixture/1 · 已完成 · 生成时没有正式内容库',{exact:true})).toBeVisible();
  const ledger=page.getByRole('region',{name:'建库调用账本'});await ledger.getByRole('button',{name:'刷新调用账本'}).click();await expect(ledger.getByText('费用：本地模拟，不计费；输入/输出 Token：不适用')).toBeVisible();
  const jobs=page.getByRole('region',{name:'后台任务保护'});await jobs.getByRole('button',{name:'刷新后台任务'}).click();await expect(jobs.getByRole('heading',{name:'候选建库 · 已完成'})).toBeVisible();await jobs.getByText('查看领取记录',{exact:true}).click();await expect(jobs.getByText(/第 0 轮 · 第 1 次 · 已完成/)).toBeVisible();
  const records=page.getByRole('region',{name:'建库运行记录'});await expect(records.getByText(/创建时处理上限：100 个片段、100000 字；30 秒内完成；最多修复 1 次/)).toBeVisible();await records.getByText('查看每次尝试',{exact:true}).click();await expect(records.getByText(/结构与来源校验通过 · 本地模拟处理 1 次 · 首次通过/)).toBeVisible();
  await page.getByRole('button',{name:'核对此候选'}).click();await page.getByLabel('本次审核依据').fill('已核对原始片段与一层括号测量范围，接受为待审草稿。');await page.getByLabel('候选名称',{exact:true}).fill('先算小括号');await page.getByLabel('可测行为',{exact:true}).fill('独立计算一层小括号内的加减，再计算除法');await page.getByLabel('能力边界').fill('不含嵌套，不推断建模能力');
  await page.getByRole('button',{name:'接受为新草稿'}).click();await page.getByRole('button',{name:'查看引用与审核'}).click();
  const trace=page.getByRole('region',{name:'内容来源追溯'});
  // The named section may have an implicit region only when its accessible name is present.
  await expect(trace.getByText(quote,{exact:true})).toBeVisible();await expect(trace.getByText('家长原创运算说明',{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'关闭记录'}).click();await page.getByRole('button',{name:'继续编辑审核草稿'}).click();await expect(page.getByRole('heading',{name:'编辑内容草稿'})).toBeVisible();
  const recordedMetadata=await page.evaluate(async()=>{const data=await(await fetch('/api/v1/builder')).json();return JSON.parse(data.candidates[0].protocolPayload);});const metadata=page.locator('[aria-label="能力 1"]');await expect(metadata.getByLabel('能力学科')).toHaveValue(recordedMetadata.subject);await expect(metadata.getByLabel('适用起始年级')).toHaveValue(String(recordedMetadata.gradeMin));await expect(metadata.getByLabel('适用结束年级')).toHaveValue(String(recordedMetadata.gradeMax));await metadata.getByLabel('适用起始年级').fill('2');await metadata.getByLabel('适用结束年级').fill('5');await metadata.getByLabel('能力领域').fill('数与运算');await metadata.getByLabel('能力复杂度').selectOption('Hard');await metadata.getByLabel('能力认知层级').fill('理解与应用');
  await page.getByRole('button',{name:'新增测量题'}).click();const q=page.locator('[aria-label="题目 1"]');await q.getByLabel('题干',{exact:true}).fill('(8 + 4) ÷ 3 = ?');await q.getByLabel('参考答案').fill('4');await q.getByLabel('讲解说明').fill('先算8+4=12，再算12÷3=4。');
  await page.getByRole('button',{name:'新增课时'}).click();await page.locator('[aria-label="课时 1"]').getByLabel('课时名称').fill('原创小括号练习');await page.getByRole('button',{name:'保存并退回待审核'}).click();await page.getByRole('button',{name:'审核答案与映射后发布'}).click();
  await expect(page.getByRole('heading',{name:'内容版本 1',exact:true})).toBeVisible();
  const publishedMetadata=await page.evaluate(async()=>{const data=await(await fetch('/api/v1/content')).json();return JSON.parse(data.releases[0].payload).kcs[0];});expect(publishedMetadata).toMatchObject({subject:'MATH',gradeMin:2,gradeMax:5,domain:'数与运算',difficultyLevel:'Hard',cognitiveLevel:'理解与应用'});
  await page.getByText('能力定义与来源',{exact:true}).click();await expect(page.getByText('能力领域：数与运算',{exact:true})).toBeVisible();await expect(page.getByText('能力认知层级：理解与应用',{exact:true})).toBeVisible();await expect(page.getByText('能力复杂度：较复杂；与题目难度分开记录。',{exact:true})).toBeVisible();await page.getByRole('button',{name:'查看能力来源'}).click();await expect(trace.getByText(quote,{exact:true})).toBeVisible();
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
});

test('家长安排步骤题并保留未观察步骤',async({page})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();
  await page.getByLabel('家长用户名').fill('browser-steps-'+Date.now());await page.getByLabel('家长密码').fill('steps-browser-private-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();
  await page.getByLabel('孩子昵称').fill('步骤同学');await page.getByRole('button',{name:'创建学生',exact:true}).click();
  await page.getByRole('button',{name:'创建混合运算样例'}).click();await expect(page.getByRole('button',{name:'审核答案与映射后发布'})).toBeVisible();
  await page.evaluate(async()=>{
    const response=await fetch('/api/v1/content');const content=await response.json();const draft=content.drafts[0];const catalog=JSON.parse(draft.payload);const q=catalog.questions[16];q.policy='ObservedSteps';q.type='MultiStep';q.mappings=[{kcId:catalog.kcs[8].id,role:'Primary',share:.5,mode:'StepObserved',step:'列式'},{kcId:catalog.kcs[0].id,role:'Secondary',share:.5,mode:'StepObserved',step:'运算顺序'}];
    const saved=await fetch('/api/v1/content/drafts/'+draft.id,{method:'PUT',headers:{'Content-Type':'application/json','X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID(),'If-Match':response.headers.get('etag')!},body:JSON.stringify({title:draft.title,catalog})});if(!saved.ok)throw new Error(await saved.text());
  });
  await page.getByRole('button',{name:/进度与计划/}).click();await page.getByRole('button',{name:/内容与发布/}).click();await page.getByRole('button',{name:'审核答案与映射后发布'}).click();await page.getByRole('button',{name:'绑定当前学生'}).click();
  await page.getByRole('button',{name:/进度与计划/}).click();await page.getByRole('button',{name:'生成草稿',exact:true}).click();
  await page.getByLabel('任务类型').selectOption('Practice');
  const choice=await page.getByLabel('计划版本题目').locator('option').filter({hasText:'家长观察步骤'}).first().getAttribute('value');
  await page.getByLabel('计划版本题目').selectOption(choice!);await page.getByLabel('追加必做任务').fill('观察列式过程');await page.getByLabel('分钟',{exact:true}).fill('5');await page.getByLabel('执行说明').fill('先在纸上列式，再写计算过程');await page.getByRole('button',{name:'加入草稿'}).click();
  await page.getByRole('button',{name:'确认并发布'}).click();await page.getByRole('button',{name:/今日学习/}).click();
  await page.getByRole('button',{name:/开始学习/}).click();await page.getByLabel('你的答案').fill('只完成了列式');await page.getByRole('button',{name:'提交答案',exact:true}).click();await expect(page.getByRole('button',{name:'等待家长判分',exact:true})).toBeDisabled();await page.getByRole('button',{name:'已提交，返回今日任务'}).click();await page.getByRole('button',{name:/继续学习/}).click();await expect(page.getByLabel('你的答案')).toHaveValue('只完成了列式');await expect(page.getByRole('button',{name:'等待家长判分',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:/证据与复习/}).click();await page.getByRole('button',{name:'查看题目并判分'}).click();
  await expect(page.getByRole('heading',{name:'核对题目与观察步骤'})).toBeVisible();await page.getByLabel('整题判分').selectOption('Partial');
  await page.getByLabel(/^列式 ·/).selectOption('Correct');await expect(page.getByLabel(/^运算顺序 ·/)).toHaveValue('Unknown');
  await page.getByLabel('判分依据').fill('纸上列式正确，计算没有观察到');await page.getByRole('button',{name:'预览判分影响'}).click();await expect(page.getByText('待确认 → 部分正确')).toBeVisible();await page.getByRole('button',{name:'确认更正并重算'}).click();
  await expect(page.getByRole('heading',{name:'核对题目与观察步骤'})).toHaveCount(0);await expect(page.getByText('第 1 次 · 部分正确')).toBeVisible();
  await page.getByRole('button',{name:/今日学习/}).click();await page.getByRole('button',{name:/继续学习/}).click();await expect(page.getByLabel('你的答案')).toHaveValue('只完成了列式');await expect(page.getByRole('button',{name:'完成这个任务 ✓'})).toBeEnabled();await page.getByRole('button',{name:'完成这个任务 ✓'}).click();await expect(page.getByText('今天的任务完成了')).toBeVisible();
});

test('纸质原题审核后代录，原始结果保留可更正',async({page})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();await page.getByLabel('家长用户名').fill('browser-paper-'+Date.now());await page.getByLabel('家长密码').fill('private-paper-browser-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();
  await page.getByLabel('孩子昵称').fill('纸质同学');await page.getByRole('button',{name:'创建学生',exact:true}).click();await page.getByRole('button',{name:'创建混合运算样例'}).click();await page.getByRole('button',{name:'审核答案与映射后发布'}).click();await page.getByRole('button',{name:'绑定当前学生'}).click();await page.getByRole('button',{name:/证据与复习/}).click();
  await page.getByLabel('题干（可稍后补充）').fill('3 + 4 × 2 = ?');await page.getByLabel('孩子答案').fill('999');await page.getByRole('button',{name:'保存为待校正错题'}).click();await page.getByRole('button',{name:'补齐并审核归因'}).click();
  await expect(page.getByRole('heading',{name:'审核纸质结果'})).toBeVisible();await expect(page.getByLabel('纸质原始答案')).toHaveValue('999');await page.getByLabel('对应的正式原题').selectOption({label:'3 + 4 × 2 = ?'});await page.getByLabel('已核对这是同一道原题，题干与条件一致').check();await page.getByLabel('纸质判分与归因依据').fill('家长核对原题，先加后乘造成错误');await page.getByRole('button',{name:'预览纸质结果归因'}).click();await expect(page.getByText(/确认后以本次确认时间代录首次作答/)).toBeVisible();await page.getByRole('button',{name:'确认并代录纸质结果'}).click();
  await expect(page.getByRole('heading',{name:'审核纸质结果'})).toHaveCount(0);await expect(page.getByText('原题与结果已确认 · 历史记录保留')).toBeVisible();await page.getByRole('button',{name:'查看代录判分'}).click();await expect(page.getByRole('heading',{name:'核对题目与观察步骤'})).toBeVisible();await expect(page.getByText('孩子答案：999')).toBeVisible();await expect(page.getByLabel('整题判分')).toHaveValue('Incorrect');
});

test('目标周期和日期编辑保留暂停恢复设置',async({page})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();await page.getByLabel('家长用户名').fill('browser-goal-'+Date.now());await page.getByLabel('家长密码').fill('goal-browser-private-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();await page.getByLabel('孩子昵称').fill('目标同学');await page.getByRole('button',{name:'创建学生',exact:true}).click();await page.getByRole('button',{name:'创建混合运算样例'}).click();await page.getByRole('button',{name:'审核答案与映射后发布'}).click();await page.getByRole('button',{name:'绑定当前学生'}).click();await page.getByRole('button',{name:/进度与计划/}).click();
  await page.getByRole('button',{name:'听力模板'}).click();await page.getByLabel('目标名称').fill('每周英语听力');await page.getByLabel('目标周期').selectOption('Weekly');await page.getByLabel('每周完成次数').fill('2');await page.getByLabel('目标开始日期').fill('2026-10-01');await page.getByLabel('目标结束日期').fill('2026-12-31');await page.getByLabel('目标优先级').selectOption('5');await page.getByRole('button',{name:'添加目标',exact:true}).click();await expect(page.getByRole('heading',{name:'每周英语听力'})).toBeVisible();await expect(page.getByText(/每周 2 次.*2026-10-01 至 2026-12-31/)).toBeVisible();await page.getByRole('button',{name:'暂停目标'}).click();await page.getByRole('button',{name:'恢复目标'}).click();await page.getByRole('button',{name:'编辑目标'}).click();await expect(page.getByLabel('每周完成次数')).toHaveValue('2');await expect(page.getByLabel('目标结束日期')).toHaveValue('2026-12-31');await expect(page.getByLabel('目标优先级')).toHaveValue('5');await page.getByLabel('目标名称').fill('英语听力新安排');await page.getByRole('button',{name:'保存目标修改'}).click();await expect(page.getByRole('heading',{name:'英语听力新安排'})).toBeVisible();
});

test('家庭负责人管理成员、导出全家包并取得删除回执',async({page})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();await page.getByLabel('家长用户名').fill('browser-family-'+Date.now());await page.getByLabel('家长密码').fill('family-browser-private-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();await page.getByRole('button',{name:/家庭设置/}).click();
  await expect(page.getByRole('heading',{name:'家庭数据与成员'})).toBeVisible();await page.getByLabel('成员用户名').fill('browser-member-'+Date.now());await page.getByLabel('初始密码').fill('family-member-private-2026');await page.getByRole('button',{name:'添加成员',exact:true}).click();await expect(page.getByText('成员已创建，可使用自己的账号登录。')).toBeVisible();await expect(page.getByLabel('初始密码')).toHaveValue('');
  const downloadPromise=page.waitForEvent('download');await page.getByRole('link',{name:'下载全家数据包'}).click();const download=await downloadPromise;expect(download.suggestedFilename()).toMatch(/^learning-family-.*\.zip$/);expect(await download.failure()).toBeNull();
  await page.getByRole('button',{name:'预览全家删除范围'}).click();await page.getByLabel('负责人密码').fill('family-browser-private-2026');await page.getByLabel('输入“永久删除家庭”').fill('永久删除家庭');await page.getByRole('button',{name:'确认永久删除全家数据'}).click();await expect(page.getByRole('heading',{name:'全家数据已删除'})).toBeVisible();await expect(page.getByRole('button',{name:'下载删除回执'})).toBeVisible();await expect(page.getByLabel('家长用户名')).toBeVisible();
});

test('教材目录编辑发布后选择单元练习与课程听力目标',async({page})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();await page.getByLabel('家长用户名').fill('browser-directory-'+Date.now());await page.getByLabel('家长密码').fill('directory-browser-private-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();await page.getByLabel('孩子昵称').fill('目录同学');await page.getByRole('button',{name:'创建学生',exact:true}).click();await page.getByRole('button',{name:'创建混合运算样例'}).click();await page.getByRole('button',{name:'编辑',exact:true}).click();await expect(page.getByLabel('教材印次与版本说明')).toHaveValue('具体印次待核对');await page.getByLabel('教材印次与版本说明').fill('家庭试用版本说明，正式印次待核对');await page.getByRole('button',{name:'新增课程',exact:true}).click();await page.getByLabel('课程名称').fill('自备英语听力');await page.getByLabel('课程提供方').fill('家庭自备音频');await page.getByRole('button',{name:'保存并退回待审核'}).click();await expect(page.getByRole('heading',{name:'编辑内容草稿'})).toHaveCount(0);await page.getByRole('button',{name:'审核答案与映射后发布'}).click();await page.getByRole('button',{name:'绑定当前学生'}).click();await page.getByRole('button',{name:/进度与计划/}).click();
  await expect(page.getByLabel('课时').locator('optgroup').filter({has:page.locator('option',{hasText:'小熊购物'})})).toHaveAttribute('label','第 1 单元 · 混合运算');
  await page.getByRole('button',{name:'能力练习模板'}).click();await page.getByLabel('目标教材单元').selectOption({label:'第 1 单元 · 混合运算'});await expect(page.getByLabel('目标能力范围')).toHaveValue('整个选定范围');await page.getByRole('button',{name:'添加目标',exact:true}).click();await expect(page.getByRole('heading',{name:'能力小练习'})).toBeVisible();await expect(page.getByText('单元：混合运算',{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'听力模板'}).click();await page.getByLabel('目标课程范围').selectOption({label:'自备英语听力 · 家庭自备音频'});await page.getByRole('button',{name:'添加目标',exact:true}).click();await expect(page.getByText('课程：自备英语听力',{exact:true})).toBeVisible();await page.getByRole('button',{name:'生成草稿',exact:true}).click();await expect(page.locator('.task-row').filter({hasText:'能力小练习'})).toHaveCount(1);await expect(page.locator('.task-row').filter({hasText:'英语听力'})).toHaveCount(1);
});

test('能力拆分提案 → 人工审核 → 预览确认与历史',async({page})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();await page.getByLabel('家长用户名').fill('browser-change-'+Date.now());await page.getByLabel('家长密码').fill('test-private-change-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();await page.getByLabel('孩子昵称').fill('能力变更试用');await page.getByRole('button',{name:'创建学生',exact:true}).click();await page.getByRole('button',{name:'创建混合运算样例'}).click();await page.getByRole('button',{name:'审核答案与映射后发布'}).click();
  await expect(page.getByRole('heading',{name:'内容版本 1',exact:true})).toBeVisible();
  const setup=await page.evaluate(async()=>{
    let etag='';async function request(path:string,body?:unknown){const response=await fetch('/api/v1'+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json','X-Learning-Request':'1','If-Match':etag,'Idempotency-Key':crypto.randomUUID()},body:body===undefined?undefined:JSON.stringify(body)});etag=response.headers.get('etag')||etag;const result=await response.json();if(!response.ok)throw new Error(result.title);return result;}
    const content=await request('/content');const r1=content.releases[0];const cat=JSON.parse(r1.payload),old=cat.kcs[0];const node=(name:string,behavior:string,boundary:string)=>({id:crypto.randomUUID(),revisionId:crypto.randomUUID(),code:'TEST.SPLIT.'+crypto.randomUUID(),name,behavior,boundary,type:'Procedure'});const a=node('先乘后加减','独立先算乘法再算加减','不含除法或括号'),b=node('先除后加减','独立先算除法再算加减','不含乘法或括号');const question=(k:any,stem:string,answer:string)=>({id:crypto.randomUUID(),revisionId:crypto.randomUUID(),stem,answer,explanation:'先乘除后加减。',type:'Numeric',difficulty:'Medium',policy:'SingleKC',coverage:'Basic',mappings:[{kcId:k.id,role:'Primary',share:1,mode:'WholeItem'}]});cat.kcs=cat.kcs.filter((k:any)=>k.id!==old.id).concat([a,b]);cat.questions=cat.questions.filter((q:any)=>!q.mappings.some((m:any)=>m.kcId===old.id)).concat([question(a,'3 + 4 × 2 = ?','11'),question(b,'18 − 12 ÷ 3 = ?','14')]);for(const l of cat.lessons)if(l.kcIds.includes(old.id)){l.kcIds=l.kcIds.filter((id:string)=>id!==old.id).concat([a.id,b.id]);l.revisionId=crypto.randomUUID();}for(const r of cat.resources)if(r.kcIds.includes(old.id))r.kcIds=r.kcIds.filter((id:string)=>id!==old.id).concat([a.id,b.id]);cat.relations=cat.relations.filter((r:any)=>r.from!==old.id&&r.to!==old.id);const draft=await request('/content/drafts',{title:'拆分后的独立测量内容',catalog:cat});await request(`/content/drafts/${draft.id}:review`,{});const preview=await request(`/content/drafts/${draft.id}/preview`);const r2=await request(`/content/drafts/${draft.id}:publish`,{previewHash:preview.hash});return {from:r1.id,to:r2.id};
  });
  await page.getByRole('button',{name:/今日学习/}).click();await page.getByRole('button',{name:/内容与发布/}).click();const changes=page.getByRole('region',{name:'能力变更提案'});await changes.getByRole('button',{name:'新建能力变更提案'}).click();await changes.getByLabel('来源内容版本').selectOption(setup.from);await changes.getByLabel('生效内容版本').selectOption(setup.to);await changes.getByRole('checkbox',{name:'先乘除后加减',exact:true}).check();await changes.getByRole('checkbox',{name:'先乘后加减',exact:true}).check();await changes.getByRole('checkbox',{name:'先除后加减',exact:true}).check();await changes.getByLabel('变更理由').fill('根据测量边界拆分，分别检验乘法和除法优先。');await changes.screenshot({path:'../../test-results/knowledge-change-draft-tablet.png'});await changes.getByRole('button',{name:'保存提案草稿'}).click();await expect(changes.getByText('提案草稿已保存，尚未停用旧能力或影响学习记录。')).toBeVisible();await changes.getByRole('button',{name:'编辑提案',exact:true}).click();await changes.getByLabel('变更理由').fill('根据测量边界拆分，分别检验乘法和除法优先。补充人工复核。');await changes.getByRole('button',{name:'保存提案草稿'}).click();await expect(changes.getByRole('button',{name:'提交变更审核'})).toBeEnabled();await changes.getByRole('button',{name:'提交变更审核'}).click();await changes.getByLabel('审核依据').fill('题目、答案和能力边界分别核对，旧证据不转移。');await changes.getByRole('button',{name:'通过变更审核'}).click();await changes.getByRole('button',{name:'预览记录能力变更'}).click();await expect(changes.getByRole('button',{name:'确认记录能力变更',exact:true})).toBeDisabled();await changes.getByLabel('输入“记录能力变更”').fill('记录能力变更');await changes.getByRole('button',{name:'确认记录能力变更',exact:true}).click();await expect(changes.getByText('旧能力已停用，历史版本和任务仍保留；学生内容版本需由家长单独选择。')).toBeVisible();await changes.getByRole('button',{name:'查看审核历史'}).click();await expect(changes.getByText('已记录 2 条来源到目标关系；历史证据保持不变。')).toBeVisible();await expect(changes.getByText('根据测量边界拆分，分别检验乘法和除法优先。', {exact:true})).toBeVisible();await changes.getByRole('button',{name:'收起审核历史'}).click();
});

test('家长查看后台运行状态与实际备份说明',async({page,context})=>{
  await page.goto('/');await page.getByRole('button',{name:'首次使用？创建家庭'}).click();await page.getByLabel('家长用户名').fill('browser-ops-'+Date.now());await page.getByLabel('家长密码').fill('test-private-operations-2026');await page.getByRole('button',{name:'创建私有家庭'}).click();await page.getByLabel('孩子昵称').fill('运行状态试用');await page.getByRole('button',{name:'创建学生',exact:true}).click();await page.getByRole('button',{name:/家庭设置/}).click();const status=page.getByRole('region',{name:'后台运行状态'});await expect(status.getByText('当前家庭未发现结果积压或建库异常。')).toBeVisible();await expect(status.getByText(/待处理 0 项/)).toBeVisible();const response=await context.request.get('/api/v1/operations');expect(response.ok()).toBeTruthy();const actual=await response.json();expect(actual.backup.verified).toBeFalsy();await expect(status.getByText(actual.backup.notice,{exact:true})).toBeVisible();if(actual.backup.configured){await expect(status.getByText(new RegExp('临时库恢复：'+(actual.backup.restoreVerified?'已验证':'待演练')))).toBeVisible();}await expect(status.getByRole('heading',{name:'本地存储',exact:true})).toBeVisible();await status.getByRole('button',{name:'检查后台状态'}).click();await expect(status.getByRole('button',{name:'检查后台状态'})).toBeEnabled();await status.screenshot({path:'../../test-results/operations-tablet.png'});
});
