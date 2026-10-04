using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace Learning;
public class DomainEvent:Row
{
    public Guid? StudentId {get;set;}
    public Guid AggregateId {get;set;}
    public string AggregateType {get;set;}="";
    public long EventSequence {get;set;}
    public string EventType {get;set;}="";
    public int PayloadVersion {get;set;}=1;
    public string Payload {get;set;}="";
    public string PayloadHash {get;set;}="";
    public DateTimeOffset OccurredAt {get;set;}
    public string? DispatchTarget {get;set;}
}
public static class DomainEvents
{
    public static readonly string[] Types=["AttemptSubmitted","GradingConfirmed","AssessmentApplied","CorrectionConfirmed","TaskTransitioned","ProgressChanged","ContentReleasePublished"];
    public static async Task<DomainEvent> Append(Database db,Guid family,Guid? student,Guid aggregate,string aggregateType,string type,object data,Guid? attempt=null,CancellationToken ct=default)
    {
        if(db.Database.CurrentTransaction==null || family==Guid.Empty || aggregate==Guid.Empty || !Types.Contains(type))throw new InvalidOperationException("Domain events require a valid domain transaction.");
        if(attempt!=null && (student==null || aggregate!=attempt || aggregateType!="Attempt" || type is not ("AttemptSubmitted" or "GradingConfirmed" or "CorrectionConfirmed")))throw new InvalidOperationException("Invalid assessment event route.");
        // Server sequence establishes append order, including events sharing a timestamp. Gaps after rollback are legitimate.
        var sequence=await db.Database.SqlQueryRaw<long>("SELECT nextval(pg_get_serial_sequence('\"DomainEvent\"','EventSequence')) AS \"Value\"").SingleAsync(ct);
        var now=DateTimeOffset.UtcNow;now=new DateTimeOffset(now.Ticks-now.Ticks%10,TimeSpan.Zero);
        var row=new DomainEvent{FamilyId=family,StudentId=student,AggregateId=aggregate,AggregateType=aggregateType,EventType=type,EventSequence=sequence,OccurredAt=now,CreatedAt=now,DispatchTarget=attempt==null?null:ProjectionJobs.Consumer};
        row.Payload=Json.Write(new{eventId=row.Id,familyId=family,studentId=student,aggregateId=aggregate,aggregateType,eventSequence=sequence,eventType=type,payloadVersion=row.PayloadVersion,occurredAt=now,data});row.PayloadHash=Content.Hash(row.Payload);db.Add(row);
        if(attempt!=null)db.Outbox.Add(new Outbox{Id=row.Id,FamilyId=family,StudentId=student!.Value,AttemptId=attempt.Value,DomainEventId=row.Id,CreatedAt=now});
        return row;
    }
    public static async Task Validate(Database db,Outbox outbox,CancellationToken ct)
    {
        if(outbox.DomainEventId==null)
        {
            // A missing link on a known modern event must never silently fall back to legacy compatibility.
            if(await db.Set<DomainEvent>().AnyAsync(e=>e.Id==outbox.Id,ct))throw Invalid();
            return;
        } // Historical events retain their original unknown type and payload.
        var row=await db.Set<DomainEvent>().SingleOrDefaultAsync(e=>e.Id==outbox.DomainEventId && e.FamilyId==outbox.FamilyId,ct);
        if(row==null || row.Id!=outbox.Id || row.StudentId!=outbox.StudentId || row.AggregateId!=outbox.AttemptId || row.AggregateType!="Attempt" || row.DispatchTarget!=ProjectionJobs.Consumer || row.PayloadVersion!=1 || row.EventSequence<=0 || row.EventType is not ("AttemptSubmitted" or "GradingConfirmed" or "CorrectionConfirmed") || Content.Hash(row.Payload)!=row.PayloadHash)throw Invalid();
        try
        {
            using var doc=JsonDocument.Parse(row.Payload);var p=doc.RootElement;
            if(p.GetProperty("eventId").GetGuid()!=row.Id || p.GetProperty("familyId").GetGuid()!=row.FamilyId || p.GetProperty("studentId").GetGuid()!=row.StudentId || p.GetProperty("aggregateId").GetGuid()!=row.AggregateId || p.GetProperty("aggregateType").GetString()!=row.AggregateType || p.GetProperty("eventSequence").GetInt64()!=row.EventSequence || p.GetProperty("eventType").GetString()!=row.EventType || p.GetProperty("payloadVersion").GetInt32()!=row.PayloadVersion || p.GetProperty("occurredAt").GetDateTimeOffset()!=row.OccurredAt || p.GetProperty("data").ValueKind!=JsonValueKind.Object)throw Invalid();
            var data=p.GetProperty("data");if(data.GetProperty("attemptId").GetGuid()!=outbox.AttemptId)throw Invalid();
            if(data.TryGetProperty("reviewTargetConfirmationId",out var confirmationId))
            {
                var id=confirmationId.GetGuid();var original=data.GetProperty("originalTargetId").GetGuid();var confirmed=data.GetProperty("confirmedTargetId").GetGuid();var action=data.GetProperty("action").GetString();var hash=data.GetProperty("measurementHash").GetString();
                if(row.EventType!="CorrectionConfirmed" || !await db.Set<ReviewTargetConfirmation>().AnyAsync(c=>c.Id==id && c.FamilyId==outbox.FamilyId && c.StudentId==outbox.StudentId && c.AttemptId==outbox.AttemptId && c.OriginalTargetId==original && c.ConfirmedTargetId==confirmed && c.Action==action && c.MeasurementHash==hash,ct))throw Invalid();
            }
            if(row.EventType is "AttemptSubmitted" or "GradingConfirmed")
            {
                var gradingId=data.GetProperty("gradingId").GetGuid();if(!await db.Gradings.AnyAsync(g=>g.Id==gradingId && g.FamilyId==outbox.FamilyId && g.AttemptId==outbox.AttemptId,ct))throw Invalid();
            }
        }
        catch(Exception ex)when(ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException){throw Invalid();}
    }
    static ApiError Invalid()=>new(422,"DOMAIN_EVENT_INVALID","事件原记录与评估输入不一致，请核对记录后处理。");
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/domain-events/window",async(Guid? studentId,int? pageSize,string? cursor,Database db,HttpContext ctx)=>{
            var actor=ctx.Actor();actor.Require("Parent");await using var snapshot=await ReadSnapshot.Begin(db,ctx);if(studentId!=null)await actor.Student(db,studentId.Value);
            var page=await StableReadPage.Load(db.Set<DomainEvent>().AsNoTracking().Where(e=>e.FamilyId==actor.FamilyId&&(studentId==null||e.StudentId==studentId)),"domain-events/1",actor.FamilyId,studentId,pageSize,cursor,ctx.RequestAborted);
            await snapshot.CommitAsync(ctx.RequestAborted);return new{page.PageSize,page.Total,events=page.Rows,nextCursor=page.NextCursor};
        }).WithMetadata(new OptionalResponseFieldsMetadata("nextCursor"));
        api.MapGet("/domain-events",async(Guid? studentId,int? page,int? pageSize,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");if(studentId!=null)await a.Student(db,studentId.Value);var p=page??1;var size=pageSize??20;if(p<1 || p>100000 || size<1 || size>50)throw new ApiError(422,"INVALID_PAGE","请使用有效页码及每页1～50条。");var q=db.Set<DomainEvent>().Where(e=>e.FamilyId==a.FamilyId && (studentId==null || e.StudentId==studentId));return new{page=p,pageSize=size,total=await q.CountAsync(),events=await q.OrderByDescending(e=>e.EventSequence).Skip((p-1)*size).Take(size).ToArrayAsync()};});
    }
}
