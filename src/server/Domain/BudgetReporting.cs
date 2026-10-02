namespace Learning;
public record BudgetDeferral(DateOnly TargetDate,string Reason,DateTimeOffset ConfirmedAt);
public record BudgetTaskResult(Guid TaskId,string Title,string Status,int EstimatedMinutes,int? ActualMinutes,bool CompletedWithinEstimate,bool UnknownCompletedDuration,BudgetDeferral[] Deferrals,bool Eligible);
public record BudgetExecutability(int PublishedTasks,int CompletedWithinEstimate,int ConfirmedDeferredTasks,int EligibleTasks,int UnknownCompletedDuration,decimal? Rate,BudgetTaskResult[] Tasks,string Note);
public static class BudgetReporting
{
    public static BudgetExecutability Calculate(IEnumerable<StudyTask> tasks,WeeklyAdjustment[] adjustments)
    {
        var confirmations=adjustments.Where(a=>a.Published && a.Details.Kind=="TaskDeferral" && a.Details.DeferredTo!=null && !string.IsNullOrWhiteSpace(a.Details.Reason))
            .SelectMany(a=>a.Details.RemovedTaskIds.Select(id=>(TaskId:id,Record:new BudgetDeferral(a.Details.DeferredTo!.Value,a.Details.Reason,a.RecordedAt))))
            .GroupBy(a=>a.TaskId).ToDictionary(g=>g.Key,g=>g.Select(a=>a.Record).ToArray());
        var rows=tasks.DistinctBy(t=>t.Id).Select(t=>
        {
            var unknown=t.Status=="Completed" && (t.ActualMinutes is null or <0 || t.Minutes<=0);
            var within=t.Status=="Completed" && !unknown && t.ActualMinutes<=t.Minutes;
            var deferred=confirmations.GetValueOrDefault(t.Id,[]);
            return new BudgetTaskResult(t.Id,t.Title,t.Status,t.Minutes,t.ActualMinutes,within,unknown,deferred,within || deferred.Length>0);
        }).ToArray();
        var eligible=rows.Count(r=>r.Eligible);
        return new(rows.Length,rows.Count(r=>r.CompletedWithinEstimate),rows.Count(r=>r.Deferrals.Length>0),eligible,rows.Count(r=>r.UnknownCompletedDuration),rows.Length==0?null:(decimal)eligible/rows.Length,rows,
            "按所选日期所有已发布修订中的固定任务去重；查看时，实际耗时不超过该任务预计分钟的完成项，或明确家长顺延项计入，两者重叠只算一次。未做、跳过、孩子自行延期不自动计入；完成但时长未知保留缺口。这是任务时长口径，不是每日总预算达标率或过去时刻快照。");
    }
}
