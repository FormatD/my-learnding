using System.Security.Cryptography;
using System.Text;

namespace Learning;
public static class Content
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string MeasurementSignature(KC k)=>Hash(Json.Write(new {behavior=k.Behavior.Trim().Normalize(),boundary=k.Boundary.Trim().Normalize(),k.Type,coverage=(k.RequiredCoverage??["Basic"]).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray()}));
    public static string[] Validate(Catalog c)
    {
        var errors = new List<string>();errors.AddRange(CatalogDirectory.Validate(c));
        if(c.Kcs.Length==0 || c.Questions.Length==0)errors.Add("正式内容至少需要一个能力和一道人工作答审核过的测量题");
        var ids = c.Kcs.Select(k => k.Id).ToHashSet();
        if (ids.Count != c.Kcs.Length || c.Kcs.Select(k => k.Code).Distinct().Count() != c.Kcs.Length) errors.Add("KC 身份和编码必须唯一");
        if (c.Questions.Select(q => q.Id).Distinct().Count() != c.Questions.Length) errors.Add("题目身份必须唯一");
        foreach (var k in c.Kcs)
        {
            if (k.Id == Guid.Empty || k.RevisionId == Guid.Empty || string.IsNullOrWhiteSpace(k.Name) || string.IsNullOrWhiteSpace(k.Behavior) || string.IsNullOrWhiteSpace(k.Boundary)) errors.Add($"{(string.IsNullOrWhiteSpace(k.Name)?"未命名能力":k.Name)}: 缺少可测行为、边界或身份");
            if(!new[]{"Procedure","Concept","Application","Representation","Misconception"}.Contains(k.Type))errors.Add($"{(string.IsNullOrWhiteSpace(k.Name)?"未命名能力":k.Name)}: 能力类型无效");
            if (!c.Questions.Any(q => q.Mappings.Any(m => m.KCId == k.Id && m.Mode != "None" && m.Share>0 && m.Role is "Primary" or "Secondary"))) errors.Add($"{(string.IsNullOrWhiteSpace(k.Name)?"未命名能力":k.Name)}: 没有经过审核的测量题");
        }
        foreach (var (q,index) in c.Questions.Select((q,i)=>(q,i)))
        {
            if (q.Id == Guid.Empty || q.RevisionId == Guid.Empty || string.IsNullOrWhiteSpace(q.Stem) || string.IsNullOrWhiteSpace(q.Answer) || string.IsNullOrWhiteSpace(q.Explanation)) errors.Add($"第 {index+1} 题: 题面或答案缺失");
            if (!new[] { "Numeric", "Fill", "Choice", "ShortAnswer", "MultiStep" }.Contains(q.Type)) errors.Add($"第 {index+1} 题: 未支持题型");
            if (!new[] { "Easy", "Medium", "Hard" }.Contains(q.Difficulty)) errors.Add($"第 {index+1} 题: 难度无效");
            if (!new[] { "NoEvidence", "SingleKC", "ObservedSteps" }.Contains(q.Policy)) errors.Add($"第 {index+1} 题: 归因策略无效");
            if (q.Type == "Numeric" && !decimal.TryParse(q.Answer, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _)) errors.Add($"第 {index+1} 题: 数值答案无效");
            if(q.Mappings.Any(m=>!new[]{"Primary","Secondary","Prerequisite","Context"}.Contains(m.Role) || !new[]{"WholeItem","StepObserved","None"}.Contains(m.Mode) || m.Mode!="None" && m.Share<=0))errors.Add($"第 {index+1} 题: 映射角色、测量方式或份额无效");
            if(q.Policy=="ObservedSteps" && q.Type is not "ShortAnswer" and not "MultiStep")errors.Add($"第 {index+1} 题: 步骤测量需家长观察判分题型");
            if(q.Mappings.GroupBy(m=>new {m.KCId,m.Mode,m.Step}).Any(g=>g.Count()>1))errors.Add($"第 {index+1} 题: 同一观察点不能重复映射同一能力");
            if (q.Mappings.Any(m => !ids.Contains(m.KCId) || m.Share < 0 || m.Share > 1)) errors.Add($"第 {index+1} 题: 映射引用或份额错误");
            if (q.Mappings.Where(m => m.Mode != "None").Sum(m => m.Share) > 1) errors.Add($"第 {index+1} 题: 测量份额超过 1");
            if (q.Mappings.Any(m => (m.Role == "Prerequisite" || m.Role == "Context") && m.Mode != "None")) errors.Add($"第 {index+1} 题: 前置/上下文不能测量");
            if (q.Policy == "NoEvidence" && q.Mappings.Any(m => m.Mode != "None")) errors.Add($"第 {index+1} 题: 不可测题含测量映射");
            if (q.Policy == "SingleKC" && (q.Mappings.Count(m => m.Mode == "WholeItem") != 1 || q.Mappings.Any(m => m.Mode != "None" && m.Mode != "WholeItem"))) errors.Add($"第 {index+1} 题: 整题只能测量一个 KC");
            if (q.Policy == "ObservedSteps" && q.Mappings.Any(m => m.Mode != "None" && (m.Mode != "StepObserved" || string.IsNullOrWhiteSpace(m.Step)))) errors.Add($"第 {index+1} 题: 步骤归因缺少观察点");
        }
        if(c.Resources.Select(r=>r.Id).Distinct().Count()!=c.Resources.Length)errors.Add("资源稳定身份不能重复");
        var resourceRevisions=c.Resources.Where(r=>r.RevisionId!=null).Select(r=>r.RevisionId!.Value).ToArray();
        if(resourceRevisions.Distinct().Count()!=resourceRevisions.Length)errors.Add("资源修订身份不能重复");
        var otherIdentities=c.Kcs.Select(k=>k.Id).Concat(c.Questions.Select(q=>q.Id)).Concat(c.Lessons.Select(l=>l.Id)).Concat((c.Textbooks??[]).Select(b=>b.Id)).Concat((c.Units??[]).Select(u=>u.Id)).Concat((c.Courses??[]).Select(course=>course.Id)).ToHashSet();
        var otherRevisions=c.Kcs.Select(k=>k.RevisionId).Concat(c.Questions.Select(q=>q.RevisionId)).Concat(c.Lessons.Where(l=>l.RevisionId!=null).Select(l=>l.RevisionId!.Value)).Concat((c.Textbooks??[]).Select(b=>b.RevisionId)).Concat((c.Units??[]).Select(u=>u.RevisionId)).Concat((c.Courses??[]).Select(course=>course.RevisionId)).ToHashSet();
        if(c.Resources.Any(r=>r.RevisionId!=null && otherIdentities.Contains(r.Id)))errors.Add("资源稳定身份与其他内容身份冲突");
        if(resourceRevisions.Any(otherRevisions.Contains))errors.Add("资源修订身份与其他内容修订冲突");
        foreach (var (r,index) in c.Resources.Select((r,i)=>(r,i)))
        {
            if(r.Id==Guid.Empty || r.RevisionId==Guid.Empty)errors.Add($"第 {index+1} 个资源: 稳定身份或修订身份无效");
            if (string.IsNullOrWhiteSpace(r.Title) || r.Minutes is <1 or >180 || r.KCIds.Length==0 || r.KCIds.Any(id => !ids.Contains(id)) || (string.IsNullOrWhiteSpace(r.PaperReference) && r.Url == null)) errors.Add($"第 {index+1} 个资源: 资源不可执行");
            if (r.Url != null && (!Uri.TryCreate(r.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")) errors.Add($"第 {index+1} 个资源: 外链只允许 HTTPS");
        }
        if(c.Resources.Select(r=>r.Id).Distinct().Count()!=c.Resources.Length || c.Lessons.Select(l=>l.Id).Distinct().Count()!=c.Lessons.Length)errors.Add("资源/课时身份必须唯一");
        if(c.Lessons.Any(l=>string.IsNullOrWhiteSpace(l.Title) || l.Sequence<1 || l.KCIds.Length==0))errors.Add("课时需要名称、正整数顺序和至少一个能力");
        if (c.Lessons.Any(l => l.KCIds.Any(id => !ids.Contains(id)))) errors.Add("课时引用了清单外 KC");
        if (c.Relations.Any(r => !ids.Contains(r.From) || !ids.Contains(r.To) || r.From == r.To)) errors.Add("关系端点非法");
        var visiting = new HashSet<Guid>(); var visited = new HashSet<Guid>();var path=new List<Guid>();Guid[]? cycle=null;
        bool Visit(Guid id)
        {
            if (visiting.Contains(id)){cycle=path.Skip(path.IndexOf(id)).Append(id).ToArray();return true;}
            if (visited.Contains(id)) return false;
            visiting.Add(id);path.Add(id);
            foreach (var r in c.Relations.Where(r => r.Type == "Prerequisite" && r.From == id)) if (Visit(r.To)) return true;
            visiting.Remove(id);path.RemoveAt(path.Count-1); visited.Add(id); return false;
        }
        if (ids.Any(Visit)) errors.Add("PREREQUISITE_CYCLE: "+string.Join(" → ",(cycle??[]).Select(id=>c.Kcs.FirstOrDefault(k=>k.Id==id)?.Name??id.ToString())));
        return errors.ToArray();
    }
    public static Catalog MultiplicationFixture()
    {
        string[] codes = ["MEANING", "MENTAL.TENS", "NOCARRY.2D1D", "CARRY.2D1D", "WRITTEN.3D1D", "ZERO", "ESTIMATE", "APPLY.ONE", "APPLY.MULTI"];
        string[] names = ["理解乘法意义", "整十整百口算", "两位数不进位乘法", "两位数进位乘法", "三位数笔算乘法", "含零乘法位值", "乘法估算", "一步乘法建模", "多条件应用建模"];
        var kcs = codes.Select((code,i) => new KC(Guid.NewGuid(), Guid.NewGuid(), $"MATH.G3.MULT.{code}", names[i], $"独立完成{names[i]}题目", "仅限本能力；不由整题结果推断其他步骤", i >= 7 ? "Application" : "Procedure")).ToArray();
        string[] stems = ["4 组小棒，每组 3 根。一共有几根？", "6 个盒子，每盒 5 支笔，共几支？", "20 × 4 = ?", "300 × 3 = ?", "23 × 3 = ?", "32 × 2 = ?", "27 × 3 = ?", "46 × 5 = ?", "123 × 3 = ?", "214 × 4 = ?", "205 × 3 = ?", "120 × 4 = ?", "估算 198 × 4，把 198 看作 200，约等于多少？", "估算 302 × 3，把 302 看作 300，约等于多少？", "每本书 12 元，买 4 本。只写乘法算式。", "每盒 24 支，买 3 盒。只写乘法算式。", "一张票 46 元，5 人共多少钱？写出列式与计算。", "两辆车各装 23 箱，每箱 4 个，写出计算步骤。", "38 × 6 = ?", "304 × 5 = ?"];
        string[] answers = ["12","30","80","900","69","64","81","230","369","856","615","480","800","900","12×4","24×3","230","184","228","1520"];
        int[] targets = [0,0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,3,5];
        var questions = stems.Select((stem,i) => new Question(Guid.NewGuid(),Guid.NewGuid(),stem,answers[i],$"参考结果：{answers[i]}。请家长结合竖式或列式讲解。",i >= 14 && i <= 17 ? "ShortAnswer" : "Numeric","Medium",i == 16 || i == 17 ? "ObservedSteps" : "SingleKC",i == 16 || i == 17 ? [new(kcs[8].Id,"Primary",.35m,"StepObserved","model1"),new(kcs[8].Id,"Primary",.35m,"StepObserved","model2"),new(kcs[3].Id,"Secondary",.25m,"StepObserved","calculate"),new(kcs[7].Id,"Context",0,"None")] : [new(kcs[targets[i]].Id)],Hint:"先说一说每个数表示什么，再按数位计算。")).ToArray();
        var resources = kcs.Select(k => new Resource(Guid.NewGuid(), $"{k.Name} · 纸上讲解", "使用自备纸笔，由家长示范一道题，再让孩子尝试", 5, [k.Id])).ToArray();
        var lessons = kcs.Select((k,i) => new Lesson(Guid.NewGuid(),k.Name,i+1,[k.Id])).ToArray();
        return new(kcs, questions, resources, lessons, [new(kcs[2].Id,kcs[3].Id),new(kcs[3].Id,kcs[4].Id)]);
    }
    public static Catalog Fixture()
    {
        string[] codes=["MULDIV_FIRST","ADD_SUB_ORDER","MULDIV_ORDER","PARENTHESES","APPLY_MULT_ADD","APPLY_MULT_SUB","APPLY_DIV_ADD","APPLY_DIV_SUB","MODEL_TWO_STEP"];
        string[] names=["先乘除后加减","加减同级从左到右","乘除同级从左到右","先算小括号","乘加应用","乘减应用","除加应用","除减应用","两步问题列式"];
        string[] boundaries=["不含小括号；使用表内乘除", "仅含加减同级运算", "仅含乘除同级运算", "一个小括号，不含嵌套", "给定乘加算式，不同时测阅读", "给定乘减算式，不同时测阅读", "给定除加算式，不同时测阅读", "给定除减算式，不同时测阅读", "只观察列式，不用最终计算推断建模"];
        var kcs=names.Select((n,i) => new KC(Guid.NewGuid(),Guid.NewGuid(),$"MATH.G3.MIXED.{codes[i]}",n,$"独立完成{n}任务，并说明先算哪一步",boundaries[i],i==8?"Application":"Procedure")).ToArray();
        string[] stems=["3 + 4 × 2 = ?", "18 − 12 ÷ 3 = ?", "24 − 8 + 6 = ?", "16 + 7 − 9 = ?", "24 ÷ 3 × 2 = ?", "4 × 6 ÷ 3 = ?", "(8 + 4) ÷ 3 = ?", "5 × (9 − 3) = ?", "3 × 6 + 5 = ?", "4 + 7 × 2 = ?", "30 − 4 × 5 = ?", "6 × 4 − 8 = ?", "18 ÷ 3 + 7 = ?", "8 + 24 ÷ 4 = ?", "25 − 18 ÷ 3 = ?", "32 ÷ 4 − 5 = ?", "有 3 盒铅笔，每盒 6 支，再添 5 支。只写一个综合算式。", "有 24 个苹果，平均装 4 袋，吃掉一袋里的 2 个。只写这一袋剩下数量的算式。", "7 + 5 × 3 = ?", "(18 − 6) ÷ 4 = ?"];
        string[] answers=["11","14","22","14","16","8","4","30","23","18","10","16","13","14","19","3","3×6+5","24÷4-2","22","3"];
        int[] targets=[0,0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,0,3];
        var qs=stems.Select((text,i) => new Question(Guid.NewGuid(),Guid.NewGuid(),text,answers[i],i>=16&&i<=17?$"参考列式：{answers[i]}。家长根据数量关系判分。":$"先确定运算顺序，再分步计算。结果为 {answers[i]}。",i>=16&&i<=17?"ShortAnswer":"Numeric","Medium","SingleKC",[new(kcs[targets[i]].Id)],Hint:i==6||i==7||i==19?"先算小括号里面。":"乘除优先；同级运算从左到右。")).ToArray();
        var resources=kcs.Select(k => new Resource(Guid.NewGuid(),$"{k.Name} · 纸笔讲解","使用自备纸笔，家长示范运算顺序，再尝试一道原创例题。",5,[k.Id],RevisionId:Guid.NewGuid())).ToArray();
        var lessons=new[] { new Lesson(Guid.NewGuid(),"小熊购物 · 乘加、乘减",1,[kcs[0].Id,kcs[4].Id,kcs[5].Id]),new Lesson(Guid.NewGuid(),"买文具 · 除加、除减",2,[kcs[0].Id,kcs[6].Id,kcs[7].Id]),new Lesson(Guid.NewGuid(),"过河 · 小括号与两步问题",3,[kcs[3].Id,kcs[8].Id]),new Lesson(Guid.NewGuid(),"单元整理 · 同级运算顺序",4,[kcs[1].Id,kcs[2].Id]) };
        var textbook=new Textbook(Guid.NewGuid(),Guid.NewGuid(),"北师大版","具体印次待核对","Math",3,"上册");
        var unit=new TextbookUnit(Guid.NewGuid(),Guid.NewGuid(),textbook.Id,"混合运算",1);
        lessons=lessons.Select(l=>l with {UnitId=unit.Id,RevisionId=Guid.NewGuid()}).ToArray();
        return new(kcs,qs,resources,lessons,[new(kcs[0].Id,kcs[3].Id),new(kcs[3].Id,kcs[8].Id)],[textbook],[unit],[]);
    }
}
