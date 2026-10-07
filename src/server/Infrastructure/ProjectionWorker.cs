using Microsoft.EntityFrameworkCore;

namespace Learning;
public class ProjectionWorker(IServiceScopeFactory scopes, ILogger<ProjectionWorker> logger) : BackgroundService
{
    public static async Task<bool> Consume(Database db,Outbox pending,CancellationToken ct=default)
    {
        await ProjectionJobs.Ensure(db,pending.Id,ct);await using var lease=await BackgroundJobs.Claim(db,ct,["AssessmentProjection"],pending.Id);
        return lease!=null && await ConsumeClaim(db,lease,ct);
    }
    public static async Task<bool> ProcessOne(Database db,CancellationToken ct=default)
    {
        await ProjectionJobs.Ensure(db,ct:ct);await using var lease=await BackgroundJobs.Claim(db,ct,["AssessmentProjection"]);
        return lease!=null && await ConsumeClaim(db,lease,ct);
    }
    static async Task<bool> ConsumeClaim(Database db,JobLease lease,CancellationToken stopping)
    {
        var ct=lease.Token;
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Lock(lease.Job.FamilyId,ct);
            var pending=await db.Outbox.SingleOrDefaultAsync(o=>o.Id==lease.Job.InputRef && o.FamilyId==lease.Job.FamilyId,ct);
            if(pending==null){await lease.Finish(db,"Failed","PROJECTION_EVENT_MISSING",null,ct);await tx.CommitAsync(ct);return false;}
            await db.Entry(pending).ReloadAsync(ct);
            var receipt=await db.Set<ConsumerReceipt>().SingleOrDefaultAsync(r=>r.FamilyId==pending.FamilyId && r.ConsumerName==ProjectionJobs.Consumer && r.EventId==pending.Id,ct);
            if(receipt!=null)
            {
                try{await AssessmentConsumption.ValidateReceipt(db,pending,receipt,ct);}
                catch(ApiError error){pending.Retries=Math.Min(3,pending.Retries+1);pending.Error=error.Code;pending.NextAttemptAt=null;await lease.Finish(db,"Failed",error.Code,null,ct);await tx.CommitAsync(ct);throw;}
                pending.ProcessedAt??=receipt.CreatedAt;await lease.Finish(db,"Succeeded",null,null,ct);await tx.CommitAsync(ct);return false;
            }
            if(pending.ProcessedAt!=null){await lease.Finish(db,"Failed","LEGACY_PROCESSED_WITHOUT_RECEIPT",null,ct);await tx.CommitAsync(ct);return false;}
            if(pending.Retries>=3){await lease.Finish(db,"Failed",pending.Error??"PROJECTION_FAILED",null,ct);await tx.CommitAsync(ct);return false;}
            if(pending.NextAttemptAt>DateTimeOffset.UtcNow){await lease.Finish(db,"Retrying",pending.Error,pending.NextAttemptAt,ct);await tx.CommitAsync(ct);return false;}
            var eventId=pending.Id;await tx.CreateSavepointAsync("projection_work",ct);
            try
            {
                if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","评估任务多次中断后已停止，请核对原记录再人工恢复。");
                if(lease.Job.StudentId!=pending.StudentId || lease.Job.TargetGenerationId==null || Content.Hash(lease.Job.InputPayload)!=lease.Job.InputHash || ProjectionJobs.Snapshot(pending)!=lease.Job.InputPayload)throw new ApiError(422,"PROJECTION_INPUT_CHANGED","评估任务原事件与学生引用不一致。");
                await DomainEvents.Validate(db,pending,ct);
                var student=await db.Students.SingleAsync(s=>s.Id==pending.StudentId && s.FamilyId==pending.FamilyId,ct);
                // Catch-up deliberately reads the latest committed student inputs under the family lock.
                // The target identity survives retries; new events are receipted only in this same result transaction.
                var applied=await AssessmentProjection.Apply(db,student,lease.Job.TargetGenerationId.Value,lease.Job.Id,ct,online:true);var generation=applied.Generation;
                var appliedEvent=await DomainEvents.Append(db,student.FamilyId,student.Id,generation.Id,"Generation","AssessmentApplied",new{consumption=applied.Consumption,jobId=lease.Job.Id,checkpointId=applied.Checkpoint?.Id,targetGenerationId=lease.Job.TargetGenerationId,generationId=generation.Id,generation.InputHash,generation.InputVersion,generation.CalculationMode,generation.ProcessedInputCount,generation.IncrementalBaseGenerationId,generation.Cursor,generation.RuleVersion,generation.ModelVersion,outboxIds=applied.OutboxIds,domainEventIds=applied.DomainEventIds},ct:ct);
                AssessmentConsumption.Commit(db,student,applied,appliedEvent);
                await lease.Finish(db,"Succeeded",null,null,ct);await tx.CommitAsync(ct);return true;
            }
            catch(Exception ex)when(ex is not OperationCanceledException)
            {
                await tx.RollbackToSavepointAsync("projection_work",ct);db.ChangeTracker.Clear();pending=await db.Outbox.SingleAsync(o=>o.Id==eventId && o.FamilyId==lease.Job.FamilyId,ct);
                pending.Retries=lease.CanExecute?Math.Min(3,pending.Retries+1):3;pending.Error=ex is ApiError a?a.Code:"PROJECTION_FAILED";pending.NextAttemptAt=pending.Retries<3?DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2,pending.Retries)):null;
                await lease.Finish(db,pending.Retries<3?"Retrying":"Failed",pending.Error,pending.NextAttemptAt,ct);await tx.CommitAsync(ct);throw;
            }
        }
        catch(OperationCanceledException)when(lease.Lost && !stopping.IsCancellationRequested){db.ChangeTracker.Clear();return false;}
    }
    protected override Task ExecuteAsync(CancellationToken ct)=>Task.WhenAll(RunLane(true,ct),RunLane(false,ct));
    async Task RunLane(bool learning,CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<Database>();
                if(learning)
                {
                    try{await ProcessOne(db,ct);}catch(Exception ex)when(ex is not OperationCanceledException){logger.LogError(ex,"Projection processing failed");db.ChangeTracker.Clear();}
                    db.ChangeTracker.Clear();await AssessmentRebuildJobs.ProcessOne(db,ct);
                }
                else
                {
                    await Builder.ProcessOne(db,ct,logger);
                    db.ChangeTracker.Clear();await MappingJobs.ProcessOne(db,ct);
                    db.ChangeTracker.Clear();await BuilderSemanticProcessing.ProcessOne(db,ct);
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Background worker lane failed {Learning}",learning);}
            try{await Task.Delay(500,ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        }
    }
}
