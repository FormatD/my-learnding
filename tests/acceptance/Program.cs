using Microsoft.Extensions.Configuration;
using Learning;

var tests=new List<(string,Action)>();
void Test(string name,Action action) => tests.Add((name,action));
void Eq<T>(T actual,T expected) { if (!Equals(actual,expected)) throw new Exception($"expected {expected}, actual {actual}"); }
void Near(decimal actual,decimal expected) { if (Math.Abs(actual-expected)>.00001m) throw new Exception($"expected {expected}, actual {actual}"); }
var family=Guid.NewGuid();var student=Guid.NewGuid();var generation=Guid.NewGuid();var kc=new KC(Guid.NewGuid(),Guid.NewGuid(),"TEST","测试","独立计算","仅计算");
var anchor=new DateTimeOffset(2026,1,1,8,0,0,TimeSpan.Zero);
AssessmentInput Input(int n,bool correct=true,int day=0,Guid? questionId=null,int hint=0,string difficulty="Medium",string type="Practice",int attemptNo=1,string? result=null)
{
    var q=new Question(questionId??Guid.NewGuid(),Guid.NewGuid(),"2×2","4","4","Numeric",difficulty,"SingleKC",[new(kc.Id)]);
    var session=new LearningSession { QuestionId=q.Id,ReleaseId=Guid.NewGuid(),StudentId=student };
    var a=new Attempt { StudentId=student,SessionId=session.Id,Number=attemptNo,Sequence=n,CreatedAt=anchor.AddDays(day).AddMinutes(n),HintLevel=hint };
    return new(a,session,q,new Grading { AttemptId=a.Id,Result=result??(correct?"Correct":"Incorrect") },[kc],new StudyTask { Type=type });
}
AssessmentOutput Replay(params AssessmentInput[] inputs)=>Assessment.Replay(family,student,generation,"Asia/Shanghai",inputs);
Test("M0 混合运算 20 题发布校验",()=>{var c=Content.Fixture();Eq(c.Questions.Length,20);Eq(Content.Validate(c).Length,0);});
Test("AT33 前置环拒绝",()=>{var c=Content.Fixture();var cycle=c with { Relations=[new(c.Kcs[0].Id,c.Kcs[1].Id),new(c.Kcs[1].Id,c.Kcs[0].Id)] };Eq(Content.Validate(cycle).Any(e=>e.Contains("CYCLE")),true);});
Test("AT03 重试不抵消首次负证据",()=>{var first=Input(1,false);var retry=Input(2) with { Session=first.Session,Question=first.Question };retry.Attempt.SessionId=first.Session.Id;retry.Attempt.Number=2;var o=Replay(first,retry);Eq(o.Evidence.Count,1);Eq(o.Masteries[0].Beta,3m);});
Test("AT04 Pending 不选后续正确",()=>{var first=Input(1,result:"Pending");var retry=Input(2,attemptNo:2);Eq(Replay(first,retry).Evidence.Count,0);});
Test("AT05 NoEvidence 不推断次能力",()=>{var i=Input(1,false);i=i with { Question=i.Question with { Policy="NoEvidence",Mappings=[new(kc.Id,"Primary",0,"None")] } };var o=Replay(i);Eq(o.Evidence.Count,0);Eq(o.Reviews.Count,1);});
Test("AT06 观察步骤不猜测未知结果",()=>{var c=Content.MultiplicationFixture();var q=c.Questions[16];var i=Input(1,false) with { Question=q,Kcs=c.Kcs,Grade=new Grading { Result="Partial",Steps=Json.Write(new[] { new ObservedStep("model1","Correct"),new ObservedStep("model2","Correct"),new ObservedStep("calculate","Incorrect") }) } };var o=Replay(i);Near(o.Evidence.Where(e=>e.Positive).Sum(e=>e.Weight),.7m);Near(o.Evidence.Where(e=>!e.Positive).Sum(e=>e.Weight),.25m);});
Test("AT07 前置不产生证据",()=>{var i=Input(1,false);i=i with { Question=i.Question with { Mappings=[new(kc.Id,"Prerequisite",0,"None")] } };Eq(Replay(i).Evidence.Count,0);});
Test("AT13 原题滚动 24h 限额",()=>{var id=Guid.NewGuid();var o=Replay(Input(1,questionId:id),Input(2,questionId:id));Eq(o.Evidence.Count,2);Eq(o.Evidence[1].Weight,0m);Eq(o.Masteries[0].DistinctQuestions,1);});
Test("原题复测折扣",()=>{var id=Guid.NewGuid();var o=Replay(Input(1,questionId:id),Input(2,day:3,questionId:id));Eq(o.Evidence[1].Weight,.5m);});
Test("AT14 同构每日正证据上限",()=>{var group=Guid.NewGuid();var items=Enumerable.Range(1,20).Select(n=>{var i=Input(n);return i with { Question=i.Question with { VariantGroupId=group } };}).ToArray();Near(Replay(items).Evidence.Sum(e=>e.Weight),2);});
Test("强提示正证据 0.4",()=>Eq(Replay(Input(1,hint:2)).Evidence[0].Weight,.4m));
Test("正负难度系数分离",()=>{Eq(Replay(Input(1,difficulty:"Hard")).Evidence[0].Weight,1.2m);Eq(Replay(Input(1,false,difficulty:"Easy")).Evidence[0].Weight,1.2m);});
Test("4 次标准正确可 CanDo",()=>{var o=Replay(Enumerable.Range(1,4).Select(n=>Input(n)).ToArray());Eq(o.Masteries[0].Probability,.75m);Eq(o.Masteries[0].Status,"CanDo");});
Test("AT16 8 题不是 Mastered",()=>{var o=Replay(Enumerable.Range(1,8).Select(n=>Input(n)).ToArray());Near(o.Masteries[0].Probability,10m/12);Eq(o.Masteries[0].Status,"CanDo");});
Test("AT17 10 题无延迟复测不能掌握",()=>Eq(Replay(Enumerable.Range(1,10).Select(n=>Input(n)).ToArray()).Masteries[0].Status,"CanDo"));
Test("AT18 掌握后独立错误先复核",()=>{var items=Enumerable.Range(1,10).Select(n=>Input(n)).Append(Input(11,day:8,type:"Review")).Append(Input(12,false,day:9)).ToArray();var m=Replay(items).Masteries[0];Eq(m.Status,"Mastered");Eq(m.NeedsRecheck,true);});
Test("AT20 D0 D2 D7 D30",()=>{var id=Guid.NewGuid();var o=Replay(Input(1,false,questionId:id),Input(2,day:2,questionId:id,type:"Review"),Input(3,day:7,questionId:id,type:"Review"),Input(4,day:30,questionId:id,type:"Review"));Eq(o.Reviews.Single(r=>r.TargetType=="WrongQuestion").Status,"Completed");Eq(o.Reviews.Single(r=>r.TargetType=="KC").DueDate,new DateOnly(2026,3,2));});
Test("AT21 迟到复习从实际通过日 +5",()=>{var id=Guid.NewGuid();var o=Replay(Input(1,false,questionId:id),Input(2,day:5,questionId:id,type:"Review"));Eq(o.Reviews.Single(r=>r.TargetType=="WrongQuestion").DueDate,new DateOnly(2026,1,11));});
Test("AT22 提示正确不推进复习阶段",()=>{var id=Guid.NewGuid();var o=Replay(Input(1,false,questionId:id),Input(2,day:2,questionId:id,hint:1,type:"Review"));Eq(o.Reviews.Single(r=>r.TargetType=="WrongQuestion").Stage,"R1");});
Test("AT24 必做超载不加入可选",()=>{var o=Planning.Fit([new("fixed","作业","Resource",50,true,0,"PARENT_LOCKED","必做"),new("optional","阅读","Resource",5,false,15,"LONG_TERM_GOAL","阅读")],30,0,0);Eq(o.Overflow,20);Eq(o.Selected.Length,1);});
Test("AT27 同分排序稳定",()=>{PlanOption[] options=[new("b","B","Practice",5,false,18,"LOW_EVIDENCE",""),new("a","A","Practice",5,false,18,"LOW_EVIDENCE","")];Eq(Planning.Fit(options,5,0,0).Selected[0].Key,"a");Eq(Planning.Fit(options.Reverse(),5,0,0).Selected[0].Key,"a");});
Test("AT19 固定事件顺序重放一致",()=>{var items=Enumerable.Range(1,12).Select(n=>Input(n,n%3!=0,day:n)).ToArray();var a=Replay(items);var b=Replay(items.Reverse().ToArray());Eq(Json.Write(a.Masteries.Select(m=>new {m.Alpha,m.Beta,m.Status,m.NeedsRecheck})),Json.Write(b.Masteries.Select(m=>new {m.Alpha,m.Beta,m.Status,m.NeedsRecheck})));});
Test("AT32 禁止跨嵌入空间或维度检索",()=>{try{Retrieval.Similarity(new double[128],"model-a",new double[128],"model-b");throw new Exception("accepted incompatible spaces");}catch(ApiError e){Eq(e.Code,"EMBEDDING_SPACE_CONFLICT");}});
Test("相关讲解重置实际保持间隔",()=>{var first=Input(1);var review=Input(2,day:8,type:"Review");var output=Assessment.Replay(family,student,generation,"Asia/Shanghai",[first,review],[new TeachingAnchor(kc.Id,anchor.AddDays(7))]);Eq(output.Evidence[1].Weight,1m);});
Test("同一遇题的两步错误只算一次独立失败",()=>{var prior=Enumerable.Range(1,10).Select(n=>Input(n)).Append(Input(11,day:8,type:"Review")).ToList();var wrong=Input(12,false,day:9);wrong=wrong with {Question=wrong.Question with {Policy="ObservedSteps",Mappings=[new(kc.Id,"Primary",.5m,"StepObserved","s1"),new(kc.Id,"Primary",.5m,"StepObserved","s2")]},Grade=new Grading {Result="Incorrect",Steps=Json.Write(new[] {new ObservedStep("s1","Incorrect"),new ObservedStep("s2","Incorrect")})}};prior.Add(wrong);var m=Replay(prior.ToArray()).Masteries.Single();Eq(m.Status,"Mastered");Eq(m.NeedsRecheck,true);});
Test("AT15 更正释放同构正证据限额后重算下游",()=>{var group=Guid.NewGuid();var inputs=Enumerable.Range(1,4).Select(n=>{var i=Input(n);return i with {Question=i.Question with {VariantGroupId=group}};}).ToArray();var old=Replay(inputs);Near(old.Evidence[2].Weight,.2m);Near(old.Evidence[3].Weight,0m);inputs[0]=inputs[0] with {Grade=new Grading {Result="Incorrect"}};var corrected=Replay(inputs);Near(corrected.Evidence[2].Weight,.8m);Near(corrected.Evidence[3].Weight,.4m);Near(corrected.Evidence.Where(e=>e.Positive).Sum(e=>e.Weight),2m);Near(corrected.Masteries.Single().Beta,3m);});
Test("AT23 相同事件重放日程不推进两次",()=>{var id=Guid.NewGuid();var inputs=new[]{Input(1,false,questionId:id),Input(2,day:2,questionId:id,type:"Review")};var a=Replay(inputs);var b=Replay(inputs);Eq(a.Reviews.Single().Stage,"R2");Eq(b.Reviews.Single().Stage,"R2");Eq(a.Reviews.Single().DueDate,b.Reviews.Single().DueDate);Eq(a.Masteries.Single().Alpha,b.Masteries.Single().Alpha);});
Test("AT34 新拆分能力不继承旧概率",()=>{var original=Input(1);var split=new KC(Guid.NewGuid(),Guid.NewGuid(),"SPLIT","拆分能力","独立回答","新的行为边界");var o=Replay(original with {Kcs=[kc,split]});Eq(o.Masteries.Any(m=>m.KCId==split.Id),false);Eq(o.Evidence.Any(e=>e.KCId==split.Id),false);});
Test("私有导出清理仅删除过期成品，并按家庭隔离",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"learning-export-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
    try{
        var cfg=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ExportDirectory",dir}}).Build();
        var a=Guid.NewGuid();var b=Guid.NewGuid();var expired=ExportCleanup.NewPath(cfg,a);File.WriteAllText(expired,"old");ExportCleanup.Built(expired);File.SetLastWriteTimeUtc(expired,DateTime.UtcNow.AddMinutes(-20));
        var building=ExportCleanup.NewPath(cfg,a);File.WriteAllText(building,"active");File.SetLastWriteTimeUtc(building,DateTime.UtcNow.AddMinutes(-20));
        var fresh=ExportCleanup.NewPath(cfg,b);File.WriteAllText(fresh,"fresh");ExportCleanup.Built(fresh);var unrelated=Path.Combine(dir,"notes.zip");File.WriteAllText(unrelated,"notes");File.SetLastWriteTimeUtc(unrelated,DateTime.UtcNow.AddMinutes(-20));
        Eq(ExportCleanup.Sweep(dir,DateTime.UtcNow,TimeSpan.FromMinutes(15)),1);Eq(File.Exists(building),true);Eq(File.Exists(fresh),true);Eq(File.Exists(unrelated),true);
        ExportCleanup.RemoveFamily(dir,a);Eq(File.Exists(building),false);Eq(File.Exists(fresh),true);Eq(File.Exists(unrelated),true);
    }finally{Directory.Delete(dir,true);}
});
var failed=0;
foreach (var (name,action) in tests) { try { action();Console.WriteLine($"PASS {name}"); } catch(Exception ex) { failed++;Console.WriteLine($"FAIL {name}: {ex.Message}"); } }
Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed");return failed>0?1:0;
