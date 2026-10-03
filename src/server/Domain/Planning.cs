using Microsoft.EntityFrameworkCore;

namespace Learning;
public record PlanOption(string Key, string Title, string Type, int Minutes, bool Mandatory, int Score, string ReasonCode, string Reason, Guid? KCId=null, Guid? QuestionId=null, Guid? ReviewTargetId=null, string ResourceRef="", DateOnly? DueDate=null, int SchoolSequence=0,string? ResourceUrl=null);
public record PlanSelection(PlanOption[] Selected, object[] Rejected, int Overflow);
public static class Planning
{
    public const string RuleVersion="plan/3";
    public static int Charge(StudyTask t) => t.Status=="Completed" ? t.ActualMinutes??t.Minutes : t.Status=="InProgress" ? Math.Max(t.Minutes,t.ActualMinutes??0) : t.Minutes;
    public static PlanSelection Fit(IEnumerable<PlanOption> options, int budget, int fixedCharge, int fixedCount)
    {
        var all=options.GroupBy(c => c.Key).Select(g => g.OrderByDescending(x => x.Score).First()).ToArray();
        var selected=all.Where(c => c.Mandatory).OrderBy(c => c.Key).ToList();
        var required=fixedCharge+selected.Sum(c => c.Minutes);
        var remaining=Math.Max(0,budget-required); var rejected=new List<object>();
        foreach (var c in all.Where(c => !c.Mandatory).OrderByDescending(c => c.Score).ThenBy(c => c.DueDate??DateOnly.MaxValue).ThenBy(c => c.SchoolSequence).ThenBy(c => c.Key,StringComparer.Ordinal))
        {
            var why=required>budget ? "MANDATORY_OVERFLOW" : c.Minutes>remaining ? "BUDGET" : selected.Count+fixedCount>=6 ? "TASK_CAP" : c.KCId.HasValue && selected.Count(x => x.KCId==c.KCId)>=2 ? "KC_CAP" : null;
            if (why!=null) rejected.Add(new { candidate=c,reason=why }); else { selected.Add(c); remaining-=c.Minutes; }
        }
        return new(selected.ToArray(),rejected.ToArray(),Math.Max(0,required-budget));
    }
    public static async Task<PlanRevision> Generate(Database db, Student s, DateOnly date)
    {
        if (s.ActiveReleaseId==null) throw new ApiError(422,"NO_CONTENT","请先审核发布内容，并绑定学生。");
        var release=await db.Releases.SingleAsync(r => r.Id==s.ActiveReleaseId && r.FamilyId==s.FamilyId && !r.Withdrawn);
        var content=Json.Read<Catalog>(release.Payload);
        var projection=await PlanProjection.Capture(db,s);
        var plan=await db.Plans.SingleOrDefaultAsync(p => p.StudentId==s.Id && p.Date==date);
        if (plan==null) { plan=new Plan { FamilyId=s.FamilyId,StudentId=s.Id,Date=date }; db.Plans.Add(plan); }
        var revisions=await db.PlanRevisions.Where(r => r.PlanId==plan.Id).OrderBy(r => r.Number).ToListAsync();
        var old=plan.ActiveRevisionId==null ? [] : await db.Placements.Where(p => p.RevisionId==plan.ActiveRevisionId).OrderBy(p => p.Sequence).ToListAsync();
        var taskIds=old.Select(p => p.TaskId).ToArray();
        var fixedTasks=await db.Tasks.Where(t => taskIds.Contains(t.Id) && (t.Locked || t.Mandatory || t.Status=="Completed" || t.Status=="InProgress")).OrderBy(t=>t.Id).ToListAsync();
        var retiredTasks=await db.Tasks.Where(t=>taskIds.Contains(t.Id) && (t.Status=="Skipped" || t.Status=="Abandoned")).OrderBy(t=>t.Id).Select(t=>new{t.Id,t.Status}).ToArrayAsync();
        foreach(var task in fixedTasks.Where(t=>t.Status=="InProgress" && t.StartedAt!=null))task.ActualMinutes=Math.Max(1,(int)Math.Ceiling((task.TrackedSeconds+(DateTimeOffset.UtcNow-task.StartedAt!.Value).TotalSeconds)/60d));
        var availability=await db.Availabilities.SingleOrDefaultAsync(a => a.StudentId==s.Id && a.Date==date);
        var budget=availability?.Minutes??s.DailyMinutes; var reserved=availability?.Reserved??0;
        if (reserved>0 && fixedTasks.Any(t => t.Type=="Schoolwork")) throw new ApiError(422,"SCHOOLWORK_DOUBLE_COUNT","学校作业已是任务，请将预留作业时间设为 0。");
        var progress=await db.Progresses.Where(p => p.StudentId==s.Id && p.Status=="Confirmed" && p.Date<=date && p.Date>=date.AddDays(-7)).ToListAsync();
        var allGoals=await db.Goals.Where(g => g.StudentId==s.Id && g.Active).ToListAsync();
        var quotaStart=allGoals.Any(g=>g.Period=="Weekly")?date.AddDays(-((int)date.DayOfWeek+6)%7):date;
        var zone=TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone);
        var quotaFrom=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(quotaStart.ToDateTime(TimeOnly.MinValue),zone));
        var quotaUntil=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue),zone));
        var completedGoalTasks=await db.Tasks.Where(t=>t.StudentId==s.Id && t.Status=="Completed" && t.CompletedAt>=quotaFrom && t.CompletedAt<quotaUntil && t.GoalSnapshots!="[]").ToListAsync();
        var quotaTaskIds=completedGoalTasks.Select(t=>t.Id).ToArray();
        var answeredTaskIds=await (from attempt in db.Attempts join session in db.Sessions on attempt.SessionId equals session.Id where attempt.StudentId==s.Id && attempt.Number==1 && quotaTaskIds.Contains(session.TaskId) select session.TaskId).Distinct().ToArrayAsync();
        var goals=allGoals.Where(g=>Goals.Scheduled(g,date) && Goals.Completed(g,date,s.TimeZone,completedGoalTasks,answeredTaskIds)<g.TargetValue && Goals.Completed(g,date,s.TimeZone,completedGoalTasks,answeredTaskIds,true)==0).ToArray();
        var mastery=projection.Conservative?[]:await db.Masteries.Where(m => m.StudentId==s.Id && m.GenerationId==s.ActiveGenerationId).ToListAsync();
        var reviews=projection.Conservative?[]:await db.Reviews.Where(r => r.StudentId==s.Id && r.GenerationId==s.ActiveGenerationId && r.Status=="Pending" && r.DueDate<=date).ToListAsync();
        var history=await (from a in db.Attempts join session in db.Sessions on a.SessionId equals session.Id where a.StudentId==s.Id && a.Number==1 select new {session.QuestionId,a.CreatedAt}).ToListAsync();
        var evidence=projection.Conservative?[]:await db.Evidence.Where(e=>e.StudentId==s.Id && e.GenerationId==s.ActiveGenerationId && e.Weight>0).OrderBy(e=>e.OccurredAt).ToListAsync();
        var options=new List<PlanOption>(); var warnings=new List<string>();
        if(projection.Conservative)warnings.Add($"PROJECTION_LAG: {projection.Pending.Length} 个学习结果尚未同步完成，本草稿保留已安排任务，只依据学校进度和家长目标补充练习；暂停按旧掌握状态推荐复习或弱项任务。请等待结果同步，或核对失败任务后重新生成。");
        else if(projection.Pending.Length>0)warnings.Add($"PROJECTION_PENDING: {projection.Pending.Length} 个结果尚未处理，可等待后台追平后重新生成");
        var goalOptions=new List<(Goal Goal,string Key)>();
        foreach(var goal in goals)
        {
            Question? q=null;
            if(goal.GoalType=="Practice")
            {
                var scopedKCIds=Goals.ScopeKCs(goal,content);
                q=content.Questions.Where(q=>q.Policy=="SingleKC" && q.Mappings.Any(m=>scopedKCIds.Contains(m.KCId) && m.Mode=="WholeItem")).OrderBy(q=>history.Count(h=>h.QuestionId==q.Id)).ThenBy(q=>q.Id).FirstOrDefault();
                if(q==null){warnings.Add($"MISSING_CONTENT: 目标 {goal.Title} 缺少可测正式题目");continue;}
            }
            var key=q==null?$"goal:{goal.Id}":$"question:{q.Id}";goalOptions.Add((goal,key));
            options.Add(new(key,goal.Title,q==null?"Resource":"Practice",goal.Minutes,false,15+goal.Priority-3,"LONG_TERM_GOAL",$"按目标周期安排（{(goal.Period=="Weekly"?"每周":"每天")} {goal.TargetValue} 次）",q?.Mappings.FirstOrDefault(m=>m.Mode=="WholeItem")?.KCId??goal.KCId,q?.Id,ResourceRef:goal.PaperReference));
        }
        foreach (var r in reviews)
        {
            var q=r.TargetType=="WrongQuestion" ? content.Questions.FirstOrDefault(q => q.Id==r.TargetId) : content.Questions.OrderBy(q => q.Id).FirstOrDefault(q => q.Policy=="SingleKC" && q.Mappings.Any(m => m.KCId==r.KCId && m.Mode=="WholeItem"));
            if (q==null) { warnings.Add($"MISSING_CONTENT: 复习目标 {r.TargetId} 缺少已发布题目"); continue; }
            options.Add(new($"question:{q.Id}",r.TargetType=="WrongQuestion" ? "到期错题复习" : "知识点保持复测","Review",5,false,r.TargetType=="WrongQuestion"?35:22,r.TargetType=="WrongQuestion"?"WRONG_DUE":"KC_REVIEW_DUE",$"{r.Stage} 复习已到期（{r.DueDate}）",r.KCId,q.Id,r.TargetId,DueDate:r.DueDate));
        }
        foreach (var lesson in content.Lessons.Where(l => progress.Any(p => p.LessonId==l.Id)))
        foreach (var kcId in lesson.KCIds)
        {
            var kc=content.Kcs.Single(k => k.Id==kcId); var m=mastery.FirstOrDefault(m => m.KCId==kcId);
            if (m is { Status: "Mastered" or "Stable", NeedsRecheck:false }) continue;
            var recent=evidence.Where(e=>e.KCId==kcId).GroupBy(e=>e.AttemptId).Select(g=>new {time=g.First().OccurredAt,negative=g.Any(e=>!e.Positive)}).OrderBy(x=>x.time).TakeLast(3).ToArray();
            if(recent.Length>=2 && recent.TakeLast(2).All(e=>e.negative))
            {
                var resource=content.Resources.Where(r=>r.KCIds.Contains(kcId) && r.Minutes is >=3 and <=15).OrderBy(r=>r.Id).FirstOrDefault();
                if(resource==null){warnings.Add($"MISSING_CONTENT: {kc.Name} 连续失败，需家长准备短讲解");continue;}
                options.Add(new($"explain:{kcId}:{resource.Id}",resource.Title,"Resource",resource.Minutes,false,25,"WEAK_CONFIRMED","连续两次遇题需要帮助，先做短讲解，减少刷题",kcId,ResourceRef:resource.PaperReference,ResourceUrl:resource.Url));
                continue;
            }
            var q=content.Questions.Where(q => q.Policy=="SingleKC" && q.Mappings.Any(x => x.KCId==kcId && x.Mode=="WholeItem")).OrderBy(q => history.Count(h => h.QuestionId==q.Id)).ThenBy(q => q.Id).FirstOrDefault();
            if (q==null) { warnings.Add($"MISSING_CONTENT: {kc.Name} 缺少可测题，请家长补充"); continue; }
            var today=progress.Any(p => p.LessonId==lesson.Id && p.Date==date);
            var reason=projection.Conservative?"SCHOOL_CONSERVATIVE":m?.NeedsRecheck==true ? "RECHECK" : today ? "SCHOOL_CURRENT" : m==null || m.Status=="Unknown" ? "LOW_EVIDENCE" : "WEAK_CONFIRMED";
            options.Add(new($"question:{q.Id}",kc.Name,m?.NeedsRecheck==true ? "Review" : "Practice",5,false,today?40:m?.NeedsRecheck==true?30:18,reason,projection.Conservative?"依据已确认学校进度安排基础练习，学习结果同步后再调整":today?"今天学校刚学，做一道题巩固":m?.NeedsRecheck==true?"需要两道独立诊断确认":"当前学习范围内，补充独立作答证据",kcId,q.Id,SchoolSequence:lesson.Sequence));
            if(m is {Probability:<.6m,Confidence:"Medium" or "High"})
            foreach(var pre in content.Relations.Where(r=>r.Type=="Prerequisite" && r.To==kcId))
            {
                var prerequisite=mastery.FirstOrDefault(x=>x.KCId==pre.From);
                if(prerequisite is not {Probability:<.6m,Confidence:"Medium" or "High"})continue;
                var diagnostic=content.Questions.Where(q=>q.Policy=="SingleKC" && q.Mappings.Any(x=>x.KCId==pre.From && x.Mode=="WholeItem")).OrderBy(q=>q.Id).FirstOrDefault();
                if(diagnostic!=null)options.Add(new($"question:{diagnostic.Id}","前置能力小诊断","Practice",5,false,20,"PREREQUISITE_CHECK","当前和前置能力都有弱证据，独立测一道题再决定",pre.From,diagnostic.Id));
            }
        }
        foreach(var goal in allGoals.Where(g=>g.Period=="Weekly" && (!g.StartDate.HasValue || date>=g.StartDate) && (!g.EndDate.HasValue || date<=g.EndDate)))
        {
            var count=Goals.Completed(goal,date,s.TimeZone,completedGoalTasks,answeredTaskIds);var weekEnd=Goals.PeriodStart(goal,date).AddDays(6);
            var possible=Enumerable.Range(0,weekEnd.DayNumber-date.DayNumber+1).Select(n=>date.AddDays(n)).Count(day=>Goals.Scheduled(goal,day) && Goals.Completed(goal,day,s.TimeZone,completedGoalTasks,answeredTaskIds,true)==0);
            if(count+possible<goal.TargetValue)warnings.Add($"GOAL_QUOTA_GAP: 目标 {goal.Title} 本周期还差 {goal.TargetValue-count} 次，可安排日期不足；不自动超出预算补齐");
        }
        var remainingOptions=options.Where(o => !fixedTasks.Any(t => t.QuestionId!=null && t.QuestionId==o.QuestionId || t.ReviewTargetId!=null && t.ReviewTargetId==o.ReviewTargetId || Json.Read<GoalSnapshot[]>(t.GoalSnapshots).Any(snapshot=>goalOptions.Any(g=>g.Key==o.Key && g.Goal.Id==snapshot.Id && Goals.Snapshot(g.Goal).Scope==snapshot.Scope)))).ToArray();
        var hash=Content.Hash(Json.Write(new { date,release.Id,budget,reserved,progress,allGoals,goalCounts=allGoals.Select(g=>new{g.Id,count=Goals.Completed(g,date,s.TimeZone,completedGoalTasks,answeredTaskIds)}).ToArray(),goals,mastery,reviews,fixedTasks,retiredTasks,options=remainingOptions,projection=PlanProjection.Input(projection),rule=RuleVersion }));
        var same=revisions.LastOrDefault(r => r.InputHash==hash && (r.Status=="Draft" || r.Id==plan.ActiveRevisionId)); if (same!=null) return same;
        var selection=Fit(remainingOptions,Math.Max(0,budget-reserved),fixedTasks.Sum(Charge),fixedTasks.Count);
        foreach(var goal in goalOptions.Where(g=>!selection.Selected.Any(o=>o.Key==g.Key) && !fixedTasks.Any(t=>Json.Read<GoalSnapshot[]>(t.GoalSnapshots).Any(snapshot=>snapshot.Id==g.Goal.Id))))warnings.Add($"GOAL_QUOTA_GAP: 目标 {goal.Goal.Title} 未能进入本次计划，请调整预算或优先级");
        if (selection.Overflow>0) warnings.Add($"MANDATORY_OVERFLOW: 必做/已执行任务超出预算 {selection.Overflow} 分钟");
        var projectionPayload=Json.Write(projection);
        var rev=new PlanRevision { ProjectionSnapshot=projectionPayload,ProjectionSnapshotHash=Content.Hash(projectionPayload),RuleVersion=RuleVersion,FamilyId=s.FamilyId,PlanId=plan.Id,ReleaseId=release.Id,Number=(revisions.LastOrDefault()?.Number??0)+1,Budget=budget,Reserved=reserved,Overflow=selection.Overflow,InputHash=hash,Candidates=Json.Write(selection.Rejected),Warnings=Json.Write(warnings) };
        db.PlanRevisions.Add(rev);
        var index=0;
        foreach (var t in fixedTasks.OrderBy(t => old.FindIndex(p => p.TaskId==t.Id))) db.Placements.Add(new() { FamilyId=s.FamilyId,RevisionId=rev.Id,TaskId=t.Id,Sequence=index++ });
        foreach (var o in selection.Selected)
        {
            var task=new StudyTask { GoalSnapshots=Json.Write(goalOptions.Where(g=>g.Key==o.Key).Select(g=>Goals.Snapshot(g.Goal)).ToArray()),FamilyId=s.FamilyId,StudentId=s.Id,ReleaseId=release.Id,Title=o.Title,Type=o.Type,Minutes=o.Minutes,Mandatory=o.Mandatory,KCId=o.KCId,QuestionId=o.QuestionId,ReviewTargetId=o.ReviewTargetId,ReasonCode=o.ReasonCode,Reason=o.Reason,ResourceRef=o.ResourceRef,ResourceUrl=o.ResourceUrl };
            db.Tasks.Add(task); db.Placements.Add(new() { FamilyId=s.FamilyId,RevisionId=rev.Id,TaskId=task.Id,Sequence=index++ });
        }
        return rev; // Generating a draft does not replace the currently executable plan.
    }
    public static async Task RefreshBudget(Database db,PlanRevision rev)
    {
        await db.SaveChangesAsync();
        var placements=await db.Placements.Where(p => p.RevisionId==rev.Id).OrderBy(p => p.Sequence).ToListAsync();
        var ids=placements.Select(p => p.TaskId).ToArray();var tasks=await db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync();
        var fixedTasks=tasks.Where(t => t.Mandatory || t.Locked || t.Status is "Completed" or "InProgress").ToArray();
        var charge=fixedTasks.Sum(Charge);var remaining=Math.Max(0,rev.Budget-rev.Reserved-charge);rev.Overflow=Math.Max(0,charge-(rev.Budget-rev.Reserved));
        var rejected=new List<object>();
        foreach (var p in placements.Where(p => !fixedTasks.Any(t => t.Id==p.TaskId)))
        {
            var t=tasks.Single(t => t.Id==p.TaskId);
            if (t.Minutes>remaining || rev.Overflow>0) { db.Placements.Remove(p);rejected.Add(new { taskId=t.Id,reason="BUDGET_AFTER_PARENT_ADJUSTMENT" }); }
            else remaining-=t.Minutes;
        }
        var warnings=Json.Read<string[]>(rev.Warnings).Where(w => !w.StartsWith("MANDATORY_OVERFLOW")).ToList();
        if (rev.Overflow>0) warnings.Add($"MANDATORY_OVERFLOW: 必做/已执行任务超出预算 {rev.Overflow} 分钟");
        rev.Warnings=Json.Write(warnings);if (rejected.Count>0) rev.Candidates=Json.Write(new { previous=System.Text.Json.JsonDocument.Parse(rev.Candidates).RootElement,rejected });
        await db.SaveChangesAsync();
    }
}
