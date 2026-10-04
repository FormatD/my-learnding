using Microsoft.EntityFrameworkCore;
namespace Learning;
public class ConsumerReceipt:Row
{
    public Guid? CheckpointId {get;set;}
    public Guid? DomainEventId {get;set;}
    public Guid StudentId {get;set;}
    public string ConsumerName {get;set;}="assessment/1";
    public Guid EventId {get;set;}
    public Guid JobId {get;set;}
    public Guid GenerationId {get;set;}
    public string InputHash {get;set;}="";
}
public static class ProjectionJobs
{
    public const string Consumer="assessment/1";
    public static string Snapshot(Outbox row)=>row.DomainEventId==null?Json.Write(new{row.FamilyId,row.StudentId,row.AttemptId,eventId=row.Id,consumer=Consumer}):Json.Write(new{row.FamilyId,row.StudentId,row.AttemptId,eventId=row.Id,domainEventId=row.DomainEventId,consumer=Consumer});
    public static async Task Ensure(Database owner,Guid? selected=null,CancellationToken ct=default)
    {
        await using var db=BackgroundJobs.Open(owner);
        var rows=await db.Outbox.AsNoTracking().Where(o=>o.ProcessedAt==null && o.Retries<3 && (selected==null || o.Id==selected)).OrderBy(o=>o.CreatedAt).ThenBy(o=>o.Id).ToArrayAsync(ct);
        foreach(var row in rows)
        {
            var payload=Snapshot(row);var hash=Content.Hash(payload);var round=row.RetryRound??0;var key=Consumer+":"+row.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"BackgroundJob\" (\"Id\",\"FamilyId\",\"CreatedAt\",\"Type\",\"InputRef\",\"IdempotencyKey\",\"InputPayload\",\"InputHash\",\"Status\",\"AttemptCount\",\"MaxAttempts\",\"RetryRound\",\"NextRunAt\",\"LeaseSeconds\",\"HeartbeatSeconds\",\"StudentId\",\"TargetGenerationId\") VALUES ({Guid.NewGuid()},{row.FamilyId},{row.CreatedAt},'AssessmentProjection',{row.Id},{key},{payload},{hash},'Queued',0,3,{round},{row.NextAttemptAt},30,5,{row.StudentId},{Guid.NewGuid()}) ON CONFLICT (\"FamilyId\",\"Type\",\"InputRef\") DO NOTHING",ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BackgroundJob\" SET \"Status\"='Queued',\"AttemptCount\"=0,\"RetryRound\"={round},\"NextRunAt\"={row.NextAttemptAt},\"LeaseOwner\"=NULL,\"LeaseExpiresAt\"=NULL WHERE \"FamilyId\"={row.FamilyId} AND \"Type\"='AssessmentProjection' AND \"InputRef\"={row.Id} AND \"RetryRound\"<{round} AND \"Status\"<>'Running'",ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BackgroundJob\" SET \"NextRunAt\"={row.NextAttemptAt} WHERE \"FamilyId\"={row.FamilyId} AND \"Type\"='AssessmentProjection' AND \"InputRef\"={row.Id} AND \"Status\"='Retrying' AND \"RetryRound\"={round}",ct);
        }
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/students/{id:guid}/consumer-receipts/window",async(Guid id,int? pageSize,string? cursor,Database db,HttpContext ctx)=>{
            var actor=ctx.Actor();actor.Require("Parent");await using var snapshot=await ReadSnapshot.Begin(db,ctx);await actor.Student(db,id);
            var page=await StableReadPage.Load(db.Set<ConsumerReceipt>().AsNoTracking().Where(r=>r.FamilyId==actor.FamilyId&&r.StudentId==id),"consumer-receipts/1",actor.FamilyId,id,pageSize,cursor,ctx.RequestAborted);
            await snapshot.CommitAsync(ctx.RequestAborted);return new{page.PageSize,page.Total,receipts=page.Rows,nextCursor=page.NextCursor};
        }).WithMetadata(new OptionalResponseFieldsMetadata("nextCursor"));
        api.MapGet("/students/{id:guid}/consumer-receipts",async(Guid id,int? page,int? pageSize,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);var p=page??1;var size=pageSize??20;if(p<1 || p>100_000 || size<1 || size>50)throw new ApiError(422,"INVALID_PAGE","请使用有效页码及每页1～50条。");var query=db.Set<ConsumerReceipt>().Where(r=>r.FamilyId==a.FamilyId && r.StudentId==id);var total=await query.CountAsync();return new{page=p,pageSize=size,total,receipts=await query.OrderByDescending(r=>r.CreatedAt).ThenBy(r=>r.Id).Skip((p-1)*size).Take(size).ToArrayAsync()};});
    }
}
