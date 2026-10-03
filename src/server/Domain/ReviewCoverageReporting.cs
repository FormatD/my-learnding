namespace Learning;
public record ReviewCoverageItem(ReviewOccurrence Schedule,bool ExecutedInPeriod,bool OverdueAtStart);
public record ReviewCoverageSummary(int DueSchedules,int ExecutedSchedules,int UnexecutedSchedules,int OverdueAtStart,decimal? Rate,ReviewCoverageItem[] Items,string RuleVersion,string Note);
public static class ReviewCoverageReporting
{
    public static ReviewCoverageSummary Calculate(IEnumerable<ReviewOccurrence> history,string zone,DateOnly start,DateOnly end)
    {
        var timeZone=TimeZoneInfo.FindSystemTimeZoneById(zone);
        DateOnly Local(DateTimeOffset time)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time,timeZone).DateTime);
        var rows=history.Where(r=>r.DueDate<=end && Local(r.ScheduledAt)<=end
            && (r.ClosedAt==null || Local(r.ClosedAt.Value)>=r.DueDate && Local(r.ClosedAt.Value)>=start)
            && (r.ExecutedAt==null || Local(r.ExecutedAt.Value)>=start))
            .Select(r=>new ReviewCoverageItem(r,r.ExecutedAt!=null && Local(r.ExecutedAt.Value)<=end,r.DueDate<start)).ToArray();
        var executed=rows.Count(r=>r.ExecutedInPeriod);
        return new(rows.Length,executed,rows.Length-executed,rows.Count(r=>r.OverdueAtStart),rows.Length==0?null:(decimal)executed/rows.Length,rows,Assessment.ReviewRuleVersion,
            "按当前有效判分、映射与复习规则重放日程；分母包含所选周新到期及期初仍未执行的逾期日程，每次阶段或到期日期变更分别保留。到期后提交对应目标的首次答案算执行，知识点复习还须实际题目仍测量原目标；待判分、错误或提示不等于独立通过。跳过、仅看解析及未作答不算执行。期末之后的作答不计本周执行；到期前已被重置的日程不计到期。更正可能重算往周口径，这不是当时已知日程的存档快照，未做日程保留。");
    }
    public static async Task<ReviewCoverageSummary> Read(Database db,Student student,DateOnly start,DateOnly end,CancellationToken ct)
    {
        var (inputs,teaching)=await Assessment.LoadInputs(db,student,ct);
        var replay=Assessment.Replay(student.FamilyId,student.Id,Guid.Empty,student.TimeZone,inputs,teaching,true);
        return Calculate(replay.ReviewHistory,student.TimeZone,start,end);
    }
}
