namespace Learning;
public record MasteryCheckpoint(Guid KCId,Guid KCRevisionId,string Name,string Status,bool NeedsRecheck,decimal Probability,string Confidence,decimal EffectiveEvidence,int DistinctQuestions,string[] RequiredCoverage,string[] CoveredCoverage,bool Delay2,bool Delay7,bool Delay30,string Reason);
public record MasteryEvent(Guid AttemptId,DateTimeOffset OccurredAt,MasteryCheckpoint State);
public record MasteryChange(Guid AttemptId,DateTimeOffset OccurredAt,MasteryCheckpoint? Before,MasteryCheckpoint After);
public record WeeklyEvidenceItem(Guid AttemptId,Guid GradingId,Guid ReleaseId,Guid? MappingReleaseId,Guid? CorrectionBatchId,Guid KCRevisionId,string Part,bool Positive,decimal RawWeight,decimal Weight,DateTimeOffset OccurredAt);
public record WeeklyMasteryItem(Guid KCId,string Name,MasteryCheckpoint? Before,MasteryCheckpoint After,int EvidenceParts,int Encounters,int SuppressedParts,decimal PositiveWeight,decimal NegativeWeight,int StatusChanges,int RecheckChanges,int CoverageChanges,MasteryChange[] Changes,WeeklyEvidenceItem[] Evidence);
public record MasteryChangeSummary(int EvidenceParts,int Encounters,int SuppressedParts,int StatusChanges,int RecheckChanges,int CoverageChanges,WeeklyMasteryItem[] Items,string EvidenceRuleVersion,string ModelVersion,string Note);
public static class MasteryReporting
{
    public static MasteryChangeSummary Calculate(AssessmentOutput replay,string zone,DateOnly start,DateOnly end)
    {
        var timeZone=TimeZoneInfo.FindSystemTimeZoneById(zone);
        DateOnly Local(DateTimeOffset time)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time,timeZone).DateTime);
        bool InPeriod(DateTimeOffset time)=>Local(time)>=start && Local(time)<=end;
        var evidence=replay.Evidence.Where(e=>InPeriod(e.OccurredAt)).ToArray();var items=new List<WeeklyMasteryItem>();
        foreach(var group in replay.MasteryHistory.Where(e=>Local(e.OccurredAt)<=end).GroupBy(e=>e.State.KCId))
        {
            var before=group.LastOrDefault(e=>Local(e.OccurredAt)<start)?.State;var after=group.Last().State;
            var previous=before;var changes=new List<MasteryChange>();
            foreach(var e in group.Where(e=>InPeriod(e.OccurredAt)))
            {
                if(previous==null || Json.Write(previous)!=Json.Write(e.State))changes.Add(new(e.AttemptId,e.OccurredAt,previous,e.State));
                previous=e.State;
            }
            var parts=evidence.Where(e=>e.KCId==group.Key).ToArray();
            var rows=parts.Select(e=>new WeeklyEvidenceItem(e.AttemptId,e.GradingId,e.ReleaseId,e.MappingReleaseId,e.CorrectionBatchId,e.KCRevisionId,e.Part,e.Positive,e.RawWeight,e.Weight,e.OccurredAt)).ToArray();
            var status=changes.Count(c=>(c.Before?.Status??"Unknown")!=c.After.Status);var recheck=changes.Count(c=>(c.Before?.NeedsRecheck??false)!=c.After.NeedsRecheck);
            var coverage=changes.Count(c=>c.Before==null?c.After.CoveredCoverage.Length>0 || c.After.Delay2 || c.After.Delay7 || c.After.Delay30:
                !c.Before.RequiredCoverage.SequenceEqual(c.After.RequiredCoverage) || !c.Before.CoveredCoverage.SequenceEqual(c.After.CoveredCoverage) || c.Before.Delay2!=c.After.Delay2 || c.Before.Delay7!=c.After.Delay7 || c.Before.Delay30!=c.After.Delay30);
            items.Add(new(group.Key,after.Name,before,after,parts.Length,parts.Select(p=>p.AttemptId).Distinct().Count(),parts.Count(p=>p.Weight==0),parts.Where(p=>p.Positive).Sum(p=>p.Weight),parts.Where(p=>!p.Positive).Sum(p=>p.Weight),status,recheck,coverage,changes.ToArray(),rows));
        }
        return new(evidence.Length,evidence.Select(e=>e.AttemptId).Distinct().Count(),evidence.Count(e=>e.Weight==0),items.Sum(i=>i.StatusChanges),items.Sum(i=>i.RecheckChanges),items.Sum(i=>i.CoverageChanges),items.OrderBy(i=>i.Name).ThenBy(i=>i.KCId).ToArray(),Assessment.EvidenceRuleVersion,Assessment.MasteryModelVersion,
            "按实际作答日期和学生时区比较期初与期末，采用查看时最新判分与题目归因重算。证据按观察部分计数，同一次遇题可有多条；零权重保留但不增加掌握证据。状态、待复核与覆盖变化分别显示；没有期初记录保持缺口，新能力不继承旧掌握。后续更正可改变往周结果，这不是当时已知的状态存档，也不能据此证明长期学习效果。");
    }
}
