using Microsoft.EntityFrameworkCore;
namespace Learning;

public class BackgroundJob:Row
{
    public Guid? StudentId {get;set;}
    public Guid? TargetGenerationId {get;set;}
    public string Type {get;set;}="";
    public Guid InputRef {get;set;}
    public string IdempotencyKey {get;set;}="";
    public string InputPayload {get;set;}="";
    public string InputHash {get;set;}="";
    public string Status {get;set;}="Queued";
    public int AttemptCount {get;set;}
    public int MaxAttempts {get;set;}=4;
    public int RetryRound {get;set;}
    public DateTimeOffset? NextRunAt {get;set;}
    public string? LeaseOwner {get;set;}
    public DateTimeOffset? LeaseExpiresAt {get;set;}
    public DateTimeOffset? HeartbeatAt {get;set;}
    public string? LastErrorCode {get;set;}
    public int LeaseSeconds {get;set;}=30;
    public int HeartbeatSeconds {get;set;}=5;
}
public class JobLeaseAttempt:Row
{
    public Guid JobId {get;set;}
    public string LeaseOwner {get;set;}="";
    public int Number {get;set;}
    public int RetryRound {get;set;}
    public string Status {get;set;}="Running";
    public DateTimeOffset StartedAt {get;set;}
    public DateTimeOffset? FinishedAt {get;set;}
    public string? ErrorCode {get;set;}
}
public static class BackgroundJobs
{
    public static Database Open(Database db)=>new(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()??throw new InvalidOperationException("Job connection missing")).Options);
    public static string Snapshot(BuilderRun run)=>Json.Write(new{run.SourceId,run.Type,run.LibraryReleaseId,run.InputVersion,run.InputHash,run.Provider,run.Model,run.PromptVersion,run.ModelConfigHash,run.ModelConfigPayload});
    public static async Task EnsureBuilderJobs(Database owner,CancellationToken ct=default)
    {
        await using var db=Open(owner);
        await JobCancellation.ReconcileBuilder(db,ct);
        foreach(var run in await db.BuilderRuns.AsNoTracking().Where(r=>r.Status=="Queued").ToArrayAsync(ct))
        {
            var type=run.Type=="ParsePDF"?"ParsePDF":"BuilderCandidates";var payload=Snapshot(run);var hash=Content.Hash(payload);var key="builder:"+run.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"BackgroundJob\" (\"Id\",\"FamilyId\",\"CreatedAt\",\"Type\",\"InputRef\",\"IdempotencyKey\",\"InputPayload\",\"InputHash\",\"Status\",\"AttemptCount\",\"MaxAttempts\",\"RetryRound\",\"NextRunAt\",\"LeaseSeconds\",\"HeartbeatSeconds\") VALUES ({Guid.NewGuid()},{run.FamilyId},{run.CreatedAt},{type},{run.Id},{key},{payload},{hash},'Queued',0,4,{run.RetryRound},{run.NextAttemptAt},30,5) ON CONFLICT (\"FamilyId\",\"Type\",\"InputRef\") DO NOTHING",ct);
            // A new human retry round is authoritative; preserve the original job input and earlier lease attempts.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BackgroundJob\" SET \"Status\"='Queued',\"AttemptCount\"=0,\"RetryRound\"={run.RetryRound},\"NextRunAt\"={run.NextAttemptAt},\"LeaseOwner\"=NULL,\"LeaseExpiresAt\"=NULL WHERE \"FamilyId\"={run.FamilyId} AND \"InputRef\"={run.Id} AND \"Type\"={type} AND \"RetryRound\"<{run.RetryRound} AND \"Status\"<>'Running'",ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BackgroundJob\" SET \"NextRunAt\"={run.NextAttemptAt} WHERE \"FamilyId\"={run.FamilyId} AND \"InputRef\"={run.Id} AND \"Type\"={type} AND \"Status\"='Retrying' AND \"RetryRound\"={run.RetryRound}",ct);
        }
    }
    public static async Task<JobLease?> Claim(Database owner,CancellationToken ct=default,string[]? types=null,Guid? inputRef=null)
    {
        types??=["BuilderCandidates","ParsePDF"];await using var db=Open(owner);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var jobs=await db.Set<BackgroundJob>().FromSqlInterpolated($"SELECT * FROM \"BackgroundJob\" WHERE \"Type\"=ANY({types}) AND (\"InputRef\"={inputRef??Guid.Empty} OR {inputRef==null}) AND ((\"Status\" IN ('Queued','Retrying') AND (\"NextRunAt\" IS NULL OR \"NextRunAt\"<=clock_timestamp())) OR (\"Status\"='Running' AND \"LeaseExpiresAt\"<=clock_timestamp())) ORDER BY COALESCE(\"NextRunAt\",\"CreatedAt\"),\"Id\" FOR UPDATE SKIP LOCKED LIMIT 1").ToArrayAsync(ct);
        if(jobs.Length==0){await tx.CommitAsync(ct);return null;}var job=jobs[0];var now=await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        if(job.LeaseOwner!=null)
        {
            var old=await db.Set<JobLeaseAttempt>().SingleOrDefaultAsync(a=>a.JobId==job.Id && a.LeaseOwner==job.LeaseOwner && a.Status=="Running",ct);
            if(old!=null){old.Status="LeaseExpired";old.FinishedAt=now;old.ErrorCode="JOB_LEASE_EXPIRED";}
        }
        var execute=job.AttemptCount<job.MaxAttempts;job.Status="Running";job.LeaseOwner=Guid.NewGuid().ToString();job.HeartbeatAt=now;job.LeaseExpiresAt=now.AddSeconds(job.LeaseSeconds);
        if(execute){job.AttemptCount++;db.Add(new JobLeaseAttempt{FamilyId=job.FamilyId,JobId=job.Id,LeaseOwner=job.LeaseOwner,Number=job.AttemptCount,RetryRound=job.RetryRound,StartedAt=now});}
        else job.LastErrorCode="JOB_ATTEMPTS_EXHAUSTED";
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new JobLease(owner,job,execute,ct);
    }
    public static void Map(RouteGroupBuilder api)
    {
        JobCancellation.Map(api);
        api.MapGet("/background-jobs/window",async(int? pageSize,string? cursor,Database db,HttpContext ctx)=>{
            var actor=ctx.Actor();actor.Require("ContentEditor");var ct=ctx.RequestAborted;await using var snapshot=await ReadSnapshot.Begin(db,ctx);
            var page=await StableReadPage.Load(db.Set<BackgroundJob>().AsNoTracking().Where(j=>j.FamilyId==actor.FamilyId),"background-jobs/1",actor.FamilyId,null,pageSize,cursor,ct);
            var ids=page.Rows.Select(j=>j.Id).ToArray();var attempts=await db.Set<JobLeaseAttempt>().AsNoTracking().Where(j=>j.FamilyId==actor.FamilyId&&ids.Contains(j.JobId)).OrderBy(j=>j.CreatedAt).ThenBy(j=>j.Id).ToArrayAsync(ct);
            await snapshot.CommitAsync(ct);return new{page.PageSize,page.Total,jobs=page.Rows,attempts,nextCursor=page.NextCursor};
        }).WithMetadata(new OptionalResponseFieldsMetadata("nextCursor"));
        api.MapGet("/background-jobs",async(int? page,int? pageSize,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("ContentEditor");var p=page??1;var size=pageSize??20;if(p<1 || p>100_000 || size<1 || size>50)throw new ApiError(422,"INVALID_PAGE","请使用有效页码及每页1～50条。");var query=db.Set<BackgroundJob>().Where(j=>j.FamilyId==a.FamilyId);var total=await query.CountAsync();var jobs=await query.OrderByDescending(j=>j.CreatedAt).ThenBy(j=>j.Id).Skip((p-1)*size).Take(size).ToArrayAsync();var ids=jobs.Select(j=>j.Id).ToArray();return new{page=p,pageSize=size,total,jobs,attempts=await db.Set<JobLeaseAttempt>().Where(j=>j.FamilyId==a.FamilyId && ids.Contains(j.JobId)).OrderBy(j=>j.CreatedAt).ThenBy(j=>j.Id).ToArrayAsync()};});
    }
}
public sealed class JobLease:IAsyncDisposable
{
    readonly Database owner;readonly string connection;readonly CancellationTokenSource work,stop=new();readonly Task heartbeat;int lost;
    public BackgroundJob Job {get;}
    public bool CanExecute {get;}
    public CancellationToken Token=>work.Token;
    public bool Lost=>Volatile.Read(ref lost)!=0;
    public JobLease(Database db,BackgroundJob job,bool execute,CancellationToken ct){owner=db;connection=db.Database.GetConnectionString()!;Job=job;CanExecute=execute;work=CancellationTokenSource.CreateLinkedTokenSource(ct);heartbeat=Pulse();}
    Database Open()=>new(new DbContextOptionsBuilder<Database>().UseNpgsql(connection).Options);
    void Lose(){Interlocked.Exchange(ref lost,1);work.Cancel();}
    async Task Pulse()
    {
        try
        {
            while(true)
            {
                await Task.Delay(TimeSpan.FromSeconds(Job.HeartbeatSeconds),stop.Token);await using var db=Open();
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(Job.HeartbeatSeconds));
                var count=await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BackgroundJob\" SET \"HeartbeatAt\"=clock_timestamp(),\"LeaseExpiresAt\"=clock_timestamp()+make_interval(secs => \"LeaseSeconds\") WHERE \"Id\"={Job.Id} AND \"FamilyId\"={Job.FamilyId} AND \"Status\"='Running' AND \"LeaseOwner\"={Job.LeaseOwner} AND \"LeaseExpiresAt\">clock_timestamp()",timeout.Token);
                if(count!=1){Lose();return;}
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        catch{Lose();}
    }
    // Serialize a durable call start against cancellation, without holding this lock during the provider wait.
    public async Task PermitCall(Database db,CancellationToken ct)
    {
        var rows=await db.Set<BackgroundJob>().FromSqlInterpolated($"SELECT * FROM \"BackgroundJob\" WHERE \"Id\"={Job.Id} AND \"FamilyId\"={Job.FamilyId} AND \"Status\"='Running' AND \"LeaseOwner\"={Job.LeaseOwner} AND \"LeaseExpiresAt\">clock_timestamp() FOR SHARE").AsNoTracking().ToArrayAsync(ct);
        if(rows.Length!=1){Lose();throw new OperationCanceledException("Job no longer permits a call",Token);}
    }
    // Lock the still-valid owner row in the result transaction. A replacement owner cannot pass this fence concurrently.
    public async Task Finish(Database db,string status,string? error,DateTimeOffset? next,CancellationToken ct)
    {
        var rows=await db.Set<BackgroundJob>().FromSqlInterpolated($"SELECT * FROM \"BackgroundJob\" WHERE \"Id\"={Job.Id} AND \"FamilyId\"={Job.FamilyId} AND \"Status\"='Running' AND \"LeaseOwner\"={Job.LeaseOwner} AND \"LeaseExpiresAt\">clock_timestamp() FOR UPDATE").ToArrayAsync(ct);
        if(rows.Length!=1){Lose();throw new OperationCanceledException("Job lease no longer owned",Token);}var row=rows[0];await db.Entry(row).ReloadAsync(ct);row.Status=status;row.LastErrorCode=error;row.NextRunAt=next;row.LeaseOwner=null;row.LeaseExpiresAt=null;
        var attempt=await db.Set<JobLeaseAttempt>().SingleOrDefaultAsync(a=>a.JobId==Job.Id && a.LeaseOwner==Job.LeaseOwner && a.Status=="Running",ct);
        if(attempt!=null){attempt.Status=status;attempt.ErrorCode=error;attempt.FinishedAt=DateTimeOffset.UtcNow;}
        await db.SaveChangesAsync(ct);
    }
    public async ValueTask DisposeAsync(){stop.Cancel();await heartbeat;try{await JobCancellation.ObserveStopped(owner,Job);}finally{work.Dispose();stop.Dispose();}}
}
