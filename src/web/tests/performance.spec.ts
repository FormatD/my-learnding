import { test, expect } from '@playwright/test';
import { writeFileSync } from 'node:fs';

test('隔离性能测量：模拟平板网络今日页可交互', async ({ page, context }) => {
  test.skip(!process.env.PERFORMANCE_TEST_USER, '仅隔离性能夹具提供账号与数据');
  test.setTimeout(180000);
  await page.goto('/');
  await page.getByLabel('家长用户名').fill(process.env.PERFORMANCE_TEST_USER!);
  await page.getByLabel('家长密码').fill(process.env.PERFORMANCE_TEST_PASSWORD!);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await page.getByRole('combobox', { name: '切换学生' }).selectOption({label:'性能验收学生1'});
  await expect(page.getByRole('button', { name: /开始学习/ })).toBeEnabled();
  await page.getByRole('button', { name: '进入孩子模式 ↗' }).click();
  await expect(page.getByRole('button', { name: /开始学习/ })).toBeEnabled();
  const cdp = await context.newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', {
    offline: false, latency: 75, downloadThroughput: 10 * 1024 * 1024 / 8,
    uploadThroughput: 2 * 1024 * 1024 / 8, connectionType: 'wifi',
  });
  const values:number[] = [];
  for(let i=0;i<60;i++) {
    // A new document load includes the app's identity, student and today's plan fetches.
    const started=performance.now();
    await page.goto('/', {waitUntil:'domcontentloaded'});
    await expect(page.getByRole('button', {name:/开始学习/})).toBeEnabled();
    await expect(page.getByRole('heading', {name:/今天也向前一步/})).toBeVisible();
    values.push(performance.now()-started);
  }
  // Verify the observed enabled action really opens the question, beyond merely rendering a label.
  await page.getByRole('button', {name:/开始学习/}).click();
  await expect(page.getByLabel('你的答案')).toBeEnabled();
  const ordered=[...values].sort((a,b)=>a-b);
  const p95=ordered[Math.ceil(.95*values.length)-1];
  writeFileSync(process.env.PERFORMANCE_TEST_OUTPUT!, JSON.stringify({
    samples:values.length, p95Ms:Math.round(p95*100)/100, maxMs:Math.round(Math.max(...values)*100)/100,
    limitMs:2000, passed:p95<=2000, viewport:'1024×1366', physicalTablet:false,
    network:{simulated:true,latencyMs:75,downloadMbps:10,uploadMbps:2},
    cache:'同一浏览器会话重复加载；不含登录，已登录孩子模式，浏览器缓存保留',
    rawMs:values.map(v=>Math.round(v*100)/100),
  },null,2));
  expect(p95,'模拟网络 p95 超过建议目标').toBeLessThanOrEqual(2000);
});
