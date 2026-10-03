using Microsoft.EntityFrameworkCore;
namespace Learning;
public record JobCancellationSummary(Guid JobId,string Status,bool AlreadyCancelled,bool ExecutionStopConfirmed);
public static class JobCancellation
{
    public const string Code="JOB_CANCELLED_BY_USER";
    public static void Authorize(Actor actor,BackgroundJob j)
    {
        if(j.Type==AssessmentRebuildJobs.Type)actor.Require("Parent");else if(j.Type is "BuilderCandidates" or "ParsePDF" or "MappingSuggestions")actor.Require("ContentEditor");else throw new ApiError(422,"JOB_CANCELLATION_UNSUPPORTED","学习结果同步必须完成，不能取消；故障时请使用结果恢复入口。");
    }
    public static async Task<JobCancellationSummary> Cancel(Database db,Actor actor,Guid id,string reason,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(reason) || reason.Length>4000)throw new ApiError(422,"REASON_REQUIRED","请填写取消依据，最多4000字。");
        if(actor.Role=="Child" || !actor.Can("Parent") && !actor.Can("ContentEditor"))throw new ApiError(403,"FORBIDDEN","需要家长或内容维护权限。");
        var rows=await db.Set<BackgroundJob>().FromSqlInterpolated($"SELECT * FROM \"BackgroundJob\" WHERE \"Id\"={id} AND \"FamilyId\"={actor.FamilyId} FOR NO KEY UPDATE").ToArrayAsync(ct);
        if(rows.Length!=1)throw new ApiError(404,"NOT_FOUND","找不到本家庭任务。");var j=rows[0];await db.Entry(j).ReloadAsync(ct);
        Authorize(actor,j);
        if(j.Status=="Cancelled")return new(j.Id,j.Status,true,!await db.Set<JobLeaseAttempt>().AnyAsync(a=>a.JobId==j.Id && a.Status=="Running",ct));
        if(j.Status is not ("Queued" or "Retrying" or "Running"))throw new ApiError(409,"JOB_NOT_ACTIVE","已完成或已停止的任务不能取消。");
        var previous=j.Status;var leaseOwner=j.LeaseOwner;j.Status="Cancelled";j.LastErrorCode=Code;j.NextRunAt=null;j.LeaseOwner=null;j.LeaseExpiresAt=null;
        db.Audits.Add(new(){FamilyId=j.FamilyId,StudentId=j.StudentId,ActorId=actor.Id,Action="BackgroundJobCancellationRequested",Details=Json.Write(new{jobId=j.Id,j.Type,j.InputRef,j.InputHash,j.RetryRound,previousStatus=previous,leaseOwner,reason=reason.Trim(),submissionRevoked=true})});
        return new(j.Id,j.Status,false,previous!="Running");
    }
    public static async Task ObserveStopped(Database owner,BackgroundJob job)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await using var db=BackgroundJobs.Open(owner);await using var tx=await db.Database.BeginTransactionAsync(timeout.Token);
        var details=await db.Audits.AsNoTracking().Where(a=>a.FamilyId==job.FamilyId && a.Action=="BackgroundJobCancellationRequested" && a.Details.Contains(job.Id.ToString())).Select(a=>a.Details).ToArrayAsync(timeout.Token);
        if(!details.Any(raw=>{using var doc=System.Text.Json.JsonDocument.Parse(raw);var p=doc.RootElement;return p.GetProperty("jobId").GetGuid()==job.Id && p.GetProperty("leaseOwner").GetString()==job.LeaseOwner && p.GetProperty("retryRound").GetInt32()==job.RetryRound;}))return;
        var count=await db.Set<JobLeaseAttempt>().Where(a=>a.FamilyId==job.FamilyId && a.JobId==job.Id && a.LeaseOwner==job.LeaseOwner && a.Status=="Running").ExecuteUpdateAsync(u=>u.SetProperty(a=>a.Status,"Cancelled").SetProperty(a=>a.ErrorCode,Code).SetProperty(a=>a.FinishedAt,DateTimeOffset.UtcNow),timeout.Token);
        if(count==1)db.Audits.Add(new(){FamilyId=job.FamilyId,StudentId=job.StudentId,Action="BackgroundJobCancellationObserved",Details=Json.Write(new{jobId=job.Id,leaseOwner=job.LeaseOwner,job.RetryRound,executionStopConfirmed=true})});await db.SaveChangesAsync(timeout.Token);await tx.CommitAsync(timeout.Token);
    }
    public static async Task ReconcileBuilder(Database db,CancellationToken ct)
    {
        var cancelled=await db.Set<BackgroundJob>().AsNoTracking().Where(j=>j.Status=="Cancelled" && j.LastErrorCode==Code && (j.Type=="BuilderCandidates" || j.Type=="ParsePDF")).Select(j=>new{j.FamilyId,j.InputRef,j.RetryRound}).ToArrayAsync(ct);
        foreach(var job in cancelled)await db.BuilderRuns.Where(r=>r.Id==job.InputRef && r.FamilyId==job.FamilyId && r.RetryRound==job.RetryRound && r.Status=="Queued").ExecuteUpdateAsync(u=>u.SetProperty(r=>r.Status,"Cancelled").SetProperty(r=>r.Error,Code).SetProperty(r=>r.NextAttemptAt,(DateTimeOffset?)null).SetProperty(r=>r.CompletedAt,(DateTimeOffset?)null),ct);
    }
    public static void Map(RouteGroupBuilder api)=>api.MapPost("/background-jobs/{id:guid}:cancel",async(Guid id,ReasonInput input,Database db,HttpContext ctx)=>await Cancel(db,ctx.Actor(),id,input.Reason,ctx.RequestAborted)).WithMetadata(new JobControlCommandMetadata());
}
