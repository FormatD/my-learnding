import {test,expect} from '@playwright/test';
test('隔离备份状态：真实调度与归档，恢复及独立盘仍待验',async({page})=>{
  test.skip(!process.env.BACKUP_TEST_USER,'需隔离备份进程及数据库');
  await page.goto('/');
  await page.getByLabel('家长用户名').fill(process.env.BACKUP_TEST_USER!);
  await page.getByLabel('家长密码').fill(process.env.BACKUP_TEST_PASSWORD!);
  await page.getByRole('button',{name:'登录',exact:true}).click();
  await page.getByRole('button',{name:/家庭设置/}).click();
  const panel=page.getByRole('region',{name:'后台运行状态'});
  await expect(panel.getByText('调度进程：已确认运行')).toBeVisible();
  await expect(panel.getByText(/最近成功快照：/)).toBeVisible();
  const restored=process.env.BACKUP_TEST_RESTORED==='1';
  await expect(panel.getByText(new RegExp('归档摘要：已核对.*临时库恢复：'+(restored?'已验证':'待演练')+'.*独立物理磁盘：待确认'))).toBeVisible();
  if(restored)await expect(panel.getByText(/演练核对时间：/)).toBeVisible();
  await panel.screenshot({path:restored?'../../test-results/backup-restored-tablet.png':'../../test-results/backup-status-tablet.png'});
});
