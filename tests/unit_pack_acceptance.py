"""Independently recompute original arithmetic and exercise draft-only creation."""
import ast,json,re,uuid
from pathlib import Path
from fractions import Fraction
from collections import Counter
from api_acceptance import Client
ROOT=Path(__file__).resolve().parents[1]
def calculate(text):
    tree=ast.parse(text.replace('×','*').replace('÷','/').replace('−','-').strip(),mode='eval')
    def visit(n):
        if isinstance(n,ast.Constant) and type(n.value) is int:return Fraction(n.value)
        if isinstance(n,ast.BinOp):
            a,b=visit(n.left),visit(n.right)
            if isinstance(n.op,ast.Add):return a+b
            if isinstance(n.op,ast.Sub):return a-b
            if isinstance(n.op,ast.Mult):return a*b
            if isinstance(n.op,ast.Div):
                value=a/b
                assert value.denominator==1,'non-integral division'
                return value
        raise AssertionError('unsupported expression')
    return visit(tree.body)
def check(c):
    assert len(c['questions'])==160 and len(c['resources'])==15 and len(c['kcs'])==9
    counts=Counter()
    for q in c['questions']:
        counts[q['type']]+=1
        if q['type']=='Numeric':
            expression=q['stem'].removeprefix('计算：').split('=')[0]
            value=calculate(expression);assert value==int(q['answer']) and 0<=value<=100
        else:
            story=q['stem'];nums=list(map(int,re.findall(r'\d+',story)))
            if '盒彩笔' in story:a,b,e=nums;value=a*b+e
            elif '张卡片' in story:total,a,b=nums;value=total-a*b
            elif '个苹果' in story:total,a,e=nums;value=Fraction(total,a)+e
            else:total,a,e=nums;value=Fraction(total,a)-e
            expression=q['answer'] if q['type']=='ShortAnswer' else q['answer'].split('；')[0].removeprefix('列式：')
            assert calculate(expression)==value
            if q['type']=='MultiStep':
                assert int(q['answer'].split('计算：')[1])==value
                assert [m['step'] for m in q['mappings']]==['model','calculate']
                assert [m['share'] for m in q['mappings']]==[.5,.5]
        assert q['variantGroupId'] and q['coverage']=='Basic'
    assert counts=={'Numeric':128,'ShortAnswer':16,'MultiStep':16}
    print('PASS 160题独立语法求值与数量关系核算；整数除法、数值范围和步骤映射')
def main():
    reference=json.loads((ROOT/'docs/content/mixed-operations-original-v1.json').read_text());check(reference)
    c=Client();c.request('/auth/register',{'userName':'pack-'+uuid.uuid4().hex[:12],'password':'private-'+uuid.uuid4().hex},expected=201);c.request('/me')
    key=str(uuid.uuid4());d=c.request('/content/unit-pack',{},key=key);again=c.request('/content/unit-pack',{},key=key)
    assert d['id']==again['id'] and d['status']=='Draft' and not d['reviewedBy']
    assert json.loads(d['payload'])==reference
    assert len(c.request('/content')['releases'])==0
    c.request('/content/drafts/'+d['id']+':publish',{'previewHash':'none'},expected=422)
    print('PASS 创建与重试仅留下同一待审核草稿，未审核拒绝发布')
    c.request('/content/drafts/'+d['id']+':review',{});preview=c.request('/content/drafts/'+d['id']+'/preview')
    c.request('/content/drafts/'+d['id']+':publish',{'previewHash':preview['hash']})
    d2=c.request('/content/unit-pack',{});c.request('/content/drafts/'+d2['id']+':review',{});p2=c.request('/content/drafts/'+d2['id']+'/preview')
    c.request('/content/drafts/'+d2['id']+':publish',{'previewHash':p2['hash']})
    assert len(c.request('/content')['releases'])==2
    print('PASS 隔离测试家庭完整审核/发布、重复准备沿用稳定身份而不冲突')
    other=Client();other.request('/auth/register',{'userName':'pack-other-'+uuid.uuid4().hex[:10],'password':'private-'+uuid.uuid4().hex},expected=201);other.request('/me')
    other.request('/content/drafts/'+d['id']+'/preview',expected=404)
    print('PASS 跨家庭不可读取草稿')
if __name__=='__main__':main()
