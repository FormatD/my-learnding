using Microsoft.EntityFrameworkCore;

namespace Learning;
public class ProjectionWorker(IServiceScopeFactory scopes, ILogger<ProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<Database>();
                var pending=await db.Outbox.Where(o => o.ProcessedAt==null && o.Retries<3 && (o.NextAttemptAt==null || o.NextAttemptAt<=DateTimeOffset.UtcNow)).OrderBy(o => o.CreatedAt).FirstOrDefaultAsync(ct);
                if (pending!=null)
                {
                    try
                    {
                        await using var tx=await db.Database.BeginTransactionAsync(ct); await db.Lock(pending.FamilyId,ct);
                        await db.Entry(pending).ReloadAsync(ct);
                        if (pending.ProcessedAt==null)
                        {
                            await Assessment.Rebuild(db,await db.Students.SingleAsync(s => s.Id==pending.StudentId,ct),ct);
                            var all=await db.Outbox.Where(o => o.StudentId==pending.StudentId && o.ProcessedAt==null).ToListAsync(ct);
                            foreach (var item in all) item.ProcessedAt=DateTimeOffset.UtcNow;
                            await db.SaveChangesAsync(ct);
                        }
                        await tx.CommitAsync(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex,"Projection failed {JobId}",pending.Id);
                        await using var failedScope=scopes.CreateAsyncScope();var failed=failedScope.ServiceProvider.GetRequiredService<Database>();
                        await using var retryTx=await failed.Database.BeginTransactionAsync(ct);await failed.Lock(pending.FamilyId,ct);
                        var job=await failed.Outbox.SingleOrDefaultAsync(o=>o.Id==pending.Id,ct);
                        if(job!=null && job.ProcessedAt==null){job.Retries++;job.Error="PROJECTION_FAILED";job.NextAttemptAt=DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2,job.Retries));await failed.SaveChangesAsync(ct);}
                        await retryTx.CommitAsync(ct);
                        db.ChangeTracker.Clear();
                    }
                }
                await Builder.ProcessOne(db,ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex,"Projection worker failed"); }
            await Task.Delay(500,ct);
        }
    }
}
