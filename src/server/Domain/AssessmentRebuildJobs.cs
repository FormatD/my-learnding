using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace Learning;
public class AssessmentRebuildRequest:Row
{
    public Guid StudentId {get;set;}
    public Guid JobId {get;set;}
    public Guid RequestedBy {get;set;}
    public Guid TargetGenerationId {get;set;}
    public string Snapshot {get;set;}="";
    public string SnapshotHash {get;set;}="";
    public string Reason {get;set;}="";
}
public class AssessmentRebuildResult:Row
{
    public Guid? CheckpointId {get;set;}
    public Guid StudentId {get;set;}
    public Guid RequestId {get;set;}
    public Guid JobId {get;set;}
    public Guid GenerationId {get;set;}
    public Guid AppliedEventId {get;set;}
    public string InputHash {get;set;}="";
    public long Cursor {get;set;}
    public bool ReusedGeneration {get;set;}
}
public record RebuildDescriptor(string Version,string InputMode,Guid FamilyId,Guid StudentId,Guid RequestedBy,Guid TargetGenerationId,string TimeZone,string InputVersion,string EvidenceRule,string MasteryModel,string ReviewRule,Guid? BaseGenerationId,string? BaseInputHash,long? BaseCursor,bool ForceFull=false);
public record AssessmentRebuildSummary(Guid Id,Guid JobId,Guid StudentId,Guid TargetGenerationId,string Status,int AttemptCount,int RetryRound,string? Error,DateTimeOffset CreatedAt,AssessmentRebuildResult? Result);
public static class AssessmentRebuildJobs
{
    public const string Type="AssessmentRebuild";
    public static async Task RequireLegacyCompatible(Database db,Student student,CancellationToken ct=default)
    {
        // An old synchronous call cannot restart work stopped through the explicit job controls.
        var request=await db.Set<AssessmentRebuildRequest>().AsNoTracking().Where(r=>r.FamilyId==student.FamilyId && r.StudentId==student.Id).OrderByDescending(r=>r.CreatedAt).ThenByDescending(r=>r.Id).FirstOrDefaultAsync(ct);
        if(request==null)return;
        if(await db.Set<BackgroundJob>().AnyAsync(j=>j.FamilyId==student.FamilyId && j.StudentId==student.Id && j.Type==Type && (j.Status=="Queued" || j.Status=="Running" || j.Status=="Retrying"),ct))throw LegacyRequired();
        var job=await db.Set<BackgroundJob>().AsNoTracking().SingleOrDefaultAsync(j=>j.Id==request.JobId && j.FamilyId==student.FamilyId && j.StudentId==student.Id && j.Type==Type,ct);
        if(job?.Status!="Succeeded")throw LegacyRequired();
        var result=await db.Set<AssessmentRebuildResult>().AsNoTracking().SingleOrDefaultAsync(r=>r.RequestId==request.Id && r.FamilyId==student.FamilyId && r.StudentId==student.Id,ct);
        if(result==null || result.JobId!=job.Id || !await AssessmentCheckpoints.ValidateResult(db,result,ct) || !await db.Set<DomainEvent>().AnyAsync(e=>e.Id==result.AppliedEventId && e.FamilyId==student.FamilyId && e.StudentId==student.Id && e.AggregateId==result.GenerationId && e.EventType=="AssessmentApplied",ct))throw new ApiError(422,"REBUILD_RESULT_INVALID","原重建结果引用不完整，请核对记录。");
    }
    static ApiError LegacyRequired()=>new(409,"REBUILD_JOB_REQUIRED","已有后台重建请求，请查看原任务进度；已失败或取消时须明确恢复或重新准备，不能通过旧入口重算。");
    public static AssessmentRebuildSummary Summary(AssessmentRebuildRequest r,BackgroundJob j,AssessmentRebuildResult? result)=>new(r.Id,j.Id,r.StudentId,r.TargetGenerationId,j.Status,j.AttemptCount,j.RetryRound,j.LastErrorCode,r.CreatedAt,result);
    public static async Task<AssessmentRebuildRequest> Enqueue(Database db,Actor a,Guid studentId,string reason,bool forceFull=false)
    {
        a.Require("Parent");if(string.IsNullOrWhiteSpace(reason) || reason.Length>4000)throw new ApiError(422,"REASON_REQUIRED","请填写重新评估的依据，最多4000字。");var student=await a.Student(db,studentId);
        var active=await(from queued in db.Set<AssessmentRebuildRequest>() join j in db.Set<BackgroundJob>() on queued.JobId equals j.Id where queued.FamilyId==a.FamilyId && queued.StudentId==studentId && (j.Status=="Queued" || j.Status=="Running" || j.Status=="Retrying") select queued).SingleOrDefaultAsync();if(active!=null){if(forceFull && !Json.Read<RebuildDescriptor>(active.Snapshot).ForceFull)throw new ApiError(409,"REBUILD_ACTIVE_REQUEST_EXISTS","当前已有重建在处理，请先取消原请求再准备完整重建。");return active;}
        var original=student.ActiveGenerationId==null?null:await db.Generations.SingleOrDefaultAsync(g=>g.Id==student.ActiveGenerationId && g.FamilyId==a.FamilyId && g.StudentId==studentId);
        var r=new AssessmentRebuildRequest{FamilyId=a.FamilyId,StudentId=studentId,RequestedBy=a.Id,TargetGenerationId=Guid.NewGuid(),Reason=reason.Trim()};
        r.Snapshot=Json.Write(new RebuildDescriptor("assessment-rebuild/1","LatestCommittedUnderLock",a.FamilyId,studentId,a.Id,r.TargetGenerationId,student.TimeZone,Assessment.InputHashVersion,Assessment.EvidenceRuleVersion,Assessment.MasteryModelVersion,Assessment.ReviewRuleVersion,original?.Id,original?.InputHash,original?.Cursor,forceFull));r.SnapshotHash=Content.Hash(r.Snapshot);
        var job=new BackgroundJob{FamilyId=a.FamilyId,StudentId=studentId,TargetGenerationId=r.TargetGenerationId,Type=Type,InputRef=r.Id,IdempotencyKey="rebuild:"+r.Id,InputPayload=r.Snapshot,InputHash=r.SnapshotHash};r.JobId=job.Id;db.AddRange(r,job);db.Audits.Add(new(){FamilyId=a.FamilyId,StudentId=studentId,ActorId=a.Id,Action="AssessmentRebuildQueued",Details=Json.Write(new{requestId=r.Id,r.JobId,r.TargetGenerationId,r.SnapshotHash,r.Reason,forceFull})});return r;
    }
    public static async Task ProcessOne(Database db,CancellationToken stopping=default)
    {
        db.ChangeTracker.Clear(); // A worker must read committed inputs, not entities tracked by an earlier job.
        await using var lease=await BackgroundJobs.Claim(db,stopping,[Type]);if(lease==null)return;var ct=lease.Token;
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Lock(lease.Job.FamilyId,ct);await tx.CreateSavepointAsync("rebuild_work",ct);
            try
            {
                var r=await db.Set<AssessmentRebuildRequest>().SingleOrDefaultAsync(r=>r.Id==lease.Job.InputRef && r.FamilyId==lease.Job.FamilyId,ct)??throw new ApiError(422,"JOB_INPUT_MISSING","原重建请求不存在。");
                if(r.JobId!=lease.Job.Id || r.StudentId!=lease.Job.StudentId || r.TargetGenerationId!=lease.Job.TargetGenerationId || r.Snapshot!=lease.Job.InputPayload || r.SnapshotHash!=lease.Job.InputHash || Content.Hash(r.Snapshot)!=r.SnapshotHash)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","原重建请求与后台输入不一致。");
                var existing=await db.Set<AssessmentRebuildResult>().SingleOrDefaultAsync(x=>x.RequestId==r.Id && x.FamilyId==r.FamilyId,ct);
                if(existing!=null){if(existing.StudentId!=r.StudentId || existing.JobId!=r.JobId || !await AssessmentCheckpoints.ValidateResult(db,existing,ct) || !await db.Set<DomainEvent>().AnyAsync(e=>e.Id==existing.AppliedEventId && e.FamilyId==r.FamilyId && e.StudentId==r.StudentId && e.AggregateId==existing.GenerationId && e.EventType=="AssessmentApplied",ct))throw new ApiError(422,"REBUILD_RESULT_INVALID","原重建结果引用不完整，请核对记录。");await lease.Finish(db,"Succeeded",null,null,ct);await tx.CommitAsync(ct);return;}
                if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","重建任务多次中断后已停止，请核对原请求后人工恢复。");
                var f=Json.Read<RebuildDescriptor>(r.Snapshot);if(f.Version!="assessment-rebuild/1" || f.InputMode!="LatestCommittedUnderLock" || f.FamilyId!=r.FamilyId || f.StudentId!=r.StudentId || f.RequestedBy!=r.RequestedBy || f.TargetGenerationId!=r.TargetGenerationId || f.InputVersion!=Assessment.InputHashVersion || f.EvidenceRule!=Assessment.EvidenceRuleVersion || f.MasteryModel!=Assessment.MasteryModelVersion || f.ReviewRule!=Assessment.ReviewRuleVersion)throw new ApiError(422,"REBUILD_RULE_CHANGED","原评估规则已变化，请重新准备重建请求。");
                var authorizedBy=r.RequestedBy;var authorized=lease.Job.RetryRound==0;
                if(lease.Job.RetryRound>0)
                {
                    foreach(var audit in await db.Audits.AsNoTracking().Where(a=>a.FamilyId==r.FamilyId && a.Action=="AssessmentRebuildManualRetry" && a.Details.Contains(r.Id.ToString())).OrderByDescending(a=>a.CreatedAt).ToArrayAsync(ct))
                    {
                        using var doc=JsonDocument.Parse(audit.Details);var p=doc.RootElement;
                        if(p.GetProperty("requestId").GetGuid()==r.Id && p.GetProperty("jobId").GetGuid()==r.JobId && p.GetProperty("retryRound").GetInt32()==lease.Job.RetryRound && p.GetProperty("targetGenerationId").GetGuid()==r.TargetGenerationId && p.GetProperty("snapshotHash").GetString()==r.SnapshotHash){authorizedBy=audit.ActorId;authorized=true;break;}
                    }
                }
                if(!authorized)throw new ApiError(422,"JOB_RETRY_AUTHORIZATION_MISSING","缺少本轮人工恢复依据，请核对后台任务记录。");
                var roles=(await db.Set<FamilyMembership>().AsNoTracking().SingleOrDefaultAsync(m=>m.FamilyId==r.FamilyId && m.AccountId==authorizedBy,ct))?.Roles??"";
                if(!new Actor(Guid.Empty,r.FamilyId,authorizedBy,null,"Parent",roles).Can("Parent"))throw new ApiError(422,"JOB_REQUESTER_FORBIDDEN","原家长权限已变化，请由有权限家长核对原请求后重新处理。");
                var student=await db.Students.SingleAsync(s=>s.Id==r.StudentId && s.FamilyId==r.FamilyId,ct);if(student.TimeZone!=f.TimeZone)throw new ApiError(422,"REBUILD_STUDENT_CONFIG_CHANGED","学生时区已变化，请重新准备重建请求。");
                var before=student.ActiveGenerationId;var applied=await AssessmentProjection.Apply(db,student,r.TargetGenerationId,lease.Job.Id,ct,f.ForceFull);var g=applied.Generation;
                var ev=await DomainEvents.Append(db,r.FamilyId,r.StudentId,g.Id,"Generation","AssessmentApplied",new{consumption=applied.Consumption,origin="ParentRebuildJob",forceFull=f.ForceFull,requestId=r.Id,jobId=lease.Job.Id,targetGenerationId=r.TargetGenerationId,checkpointId=applied.Checkpoint?.Id,generationId=g.Id,g.InputHash,g.InputVersion,g.CalculationMode,g.ProcessedInputCount,g.IncrementalBaseGenerationId,g.Cursor,g.RuleVersion,g.ModelVersion,reusedGeneration=before==g.Id,originalRequestedBy=r.RequestedBy,authorizedBy,outboxIds=applied.OutboxIds,domainEventIds=applied.DomainEventIds},ct:ct);
                AssessmentConsumption.Commit(db,student,applied,ev);
                db.Add(new AssessmentRebuildResult{FamilyId=r.FamilyId,StudentId=r.StudentId,RequestId=r.Id,JobId=lease.Job.Id,GenerationId=g.Id,CheckpointId=applied.Checkpoint?.Id,AppliedEventId=ev.Id,InputHash=g.InputHash,Cursor=g.Cursor,ReusedGeneration=before==g.Id});
                await lease.Finish(db,"Succeeded",null,null,ct);await tx.CommitAsync(ct);
            }
            catch(Exception ex)when(ex is not OperationCanceledException)
            {
                await tx.RollbackToSavepointAsync("rebuild_work",ct);db.ChangeTracker.Clear();var retry=ex is not ApiError && lease.Job.AttemptCount<lease.Job.MaxAttempts;var code=ex is ApiError a?a.Code:"REBUILD_PROCESSING_FAILED";await lease.Finish(db,retry?"Retrying":"Failed",code,retry?DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2,lease.Job.AttemptCount)):null,ct);await tx.CommitAsync(ct);
            }
        }
        catch(OperationCanceledException)when(lease.Lost && !stopping.IsCancellationRequested){db.ChangeTracker.Clear();}
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/students/{id:guid}/mastery:rebuild",async(Guid id,ReasonInput input,Database db,HttpContext ctx)=>{var r=await Enqueue(db,ctx.Actor(),id,input.Reason);var j=db.Set<BackgroundJob>().Local.FirstOrDefault(j=>j.Id==r.JobId)??await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==r.JobId && j.FamilyId==r.FamilyId);return TypedResults.Accepted("/api/v1/students/"+id+"/rebuild-requests/"+r.Id,Summary(r,j,null));});
        api.MapPost("/students/{id:guid}/mastery:full-rebuild",async(Guid id,ReasonInput input,Database db,HttpContext ctx)=>{var r=await Enqueue(db,ctx.Actor(),id,input.Reason,true);var j=db.Set<BackgroundJob>().Local.FirstOrDefault(j=>j.Id==r.JobId)??await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==r.JobId && j.FamilyId==r.FamilyId);return TypedResults.Accepted("/api/v1/students/"+id+"/rebuild-requests/"+r.Id,Summary(r,j,null));});
        api.MapGet("/students/{id:guid}/rebuild-requests",async(Guid id,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);var rows=await(from r in db.Set<AssessmentRebuildRequest>() join j in db.Set<BackgroundJob>() on r.JobId equals j.Id where r.FamilyId==a.FamilyId && r.StudentId==id orderby r.CreatedAt descending,r.Id select new{r,j}).Take(50).ToArrayAsync();var ids=rows.Select(x=>x.r.Id).ToArray();var results=await db.Set<AssessmentRebuildResult>().Where(r=>r.FamilyId==a.FamilyId && r.StudentId==id && ids.Contains(r.RequestId)).ToDictionaryAsync(r=>r.RequestId);return rows.Select(x=>Summary(x.r,x.j,results.GetValueOrDefault(x.r.Id))).ToArray();});
        api.MapGet("/students/{id:guid}/rebuild-requests/{requestId:guid}",async(Guid id,Guid requestId,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);var r=await db.Set<AssessmentRebuildRequest>().SingleOrDefaultAsync(r=>r.Id==requestId && r.FamilyId==a.FamilyId && r.StudentId==id)??throw new ApiError(404,"NOT_FOUND","重建请求不存在。");return Summary(r,await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==r.JobId && j.FamilyId==a.FamilyId),await db.Set<AssessmentRebuildResult>().SingleOrDefaultAsync(x=>x.RequestId==r.Id && x.FamilyId==a.FamilyId));});
        api.MapPost("/students/{id:guid}/rebuild-requests/{requestId:guid}:retry",async(Guid id,Guid requestId,ReasonInput input,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);if(string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>4000)throw new ApiError(422,"REASON_REQUIRED","请填写恢复依据，最多4000字。");var r=await db.Set<AssessmentRebuildRequest>().SingleOrDefaultAsync(r=>r.Id==requestId && r.StudentId==id && r.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","重建请求不存在。");var j=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==r.JobId && j.FamilyId==a.FamilyId);if(j.Status is not ("Failed" or "Cancelled") || await db.Set<AssessmentRebuildResult>().AnyAsync(x=>x.RequestId==r.Id))throw new ApiError(409,"RUN_NOT_FAILED","已有结果或未停止的重建无需再次恢复。");if(j.LastErrorCode is not ("REBUILD_PROCESSING_FAILED" or "JOB_ATTEMPTS_EXHAUSTED" or "JOB_REQUESTER_FORBIDDEN" or "JOB_CANCELLED_BY_USER"))throw new ApiError(422,"RUN_RECREATE_REQUIRED","原请求需要核对规则或学生设置，请重新准备。");j.Status="Queued";j.AttemptCount=0;j.RetryRound++;j.NextRunAt=null;j.LastErrorCode=null;db.Audits.Add(new(){FamilyId=a.FamilyId,StudentId=id,ActorId=a.Id,Action="AssessmentRebuildManualRetry",Details=Json.Write(new{requestId=r.Id,jobId=r.JobId,retryRound=j.RetryRound,targetGenerationId=r.TargetGenerationId,snapshotHash=r.SnapshotHash,reason=input.Reason.Trim()})});return TypedResults.Accepted("/api/v1/students/"+id+"/rebuild-requests/"+r.Id,Summary(r,j,null));});
    }
}
