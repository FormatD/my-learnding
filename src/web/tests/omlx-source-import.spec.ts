import {test,expect} from '@playwright/test';
test('本地识别结果先核对再保存，模型按钮独立于模拟流程',async({page,context})=>{
 const r=await context.request.post('/api/v1/auth/register',{headers:{'X-Learning-Request':'1','Idempotency-Key':crypto.randomUUID()},data:{userName:'ocr-import-ui-'+Date.now(),password:'only-test-ocr-'+crypto.randomUUID()}});expect(r.status()).toBe(201);
 await page.goto('/');await page.getByRole('button',{name:/辅助建库/}).click();
 const source={title:'二年级除法·识别结果待核对',text:'PDF第6页，书内第2页：每盘放6个苹果，18个苹果可以放几盘？',allowExternalAI:false,usageScope:'FamilyOnly'};
 let writes=0,modelCalls=0;page.on('request',r=>{if(r.url().endsWith('/content/sources')&&r.method()==='POST')writes++;if(r.url().endsWith('/builder/runs')&&r.method()==='POST')modelCalls++});
 await expect(page.getByLabel('选择本地识别结果',{exact:true})).toBeEnabled();await page.getByLabel('选择本地识别结果',{exact:true}).setInputFiles({name:'source-input.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(source))});
 await expect(page.getByLabel('来源标题',{exact:true})).toHaveValue(source.title);await expect(page.getByLabel('文本内容',{exact:true})).toHaveValue(source.text);expect(writes).toBe(0);expect(modelCalls).toBe(0);
 const saved=page.waitForResponse(r=>r.url().endsWith('/content/sources')&&r.request().method()==='POST');await page.getByRole('button',{name:'保存来源',exact:true}).click();expect((await saved).status()).toBe(201);
 await expect(page.getByRole('button',{name:'用本机模型生成候选',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'验证候选流程',exact:true})).toBeVisible();expect(modelCalls).toBe(0);
 const row=page.locator('.content-row').filter({hasText:source.title});await expect(row).toContainText('FamilyOnly');await page.screenshot({path:'../../.local/textbook-workflow/import-ui.png',fullPage:true});
});
