using Microsoft.EntityFrameworkCore;
namespace Learning;
public record WeeklyCounts(int Total,int Completed);
public record WeeklyPlacement(Guid PlanId,Guid RevisionId,DateOnly Date,int Revision,bool Original,bool Current);
public record WeeklyTask(Guid TaskId,string Title,string Status,int EstimatedMinutes,int? ActualMinutes,WeeklyPlacement[] Placements);
public record PlanAdjustmentDetails(Guid PlanId,Guid RevisionId,string Reason,Guid[] BeforeTaskIds,Guid[] AfterTaskIds,Guid[] RemovedTaskIds,Guid[] LockedIds,Guid[] RequestedTaskIds,string Kind="DraftAdjustment",DateOnly? DeferredTo=null);
public record WeeklyAdjustment(Guid Id,DateTimeOffset RecordedAt,bool Published,PlanAdjustmentDetails Details);
public record TaskTransitionDetails(Guid StudentId,Guid TaskId,string From,string To,string? Reason);
public record WeeklyTransition(Guid Id,DateTimeOffset RecordedAt,TaskTransitionDetails Details);
public record WeeklySummary(DateOnly Start,DateOnly End,DateOnly CurrentDate,DateTimeOffset ObservedAt,string TimeZone,WeeklyCounts Original,WeeklyCounts Adjusted,int ActualMinutes,int DueReviews,int Pending,ParentBurdenSummary ParentBurden,BudgetExecutability BudgetExecutability,WeeklyTask[] Tasks,WeeklyAdjustment[] Adjustments,WeeklyTransition[] Transitions,string HistoryNote);
public static class WeeklyReporting
{
    public static async Task<WeeklySummary> Read(Database db,Actor actor,Guid id,DateOnly? end,CancellationToken ct=default)
    {
        actor.Require("Parent");await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var s=await actor.Student(db,id);var observed=DateTimeOffset.UtcNow;var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(observed,TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone)).DateTime);
        var last=end??today;if(last>today || last<new DateOnly(2000,1,7))throw new ApiError(422,"INVALID_REPORT_DATE","请选择今天或更早的周结束日期（2000年1月7日以后）。");var start=last.AddDays(-6);
        var plans=await db.Plans.Where(p=>p.StudentId==id && p.Date>=start && p.Date<=last).ToListAsync(ct);var ids=plans.Select(p=>p.Id).ToArray();
        var revisions=await db.PlanRevisions.Where(r=>ids.Contains(r.PlanId) && r.Status=="Published").OrderBy(r=>r.Number).ToListAsync(ct);
        var originalIds=revisions.GroupBy(r=>r.PlanId).Select(g=>g.First().Id).ToHashSet();var currentIds=plans.Where(p=>p.ActiveRevisionId!=null).Select(p=>p.ActiveRevisionId!.Value).ToHashSet();var revisionIds=revisions.Select(r=>r.Id).ToArray();
        var placements=await db.Placements.Where(p=>revisionIds.Contains(p.RevisionId)).ToListAsync(ct);var tids=placements.Select(p=>p.TaskId).Distinct().ToArray();var tasks=await db.Tasks.Where(t=>tids.Contains(t.Id)).OrderBy(t=>t.CreatedAt).ThenBy(t=>t.Id).ToListAsync(ct);
        var original=placements.Where(p=>originalIds.Contains(p.RevisionId)).Select(p=>p.TaskId).ToHashSet();var current=placements.Where(p=>currentIds.Contains(p.RevisionId)).Select(p=>p.TaskId).ToHashSet();
        var rows=tasks.Select(t=>new WeeklyTask(t.Id,t.Title,t.Status,t.Minutes,t.ActualMinutes,placements.Where(p=>p.TaskId==t.Id).Select(p=>{var r=revisions.Single(r=>r.Id==p.RevisionId);var plan=plans.Single(plan=>plan.Id==r.PlanId);return new WeeklyPlacement(plan.Id,r.Id,plan.Date,r.Number,originalIds.Contains(r.Id),currentIds.Contains(r.Id));}).OrderBy(p=>p.Date).ThenBy(p=>p.Revision).ToArray())).ToArray();
        var audits=await db.Audits.Where(a=>a.FamilyId==actor.FamilyId && a.StudentId==id && a.Action=="PlanAdjusted").OrderBy(a=>a.CreatedAt).ThenBy(a=>a.Id).ToListAsync(ct);
        var adjustments=audits.Select(a=>new WeeklyAdjustment(a.Id,a.CreatedAt,false,Json.Read<PlanAdjustmentDetails>(a.Details))).Where(a=>ids.Contains(a.Details.PlanId)).Select(a=>a with {Published=revisionIds.Contains(a.Details.RevisionId)}).ToArray();
        var transitionAudits=await db.Audits.Where(a=>a.FamilyId==actor.FamilyId && a.StudentId==id && a.Action=="TaskTransition").OrderBy(a=>a.CreatedAt).ThenBy(a=>a.Id).ToListAsync(ct);
        var transitions=transitionAudits.Select(a=>new WeeklyTransition(a.Id,a.CreatedAt,Json.Read<TaskTransitionDetails>(a.Details))).Where(a=>a.Details.StudentId==id && tids.Contains(a.Details.TaskId)).ToArray();
        var burden=await ParentBurden.Summary(db,id,start,last);var due=await db.Reviews.CountAsync(r=>r.StudentId==id && r.GenerationId==s.ActiveGenerationId && r.Status=="Pending" && r.DueDate<=today,ct);var pending=await db.Outbox.CountAsync(o=>o.StudentId==id && o.ProcessedAt==null,ct);
        var result=new WeeklySummary(start,last,today,observed,s.TimeZone,new(original.Count,tasks.Count(t=>original.Contains(t.Id) && t.Status=="Completed")),new(current.Count,tasks.Count(t=>current.Contains(t.Id) && t.Status=="Completed")),tasks.Sum(t=>t.ActualMinutes??0),due,pending,burden,BudgetReporting.Calculate(tasks,adjustments),rows,adjustments,transitions,"按所选日期内的已发布计划及当前任务状态汇总；当前到期复习不代表往周覆盖率。旧调整及执行审计只有摘要，未保存的原因不能推算。草稿任务不进入完成率。");
        await tx.CommitAsync(ct);return result;
    }
}
