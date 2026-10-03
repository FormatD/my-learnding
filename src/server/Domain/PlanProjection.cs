using Microsoft.EntityFrameworkCore;
namespace Learning;
public record PlanProjectionPending(Guid EventId,Guid? DomainEventId,DateTimeOffset CreatedAt,int Retries,string? Error);
public record PlanProjectionFailedJob(Guid JobId,Guid EventId,string? ErrorCode,int AttemptCount,int RetryRound);
public record PlanProjectionSnapshot(string Version,Guid FamilyId,Guid StudentId,DateTimeOffset CapturedAt,Guid? GenerationId,string? GenerationInputHash,long? GenerationAttemptCursor,long? AssessmentEventSequence,Guid? AssessmentAppliedEventId,PlanProjectionPending[] Pending,PlanProjectionFailedJob[] FailedJobs,int FailedCount,bool Conservative,int LagThresholdSeconds);
public static class PlanProjection
{
    public const int LagThresholdSeconds=30;
    public static async Task<PlanProjectionSnapshot> Capture(Database db,Student student)
    {
        var now=await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync();
        var generation=student.ActiveGenerationId==null?null:await db.Generations.SingleAsync(g=>g.FamilyId==student.FamilyId && g.StudentId==student.Id && g.Id==student.ActiveGenerationId && g.Status=="Active");
        var cursor=await db.Set<AssessmentConsumerCursor>().AsNoTracking().SingleOrDefaultAsync(c=>c.FamilyId==student.FamilyId && c.StudentId==student.Id && c.ConsumerName==ProjectionJobs.Consumer);
        var pending=await db.Outbox.Where(o=>o.FamilyId==student.FamilyId && o.StudentId==student.Id && o.ProcessedAt==null).OrderBy(o=>o.CreatedAt).ThenBy(o=>o.Id).Select(o=>new PlanProjectionPending(o.Id,o.DomainEventId,o.CreatedAt,o.Retries,o.Error)).ToArrayAsync();
        var pendingIds=pending.Select(o=>o.EventId).ToArray();
        var failures=await db.Set<BackgroundJob>().Where(j=>j.FamilyId==student.FamilyId && j.StudentId==student.Id && j.Type=="AssessmentProjection" && j.Status=="Failed" && pendingIds.Contains(j.InputRef)).OrderBy(j=>j.Id).Select(j=>new PlanProjectionFailedJob(j.Id,j.InputRef,j.LastErrorCode,j.AttemptCount,j.RetryRound)).ToArrayAsync();
        var failedIds=failures.Select(j=>j.EventId).ToHashSet();var failed=pending.Count(o=>o.Retries>=3 || failedIds.Contains(o.EventId));var conservative=failed>0 || pending.Any(o=>o.CreatedAt<now.AddSeconds(-LagThresholdSeconds));
        return new("plan-projection/1",student.FamilyId,student.Id,now,generation?.Id,generation?.InputHash,generation?.Cursor,cursor?.LastEventSequence,cursor?.LastAppliedEventId,pending,failures,failed,conservative,LagThresholdSeconds);
    }
    // Capture time is provenance, not a new logical input on every identical generate request.
    public static object Input(PlanProjectionSnapshot snapshot)=>new{snapshot.Version,snapshot.FamilyId,snapshot.StudentId,snapshot.GenerationId,snapshot.GenerationInputHash,snapshot.GenerationAttemptCursor,snapshot.AssessmentEventSequence,snapshot.AssessmentAppliedEventId,snapshot.Pending,snapshot.FailedJobs,snapshot.FailedCount,snapshot.Conservative,snapshot.LagThresholdSeconds};
}
