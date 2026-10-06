#!/usr/bin/env python3
"""Prepare original, unreviewed practice for two division contexts; never publish."""
import argparse,hashlib,json,uuid
from pathlib import Path
NAMESPACE=uuid.UUID('c33e891a-e82b-4c36-8f4d-33288eafda93')
def identity(name):return str(uuid.uuid5(NAMESPACE,name))
def prepare():
 names=['有余数除法的竖式计算','至少需要多少完整份','最多能取得多少完整份']
 behaviors=['独立用竖式计算两位数除以一位数，写出商和余数，并检查余数小于除数。','在已给出商和余数的容量情境中，判断有剩余时还需一份；整除时不增加。','在已给出商和余数的购买或时间情境中，判断只能取得完整份，不能把不足一份的余量进位。']
 boundaries=['只测量实际观察到的计算步骤，不凭最终答案推断竖式过程；不测应用题建模。','商和余数直接给出，除数为正整数；只测余数处理，不把计算或阅读速度当作该能力。','单位价格或每份用量为正整数，商和余数直接给出；只测完整份限制，不测列式计算。']
 kcs=[dict(id=identity('kc-'+str(i)),revisionId=identity('kc-revision-'+str(i)),code='MATH.G2.DIV.ORIGINAL_DRAFT1.'+str(i+1),name=n,behavior=behaviors[i],boundary=boundaries[i],type='Procedure' if i==0 else 'Application',requiredCoverage=['Basic'],subject='MATH',gradeMin=2,gradeMax=2) for i,n in enumerate(names)]
 questions=[];checks=[]
 def question(stem,answer,explanation,target,kind='Numeric',policy='SingleKC',steps=None,group=''):
  index=len(questions)+1
  mappings=[dict(kcId=kcs[target]['id'],role='Primary',share=.5,mode='StepObserved',step=s) for s in steps] if steps else [dict(kcId=kcs[target]['id'],role='Primary',share=1,mode='WholeItem',step=None)]
  questions.append(dict(id=identity('q-'+str(index)),revisionId=identity('q-revision-'+str(index)),stem=stem,answer=str(answer),explanation=explanation,type=kind,difficulty='Medium',policy=policy,mappings=mappings,variantGroupId=identity('variant-'+group),coverage='Basic',hint='先说清题目要求，再独立作答；使用提示需记录。'))
 for a,b in [(23,5),(38,6),(47,8),(29,4),(34,7),(56,9),(42,6),(63,7)]:
  q,r=divmod(a,b)
  question(f'用竖式计算 {a}÷{b}，写出商和余数。家长分别观察“求商”和“求余数并检查”两步，未观察的步骤记未知。',f'商{q}，余数{r}',f'{a}={b}×{q}+{r}，0≤{r}<{b}。家长核对实际竖式过程，不能仅凭结果认定步骤独立完成。',0,'MultiStep','ObservedSteps',['求商','求余数并检查'],'written-division')
  checks.append(dict(kind='division',a=a,b=b,q=q,r=r))
 contexts=[('小朋友乘车','人','辆车','每辆车最多坐','至少需要几辆车'),('点心装盒','块','个盒子','每个盒子最多装','至少需要几个盒子'),('花苗装盘','株','个托盘','每个托盘最多放','至少需要几个托盘')]
 for i,(a,b) in enumerate([(22,5),(31,6),(43,8),(20,5),(36,6),(40,8)]):
  q,r=divmod(a,b);n,unit,container,label,ask=contexts[i%3];answer=q+(r>0)
  stem=f'{n}：共有{a}{unit}，{label}{b}{unit}。已知{a}÷{b}={q}……{r}。{ask}？只填数量。'
  question(stem,answer,f'已经有{q}{container}，'+(f'还剩{r}{unit}，必须再准备1份，因此至少{answer}份。' if r else f'没有剩余，不再增加，至少{answer}份。'),1,group='ceil-'+str(i%3));checks.append(dict(kind='ceil',a=a,b=b,answer=answer))
 contexts=[('买完整小本子','元','本小本子','每本','最多能买几本'),('租一辆小车','元','小时','每小时','最多能租几整小时'),('制作完整小袋','厘米丝带','个小袋','每个小袋用','最多能做几个完整小袋')]
 for i,(a,b) in enumerate([(26,7),(35,8),(44,9),(28,7),(32,8),(45,9)]):
  q,r=divmod(a,b);n,unit,item,label,ask=contexts[i%3]
  stem=f'{n}：共有{a}{unit}，{label}{b}{unit}。已知{a}÷{b}={q}……{r}。{ask}？只填数量。'
  question(stem,q,f'可以取得{q}{item}，余量{r}小于每份用量{b}，不能再取得一整份，所以最多{q}。整除时同样保留商。',2,group='floor-'+str(i%3));checks.append(dict(kind='floor',a=a,b=b,answer=q))
 resources=[]
 activities=[('竖式中的商与余数','准备纸笔。家长示范19÷4：商4，余数3；检查4×4+3=19且3<4。孩子独立算27÷5，家长分别观察求商与余数检查；参考商5余2。讲解后的练习不冒充无提示测量。',[0]),('还有剩余，容器够不够','画5个座位一组。22人先安排4组，还剩2人，需第5组。对照20人恰好4组，不再增加。孩子解释两种情况；商和余数已经给出，不同时测计算。',[1]),('余量不够完整一份','假设每本练习本7元，共26元。已知26÷7=3余5：3本共21元，余下5元不够第4本。对照28元可以买4本。孩子解释为何不能进一。',[2]),('同样的商和余数，不同问题','都给出22÷5=4余2。22人乘每辆最多5人的车至少5辆；22元买每本5元的完整本子最多4本。先说每个数与余数的单位，再比较限制；不能教成“有余数都加1”。',[1,2])]
 for i,(title,instruction,targets) in enumerate(activities):resources.append(dict(id=identity('resource-'+str(i)),revisionId=identity('resource-revision-'+str(i)),title=title+' · 原创纸笔活动',paperReference=instruction,minutes=5,kcIds=[kcs[t]['id'] for t in targets],url=None))
 textbook=dict(id=identity('book'),revisionId=identity('book-revision'),publisher='北师大版',edition='用户本地二年级下册；印次与适配待审核',subject='Math',grade=2,semester='下册',sourceId=None)
 unit=dict(id=identity('unit'),revisionId=identity('unit-revision'),textbookId=textbook['id'],title='除法 · 两页情境补充（非整单元）',sequence=1)
 lessons=[dict(id=identity('lesson-'+str(i)),revisionId=identity('lesson-revision-'+str(i)),title=title,sequence=i+1,kcIds=[kcs[t]['id'] for t in targets],unitId=unit['id'],courseId=None,estimatedMinutes=5,sourceRefs=[]) for i,(title,targets) in enumerate([('竖式计算',[0]),('最多与至少',[1,2])])]
 return dict(title='除法两页情境原创草稿 · 20题 / 4资源（待审核）',catalog=dict(kcs=kcs,questions=questions,resources=resources,lessons=lessons,relations=[],textbooks=[textbook],units=[unit],courses=[],mappingCoverage=None)),checks

def verify(checks):
 for c in checks:
  a,b=c['a'],c['b'];assert b>0
  if c['kind']=='division':assert c['q']*b+c['r']==a and 0<=c['r']<b
  elif c['kind']=='ceil':assert c['answer']*b>=a and (c['answer']-1)*b<a
  else:assert c['answer']*b<=a and (c['answer']+1)*b>a

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=Path('.local/textbook-workflow/division-original-draft'));args=parser.parse_args();pack,checks=prepare();verify(checks);args.output.mkdir(parents=True,exist_ok=True)
 text=json.dumps(pack,ensure_ascii=False,indent=2);path=args.output/'content-pack.json';path.write_text(text);path.chmod(0o600)
 manifest={'version':'division-original-draft/1','contentSHA256':hashlib.sha256(text.encode()).hexdigest(),'humanReview':'Pending','sourceReview':'Pending','scope':'Original supplement for privately transcribed PDF pages 15 and 17, not a full unit','questions':20,'resources':4,'kcs':3,'independentTemplateGroups':len({q['variantGroupId'] for q in pack['catalog']['questions']}),'coverage':['Basic'],'numericChecks':checks,'limitations':['No publisher question reproduction','No candidate acceptance or human review claimed','No formal publishing or student binding','Vertical procedure requires actual observed steps','Numbers and wording still need human review']}
 m=args.output/'manifest.json';m.write_text(json.dumps(manifest,ensure_ascii=False,indent=2));m.chmod(0o600)
 review=['# 除法两页情境原创题包 · 待审核','', '范围：北师大版二年级下册第一单元除法的两页情境补充；具体印次待核对。20题、4份原创纸笔资源、3个拟议能力，非整单元题库。全部内容为原创，不复刻教材题目；不会自动接受模型候选。', '', '数值关系校验通过不等于人工审核。只覆盖Basic，7个变式组不是20个独立模板；未观察的竖式步骤记未知。最多/至少题已给出商和余数，只测余数处理。', '', '审核人、教材适配、每题答案与映射、资源可执行性、问题修改与复核：待填写。', '', '## 拟议能力与边界', '']
 for k in pack['catalog']['kcs']:review.extend([k['name']+'：'+k['behavior']+' 边界：'+k['boundary'],''])
 review.extend(['## 逐题核对','','|编号|题面|参考答案|测量目标|','|---|---|---|---|'])
 names={k['id']:k['name'] for k in pack['catalog']['kcs']}
 for i,q in enumerate(pack['catalog']['questions'],1):review.append(f"|{i:02}|{q['stem']}|{q['answer']}|{names[q['mappings'][0]['kcId']]}|")
 review.extend(['','## 原创纸笔资源',''])
 for r in pack['catalog']['resources']:review.extend([r['title']+'（'+str(r['minutes'])+'分钟）：'+r['paperReference'],''])
 guide=args.output/'review.md';guide.write_text('\n'.join(review));guide.chmod(0o600)
 print('Prepared original draft: 20 questions, 4 resources, 3 proposed KC definitions; all human review pending.')
if __name__=='__main__':main()
