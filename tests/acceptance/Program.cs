using Microsoft.Extensions.Configuration;
using Learning;

if(args.Length==2 && args[0]=="--export-unit-pack"){File.WriteAllText(args[1],Json.Write(MixedOperationsPack.Create()));return 0;}
if(args.Length==2 && args[0]=="--export-flow-fixture"){File.WriteAllText(args[1],Json.Write(Content.Fixture()));return 0;}
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
Test("原创题包身份稳定、目录合法、变式分组与人工步骤映射",()=>{
    var c=MixedOperationsPack.Create();Eq(Json.Write(c),Json.Write(MixedOperationsPack.Create()));Eq(Content.Validate(c).Length,0);Eq(c.Questions.Length,160);Eq(c.Resources.Length,15);
    Eq(c.Questions.Count(q=>q.Type=="Numeric"),128);Eq(c.Questions.Count(q=>q.Policy=="ObservedSteps"),16);
    Eq(c.Questions.All(q=>q.VariantGroupId!=null),true);Eq(c.Questions.Where(q=>q.Policy=="ObservedSteps").All(q=>q.Mappings.Select(m=>m.Step).SequenceEqual(new[]{"model","calculate"})),true);
    Eq(c.Questions.Where(q=>q.Policy=="SingleKC").All(q=>q.Mappings.Length==1),true);Eq(c.Resources.All(r=>r.PaperReference.Contains("独立练") && r.PaperReference.Contains("核对")),true);
});
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
Test("目录和目标范围不改变能力身份，旧目标范围摘要兼容",()=>{
    var c=Content.Fixture();var unit=c.Units!.Single();Eq(c.Lessons.All(l=>l.UnitId==unit.Id && l.RevisionId!=null),true);Eq(c.Textbooks!.Single().Edition,"具体印次待核对");
    var duplicate=c with {Units=[unit with {RevisionId=c.Kcs[0].RevisionId}]};Eq(Content.Validate(duplicate).Any(e=>e.Contains("修订身份必须")),true);
    var goal=new Goal{KCId=c.Kcs[0].Id,Subject="Math",GoalType="Practice"};Eq(Goals.Snapshot(goal).Scope,Content.Hash(Json.Write(new{goal.Subject,goal.GoalType,goal.KCId})));
    var before=Goals.Snapshot(goal).Scope;goal.UnitId=unit.Id;Eq(before==Goals.Snapshot(goal).Scope,false);Eq(Goals.ScopeKCs(goal,c).Single(),goal.KCId!.Value);
    goal.KCId=null;Eq(Goals.ScopeKCs(goal,c).Length,c.Kcs.Length);goal.UnitId=Guid.NewGuid();Eq(Goals.ScopeKCs(goal,c).Length,0);
});
Test("预算可执行率没有任务保持未知，耗时未知和超时不算预计内完成",()=>{
    Eq(BudgetReporting.Calculate([],[]).Rate,(decimal?)null);
    var unknown=new StudyTask{Status="Completed",ActualMinutes=null,Minutes=5};var over=new StudyTask{Status="Completed",ActualMinutes=6,Minutes=5};var skipped=new StudyTask{Status="Skipped",Minutes=5};
    var result=BudgetReporting.Calculate([unknown,over,skipped],[]);Eq(result.PublishedTasks,3);Eq(result.EligibleTasks,0);Eq(result.UnknownCompletedDuration,1);Eq(result.Rate,(decimal?)0);
});
Test("预算可执行率固定身份去重，完成与家长顺延重叠只计一次",()=>{
    var task=new StudyTask{Status="Completed",Minutes=5,ActualMinutes=5};var detail=new PlanAdjustmentDetails(Guid.NewGuid(),Guid.NewGuid(),"真实家长顺延依据",[task.Id],[],[task.Id],[],[],"TaskDeferral",new DateOnly(2026,10,3));
    var adjustment=new WeeklyAdjustment(Guid.NewGuid(),DateTimeOffset.UtcNow,true,detail);var result=BudgetReporting.Calculate([task,task],[adjustment,adjustment]);Eq(result.PublishedTasks,1);Eq(result.CompletedWithinEstimate,1);Eq(result.ConfirmedDeferredTasks,1);Eq(result.EligibleTasks,1);Eq(result.Rate,(decimal?)1);
});
Test("预算执行不从草稿移除或普通延期状态猜测家长确认",()=>{
    var task=new StudyTask{Status="Deferred",Minutes=5};var detail=new PlanAdjustmentDetails(Guid.NewGuid(),Guid.NewGuid(),"草稿调整",[task.Id],[],[task.Id],[],[],"TaskDeferral",new DateOnly(2026,10,3));
    var draft=new WeeklyAdjustment(Guid.NewGuid(),DateTimeOffset.UtcNow,false,detail);var ordinary=draft with{Published=true,Details=detail with{Kind="DraftAdjustment"}};var result=BudgetReporting.Calculate([task],[draft,ordinary]);Eq(result.EligibleTasks,0);Eq(result.ConfirmedDeferredTasks,0);
});
ReviewPassSummary ReviewReport(DateOnly start,DateOnly end,params AssessmentInput[] inputs)
{
    foreach(var i in inputs)i.Session.TaskId=i.Task.Id;
    return ReviewReporting.Calculate(inputs.Select(i=>i.Attempt),inputs.Select(i=>i.Session).DistinctBy(s=>s.Id),inputs.Select(i=>i.Task).DistinctBy(t=>t.Id),inputs.Select(i=>i.Grade),"Asia/Shanghai",start,end);
}
Test("周报复习分母只含首次已判分复习，提示与待判分不补通过",()=>{
    var start=new DateOnly(2026,1,1);var end=start.AddDays(6);
    var items=new[]{Input(1,type:"Review"),Input(2,false,type:"Review"),Input(3,hint:1,type:"Review"),Input(4,type:"Review",result:"Pending"),Input(5,type:"Review",result:"Partial"),Input(6),Input(7,type:"Review",attemptNo:2)};
    var r=ReviewReport(start,end,items);Eq(r.Encounters,5);Eq(r.GradedEncounters,4);Eq(r.IndependentPasses,1);Eq(r.PendingEncounters,1);Eq(r.Rate,.25m);
    items[0].Attempt.AnswerShown=true;Eq(ReviewReport(start,end,items).IndependentPasses,0);
});
Test("周报复习重复原题遵守历史24小时窗，观察点提示不算独立",()=>{
    var id=Guid.NewGuid();var original=Input(1,day:0,questionId:id);var repeated=Input(2,day:0,questionId:id,type:"Review");var later=Input(3,day:2,questionId:id,type:"Review");
    var r=ReviewReport(new(2026,1,1),new(2026,1,7),later,repeated,original);Eq(r.GradedEncounters,2);Eq(r.IndependentPasses,1);Eq(r.Items[0].Reason,"ROLLING_24H");
    later.Grade.Steps=Json.Write(new[]{new ObservedStep("calculate","Correct",1)});Eq(ReviewReport(new(2026,1,1),new(2026,1,7),original,later).IndependentPasses,0);
});
Test("周报复习采用最新判分、实际作答日期与学生时区，空分母未知",()=>{
    var i=Input(1,type:"Review");i.Session.TaskId=i.Task.Id;i.Attempt.CreatedAt=new DateTimeOffset(2026,1,7,16,1,0,TimeSpan.Zero);
    var corrected=new Grading{AttemptId=i.Attempt.Id,Number=2,Result="Incorrect"};i.Grade.Number=1;
    ReviewPassSummary Read(DateOnly day,string zone)=>ReviewReporting.Calculate([i.Attempt],[i.Session],[i.Task],[corrected,i.Grade],zone,day,day);
    Eq(Read(new(2026,1,7),"Asia/Shanghai").Rate,null);Eq(Read(new(2026,1,8),"Asia/Shanghai").GradedEncounters,1);Eq(Read(new(2026,1,8),"Asia/Shanghai").IndependentPasses,0);Eq(Read(new(2026,1,7),"UTC").Encounters,1);
});
ReviewCoverageSummary Coverage(DateOnly start,DateOnly end,params AssessmentInput[] inputs)
{
    var result=Assessment.Replay(family,student,generation,"Asia/Shanghai",inputs,collectReviewHistory:true);
    return ReviewCoverageReporting.Calculate(result.ReviewHistory,"Asia/Shanghai",start,end);
}
Test("复习覆盖保留未做到期和期初逾期，不把今日待办当历史",()=>{
    var i=Input(1,false);var day=new DateOnly(2026,1,1);
    Eq(Coverage(day,day.AddDays(1),i).Rate,null);var due=Coverage(day,day.AddDays(6),i);Eq(due.DueSchedules,1);Eq(due.ExecutedSchedules,0);Eq(due.UnexecutedSchedules,1);
    var overdue=Coverage(day.AddDays(7),day.AddDays(13),i);Eq(overdue.OverdueAtStart,1);Eq(overdue.Rate,0m);
});
Test("复习覆盖按阶段到期保留，晚作答不虚增往周执行",()=>{
    var id=Guid.NewGuid();var items=new[]{Input(1,false,questionId:id),Input(2,day:2,questionId:id,type:"Review"),Input(3,day:7,questionId:id,type:"Review"),Input(4,day:30,questionId:id,type:"Review")};
    var r=Coverage(new(2026,1,1),new(2026,1,8),items);Eq(r.DueSchedules,2);Eq(r.ExecutedSchedules,2);Eq(r.Rate,1m);
    var late=new[]{Input(1,false,questionId:id),Input(2,day:9,questionId:id,type:"Review")};var past=Coverage(new(2026,1,1),new(2026,1,7),late);Eq(past.DueSchedules,1);Eq(past.ExecutedSchedules,0);Eq(past.Items[0].Schedule.ExecutedAttemptId,late[1].Attempt.Id);
    Eq(Coverage(new(2026,1,8),new(2026,1,14),late).ExecutedSchedules,1);
});
Test("覆盖执行不要求通过或判分，到期前重置不制造未做日程",()=>{
    var id=Guid.NewGuid();var original=Input(1,false,questionId:id);var pending=Input(2,day:2,questionId:id,type:"Review",result:"Pending");
    Eq(Coverage(new(2026,1,1),new(2026,1,7),original,pending).ExecutedSchedules,1);
    var hinted=Input(2,day:2,questionId:id,type:"Review",hint:1);var h=Coverage(new(2026,1,1),new(2026,1,7),original,hinted);Eq(h.DueSchedules,2);Eq(h.ExecutedSchedules,1);
    var early=Input(2,false,day:1,questionId:id);var reset=Coverage(new(2026,1,1),new(2026,1,7),original,early);Eq(reset.DueSchedules,1);Eq(reset.Items[0].Schedule.DueDate,new DateOnly(2026,1,4));
    var ordered=Assessment.Replay(family,student,generation,"Asia/Shanghai",[original,hinted],collectReviewHistory:true);var reversed=Assessment.Replay(family,student,generation,"Asia/Shanghai",[hinted,original],collectReviewHistory:true);Eq(Json.Write(ordered.ReviewHistory),Json.Write(reversed.ReviewHistory));
});
Test("覆盖保留能力保持日程，实际目标匹配才执行，并按学生时区分期",()=>{
    var items=Enumerable.Range(1,10).Select(n=>Input(n)).ToList();var review=Input(11,day:8,type:"Review");review.Task.ReviewTargetId=kc.Id;items.Add(review);
    var r=Coverage(new(2026,1,1),new(2026,1,14),items.ToArray());Eq(r.DueSchedules,1);Eq(r.ExecutedSchedules,1);Eq(r.Items[0].Schedule.TargetType,"KC");
    review.Task.ReviewTargetId=Guid.NewGuid();Eq(Coverage(new(2026,1,1),new(2026,1,14),items.ToArray()).ExecutedSchedules,0);
    var wrong=Input(1,false);wrong.Attempt.CreatedAt=new(2026,1,1,23,50,0,TimeSpan.Zero);Eq(Coverage(new(2026,1,1),new(2026,1,3),wrong).DueSchedules,0);Eq(Coverage(new(2026,1,1),new(2026,1,4),wrong).DueSchedules,1);
});
MasteryChangeSummary MasteryWeek(DateOnly start,DateOnly end,params AssessmentInput[] inputs)=>MasteryReporting.Calculate(Assessment.Replay(family,student,generation,"Asia/Shanghai",inputs,collectMasteryHistory:true),"Asia/Shanghai",start,end);
Test("掌握周报保留未知期初、每次状态晋级与覆盖，不只比较百分比",()=>{
    var items=Enumerable.Range(1,10).Select(n=>Input(n)).Append(Input(11,day:8,type:"Review")).Append(Input(12,false,day:9)).ToArray();
    var r=MasteryWeek(new(2026,1,8),new(2026,1,14),items);var m=r.Items.Single();Eq(m.Before!.Status,"CanDo");Eq(m.After.Status,"Mastered");Eq(m.After.NeedsRecheck,true);Eq(m.StatusChanges,1);Eq(m.RecheckChanges,1);Eq(m.CoverageChanges,1);Eq(m.After.Delay7,true);Eq(m.EvidenceParts,2);Eq(m.Encounters,2);
    var cleared=MasteryWeek(new(2026,1,8),new(2026,1,14),items.Append(Input(13,day:10)).Append(Input(14,day:11)).ToArray()).Items.Single();Eq(cleared.RecheckChanges,2);Eq(cleared.After.NeedsRecheck,false);
    var first=MasteryWeek(new(2026,1,1),new(2026,1,7),items);Eq(first.Items[0].Before,null);Eq(first.Items[0].After.Status,"CanDo");Eq(first.StatusChanges,2);
});
Test("掌握周报分步证据不冒充独立遇题，零权重和待判分保留缺口",()=>{
    var id=Guid.NewGuid();var original=Input(1,questionId:id);var duplicate=Input(2,questionId:id);var pending=Input(3,result:"Pending");
    var r=MasteryWeek(new(2026,1,1),new(2026,1,7),original,duplicate,pending);Eq(r.EvidenceParts,2);Eq(r.Encounters,2);Eq(r.SuppressedParts,1);Eq(r.Items[0].After.DistinctQuestions,1);
    var partial=Input(1) with {Question=original.Question with {Policy="ObservedSteps",Mappings=[new(kc.Id,"Primary",.5m,"StepObserved","s1"),new(kc.Id,"Primary",.5m,"StepObserved","s2")]},Grade=new Grading{Result="Partial",Steps=Json.Write(new[]{new ObservedStep("s1","Correct"),new ObservedStep("s2","Incorrect")})}};
    var observed=MasteryWeek(new(2026,1,1),new(2026,1,7),partial);Eq(observed.EvidenceParts,2);Eq(observed.Encounters,1);Eq(observed.Items[0].NegativeWeight,.5m);
});
Test("掌握周报更正按新结果重算，未来结果不越过期末，新能力不继承",()=>{
    var first=Input(1);var later=Input(2,false,day:8);var r=MasteryWeek(new(2026,1,1),new(2026,1,7),first,later);Eq(r.EvidenceParts,1);Eq(r.Items.Single().NegativeWeight,0m);
    first.Grade.Result="Incorrect";Eq(MasteryWeek(new(2026,1,1),new(2026,1,7),first,later).Items.Single().NegativeWeight,1m);
    var other=new KC(Guid.NewGuid(),Guid.NewGuid(),"NEW","新能力","独立","新测量");var fresh=Input(3,day:9);fresh=fresh with {Kcs=[other],Question=fresh.Question with {Mappings=[new(other.Id)]}};
    var split=MasteryWeek(new(2026,1,8),new(2026,1,14),first,later,fresh).Items.Single(m=>m.KCId==other.Id);Eq(split.Before,null);Eq(split.After.EffectiveEvidence,1m);Eq(split.After.Status,"Learning");
    Eq(MasteryWeek(new(2025,12,1),new(2025,12,7),first,later).Items.Length,0);
});
Test("交接JSON夹具内容合法且重放、更正、并发提交后序列满足独立预期",FixtureCases.Verify);
Test("资源旧快照未记录修订保持合法，新样例具有独立修订",()=>{
    var c=Content.Fixture();Eq(c.Resources.All(r=>r.RevisionId!=null && r.RevisionId!=Guid.Empty),true);
    var old=c with {Resources=c.Resources.Select(r=>r with {RevisionId=null}).ToArray()};Eq(Content.Validate(old).Length,0);
    Eq(Json.Read<Resource>("{\"id\":\"00000000-0000-0000-0000-000000000001\",\"title\":\"旧资源\",\"paperReference\":\"纸笔\",\"minutes\":5,\"kcIds\":[]}").RevisionId,null);
    Eq(Content.Validate(c with {Resources=[c.Resources[0],c.Resources[0]]}).Any(e=>e.Contains("身份不能重复")),true);
    Eq(Content.Validate(c with {Resources=[c.Resources[0] with {RevisionId=Guid.Empty}]}).Any(e=>e.Contains("身份无效")),true);
    Eq(Content.Validate(c with {Resources=[c.Resources[0] with {RevisionId=c.Questions[0].RevisionId}]}).Any(e=>e.Contains("其他内容修订冲突")),true);
    Eq(Content.Validate(c with {Resources=[c.Resources[0] with {Id=c.Kcs[0].Id}]}).Any(e=>e.Contains("其他内容身份冲突")),true);
});
Test("映射建议校验分开教学覆盖与证据预算，限制原版本与来源",MappingSuggestionCases.Policy);
Test("模拟分步建议保留独立观察点，不推断额外能力或虚构题型",MappingSuggestionCases.ObservedSteps);
Test("接受映射创建新对象修订并固定能力库，不覆盖输入快照",MappingSuggestionCases.FrozenApply);
Test("评估上下文包含未准入与重试，证据固定同一上下文与实际更正版本",()=>{
    var first=Input(1,result:"Pending");var retry=Input(2,attemptNo:2);var no=Input(3) with{Question=Input(3).Question with{Policy="NoEvidence"}};
    var o=Replay(first,retry,no);Eq(o.Contexts.Count,3);Eq(o.Contexts[0].AdmissionStatus,"Pending");Eq(o.Contexts[1].AdmissionStatus,"RetryExcluded");Eq(o.Contexts[2].AdmissionStatus,"NoEvidence");Eq(o.Evidence.Count,0);
    var c=Content.MultiplicationFixture();var q=c.Questions[16];var mapping=Guid.NewGuid();var correction=Guid.NewGuid();var release=Guid.NewGuid();
    var input=Input(4) with{Question=q,Kcs=c.Kcs,MappingSetRevisionId=mapping,MappingReleaseId=release,CorrectionBatchId=correction,Grade=new Grading{Result="Partial",Steps=Json.Write(new[]{new ObservedStep("model1","Correct"),new ObservedStep("model2","Incorrect")})}};
    o=Replay(input);var context=o.Contexts.Single();Eq(context.MappingSource,"FixedContainer");Eq(context.MappingSetRevisionId,mapping);Eq(context.QuestionRevisionId,q.RevisionId);Eq(context.MappingReleaseId,release);Eq(context.CorrectionBatchId,correction);Eq(context.GradingRevisionId,input.Grade.Id);Eq(context.EvidenceRuleVersion,Assessment.EvidenceRuleVersion);Eq(o.Evidence.Count,2);Eq(o.Evidence.All(e=>e.ContextId==context.Id && e.MappingSetRevisionId==mapping && e.GradingId==context.GradingRevisionId),true);
    Eq(Replay(first).Contexts.Single().MappingSource,"LegacySnapshot");
});
Test("Builder 严格结构、真实引文与未知字段拒绝",BuilderProtocolCases.Validation);
Test("Builder 一次受控修复、超时取消与输入上限",BuilderProtocolCases.Control);
var failed=0;
foreach (var (name,action) in tests) { try { action();Console.WriteLine($"PASS {name}"); } catch(Exception ex) { failed++;Console.WriteLine($"FAIL {name}: {ex.Message}"); } }
Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed");return failed>0?1:0;
