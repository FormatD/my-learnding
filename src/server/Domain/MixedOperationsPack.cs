using System.Security.Cryptography;
using System.Text;
namespace Learning;

// Authored original material. A draft requires human review and explicit publishing.
public static class MixedOperationsPack
{
    static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("mixed-original-v1:"+key))[..16]);
    public static Catalog Create()
    {
        var sample=Content.Fixture();
        var kcs=sample.Kcs.Select((k,i)=>k with {Id=Id($"kc:{i}"),RevisionId=Id($"kc-revision:{i}"),Code=$"MATH.G3.MIXED.ORIGINAL1.{i+1}",Behavior=i==8?"独立为两步数量关系写出一个综合算式；接受等价列式":"给定算式，独立计算两步运算的结果；不由结果推断未观察的口头解释"}).ToArray();
        var qs=new List<Question>();
        void Add(int kc,string expression,int answer,string first,int variant)
        {
            var index=qs.Count;
            qs.Add(new(Id($"q:{index}"),Id($"q-revision:{index}"),$"计算：{expression} = ?",answer.ToString(System.Globalization.CultureInfo.InvariantCulture),$"先算{first}，再完成剩下的一步。{expression} = {answer}。只判当前能力的结果，不能据此推断阅读或列式能力。","Numeric","Medium","SingleKC",[new(kcs[kc].Id)],Id($"variant:{kc}:{variant}"),Hint:kc==3?"先找小括号。":kc==1||kc==2?"同级运算从左到右。":"先标出乘除部分。"));
        }
        for(var i=0;i<16;i++)
        {
            var a=2+i%4;var b=2+i/4;var product=a*b;var extra=3+i;
            if(i%2==0) Add(0,$"{extra} + {a} × {b}",extra+product,$"{a} × {b} = {product}",0);
            else Add(0,$"{product+extra} − {product} ÷ {a}",product+extra-b,$"{product} ÷ {a} = {b}",1);
            Add(1,$"{30+i} − {a} + {b}",30+i-a+b,$"{30+i} − {a} = {30+i-a}",0);
            if(i%2==0) Add(2,$"{product} ÷ {a} × 2",b*2,$"{product} ÷ {a} = {b}",0);
            else Add(2,$"{a} × {b} ÷ {a}",b,$"{a} × {b} = {product}",1);
            if(i%2==0) Add(3,$"({product-1} + 1) ÷ {a}",b,$"{product-1} + 1 = {product}",0);
            else Add(3,$"{a} × ({b+2} − 2)",product,$"{b+2} − 2 = {b}",1);
            Add(4,$"{a} × {b} + {extra}",product+extra,$"{a} × {b} = {product}",0);
            Add(5,$"{product+extra} − {a} × {b}",extra,$"{a} × {b} = {product}",0);
            Add(6,$"{product} ÷ {a} + {extra}",b+extra,$"{product} ÷ {a} = {b}",0);
            Add(7,$"{product} ÷ {a} − 1",b-1,$"{product} ÷ {a} = {b}",0);
        }
        for(var observed=0;observed<2;observed++) for(var i=0;i<16;i++)
        {
            var a=2+i%4;var b=3+i/4;var n=a*b;var extra=2+i/4;var kind=i%4;
            var story=kind switch {
                0=>$"有 {a} 盒彩笔，每盒 {b} 支，另有 {extra} 支散放的彩笔。一共有多少支？",
                1=>$"有 {n+extra} 张卡片，送出 {a} 组，每组 {b} 张。还剩多少张？",
                2=>$"有 {n} 个苹果，平均装进 {a} 袋。在其中一袋里再放 {extra} 个。这一袋现在有多少个？",
                _=>$"有 {n} 块积木，平均装进 {a} 盒。从其中一盒取出 1 块。这一盒还剩多少块？"};
            var expression=kind switch {0=>$"{a}×{b}+{extra}",1=>$"{n+extra}−{a}×{b}",2=>$"{n}÷{a}+{extra}",_=>$"{n}÷{a}−1"};
            var answer=kind switch {0=>n+extra,1=>extra,2=>b+extra,_=>b-1};
            var index=qs.Count;
            var rubric=observed==0?"家长只判列式是否表达数量关系，接受等价列式；不要求数值结果。":"家长分别记录 model（综合列式）和 calculate（按给定参考算式完成计算）。没有看到某步过程，应记 Unknown；不能从最终答案反推列式正确。";
            qs.Add(new(Id($"q:{index}"),Id($"q-revision:{index}"),story+(observed==0?"只写一个综合算式，不计算。":"先独立列综合算式；家长记录后，给出参考算式，再让孩子计算。"),observed==0?expression:$"列式：{expression}；计算：{answer}",$"参考列式：{expression}；参考结果：{answer}。{rubric}",observed==0?"ShortAnswer":"MultiStep","Medium",observed==0?"SingleKC":"ObservedSteps",observed==0?[new(kcs[8].Id)]:[new(kcs[8].Id,"Primary",.5m,"StepObserved","model"),new(kcs[4+kind].Id,"Secondary",.5m,"StepObserved","calculate")],Id($"story-variant:{kind}"),Hint:"先说每个数表示什么，再找先求的数量。"));
        }
        string[] titles=["先乘除后加减","加减从左到右","乘除从左到右","先算小括号","给定乘加算式","给定乘减算式","给定除加算式","给定除减算式","只列式不计算","乘加与乘减对照","除加与除减对照","括号改变顺序","购物数量关系","平均分后再变化","检查两步运算"];
        string[] texts=[
            "示范：6+3×4，先算3×4=12，再算6+12=18。不要先把6和3相加。一起练：8+2×5。独立练：20−12÷3。核对：18、16。请孩子圈出第一步。",
            "示范：18−6+3，先18−6=12，再12+3=15。不能把6+3整体减去。一起练：20−8+4。独立练：17+5−9。核对：16、13。用箭头标从左到右。",
            "示范：18÷3×2，先18÷3=6，再6×2=12。不能先算3×2。一起练：4×6÷3。独立练：24÷4×3。核对：8、18。乘与除是同级。",
            "示范：(8+4)÷3，先括号内8+4=12，再12÷3=4。一起练：3×(7−2)。独立练：(15−3)÷4。核对：15、3。括号是一整块。",
            "示范：2×6+4，先求两组六个是12，再加4得16。一起练：3×5+2。独立练：4×3+7。核对：17、19。这里给定算式，只练计算。",
            "示范：25−3×6，先3×6=18，再25−18=7。一起练：28−4×5。独立练：19−2×7。核对：8、5。被减去的是乘法结果。",
            "示范：18÷3+5，先18÷3=6，再6+5=11。一起练：24÷4+2。独立练：30÷5+3。核对：8、9。除完后再增加。",
            "示范：24÷4−2，先24÷4=6，再6−2=4。一起练：35÷5−3。独立练：16÷2−5。核对：4、3。确认是在一份里减少。",
            "示范：3盒彩笔，每盒4支，另有2支，综合列式3×4+2。先圈每盒数量，再圈盒数，最后加散支。独立练：2盒，每盒5支，另有3支。核对列式2×5+3，接受交换乘法因子的等价写法；不以计算结果代替列式。",
            "并列示范：3×4+2=14；20−3×4=8。先求同样的12，之后分别增加和从总数拿走。独立练：2×5+6、18−2×5。核对：16、8。说清加或减的是哪个数量。",
            "并列示范：24÷4+2=8；24÷4−2=4。每份原来都是6，变化方向不同。独立练：18÷3+1、18÷3−1。核对：7、5。每份和总数不要混淆。",
            "并列示范：8+4÷2=10；(8+4)÷2=6。前式先除，后式先括号。独立练：6+6÷3、(6+6)÷3。核对：8、4。圈括号，再比较第一步。",
            "示范：4袋贴纸，每袋3张，另外2张，共多少？先求4袋有12张，再加2张；列式4×3+2=14。独立练：5袋每袋2张，从总共18张中送出这5袋，还剩多少？核对列式18−5×2，结果8。列式与计算分别观察。",
            "示范：18块饼干平均装3袋，从其中一袋拿2块，这袋剩18÷3−2=4块。独立练：24块平均4袋，其中一袋再加3块。这袋有多少？核对列式24÷4+3，结果9。提醒：不是每袋都变化，也不是总数变化。",
            "检查示范：30−4×5。把第一步4×5=20写下，第二步30−20=10；用10+20=30检查。独立练：(9+6)÷3与24÷3×2。核对：5、16。先检查顺序，再检查两次计算。"];
        int[][] targets=[[0],[1],[2],[3],[4],[5],[6],[7],[8],[4,5],[6,7],[0,3],[4,5,8],[6,7,8],[0,1,2,3]];
        var resources=titles.Select((title,i)=>new Resource(Id($"resource:{i}"),title+" · 原创纸笔课",$"准备纸笔。家长先读示范，孩子写中间步骤，再独立练习。{texts[i]} 完成后请孩子用自己的话说第一步；讲解后练习不冒充独立测量证据。",6,targets[i].Select(t=>kcs[t].Id).ToArray())).ToArray();
        var textbook=new Textbook(Id("textbook"),Id("textbook-revision"),"北师大版","具体印次待核对","Math",3,"上册");
        var unit=new TextbookUnit(Id("unit"),Id("unit-revision"),textbook.Id,"混合运算",1);
        var lessons=sample.Lessons.Select((l,i)=>l with {Id=Id($"lesson:{i}"),RevisionId=Id($"lesson-revision:{i}"),UnitId=unit.Id,KCIds=l.KCIds.Select(id=>kcs[Array.FindIndex(sample.Kcs,k=>k.Id==id)].Id).ToArray()}).ToArray();
        return new(kcs,qs.ToArray(),resources,lessons,[],[textbook],[unit],[]);
    }
}
