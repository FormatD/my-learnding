using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace Learning;
public class AssessmentConsumerCursor:Row
{
    public Guid StudentId {get;set;}
    public string ConsumerName {get;set;}=ProjectionJobs.Consumer;
    public long LastEventSequence {get;set;}
    public Guid LastEventId {get;set;}
    public Guid LastReceiptId {get;set;}
    public Guid LastAppliedEventId {get;set;}
    public Guid GenerationId {get;set;}
    public string InputHash {get;set;}="";
    public DateTimeOffset UpdatedAt {get;set;}
}
public record ConsumptionWindow(AssessmentConsumerCursor? Cursor,DomainEvent[] Events,Guid[] NewEventIds);
public record ConsumptionAdvance(string Version,long? PriorSequence,long ThroughSequence,Guid LastEventId,Guid LastReceiptId,Guid[] VerifiedEventIds,Guid[] VerifiedReceiptIds,Guid[] NewEventIds,Guid[] LegacyEventIds);
public record ConsumptionStatus(AssessmentConsumerCursor? Cursor,int PendingModern,int PendingLegacy,int LegacyWithoutReceipt);
public static class AssessmentConsumption
{
    static ApiError Gap()=>new(422,"ASSESSMENT_CONSUMPTION_GAP","顺序消费记录存在缺口，请核对原事件与回执，不能跳过缺失记录推进。");
    static ApiError Invalid()=>new(422,"ASSESSMENT_CURSOR_INVALID","消费进度与实际应用记录不一致，请核对记录后处理。");
    public static async Task ValidateReceipt(Database db,Outbox source,ConsumerReceipt receipt,CancellationToken ct)
    {
        await DomainEvents.Validate(db,source,ct);
        if(receipt.ConsumerName!=ProjectionJobs.Consumer || receipt.FamilyId!=source.FamilyId || receipt.StudentId!=source.StudentId || receipt.EventId!=source.Id || receipt.DomainEventId!=source.DomainEventId || !await db.Generations.AnyAsync(g=>g.Id==receipt.GenerationId && g.FamilyId==source.FamilyId && g.StudentId==source.StudentId,ct) || !await db.Set<BackgroundJob>().AnyAsync(j=>j.Id==receipt.JobId && j.FamilyId==source.FamilyId && j.StudentId==source.StudentId,ct))throw Gap();
        if(receipt.CheckpointId is Guid checkpoint)
        {
            if(!await db.Set<AssessmentCheckpoint>().AnyAsync(c=>c.Id==checkpoint && c.FamilyId==receipt.FamilyId && c.StudentId==receipt.StudentId && c.GenerationId==receipt.GenerationId && c.InputHash==receipt.InputHash,ct))throw Gap();
        }
        else if(source.DomainEventId!=null)
        {
            var applications=await db.Set<DomainEvent>().AsNoTracking().Where(e=>e.FamilyId==source.FamilyId && e.StudentId==source.StudentId && e.AggregateId==receipt.GenerationId && e.EventType=="AssessmentApplied").ToArrayAsync(ct);
            if(!applications.Any(e=>Matches(e,receipt)))throw Gap();
        }
    }
    static bool Matches(DomainEvent e,ConsumerReceipt receipt)
    {
        if(Content.Hash(e.Payload)!=e.PayloadHash)return false;
        try{using var doc=JsonDocument.Parse(e.Payload);var data=doc.RootElement.GetProperty("data");return data.GetProperty("jobId").GetGuid()==receipt.JobId && data.GetProperty("generationId").GetGuid()==receipt.GenerationId && data.GetProperty("inputHash").GetString()==receipt.InputHash && data.GetProperty("outboxIds").EnumerateArray().Any(x=>x.GetGuid()==receipt.EventId);}
        catch(Exception ex)when(ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){return false;}
    }
    public static async Task<ConsumptionWindow?> Prepare(Database db,Student student,Outbox[] pending,CancellationToken ct)
    {
        var cursor=await db.Set<AssessmentConsumerCursor>().SingleOrDefaultAsync(c=>c.FamilyId==student.FamilyId && c.StudentId==student.Id && c.ConsumerName==ProjectionJobs.Consumer,ct);
        if(cursor!=null)
        {
            var ev=await db.Set<DomainEvent>().AsNoTracking().SingleOrDefaultAsync(e=>e.Id==cursor.LastAppliedEventId && e.FamilyId==student.FamilyId && e.StudentId==student.Id && e.EventType=="AssessmentApplied" && e.AggregateId==cursor.GenerationId,ct);
            if(ev==null || Content.Hash(ev.Payload)!=ev.PayloadHash)throw Invalid();
            try{using var doc=JsonDocument.Parse(ev.Payload);var data=doc.RootElement.GetProperty("data");var applied=Json.Read<ConsumptionAdvance>(data.GetProperty("consumption").GetRawText());if(applied.Version!="assessment-consumption/1" || applied.ThroughSequence!=cursor.LastEventSequence || applied.LastEventId!=cursor.LastEventId || applied.LastReceiptId!=cursor.LastReceiptId || data.GetProperty("inputHash").GetString()!=cursor.InputHash)throw Invalid();}
            catch(Exception ex)when(ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){throw Invalid();}
            var original=await db.Outbox.SingleOrDefaultAsync(o=>o.Id==cursor.LastEventId && o.FamilyId==student.FamilyId && o.StudentId==student.Id,ct);
            var receipt=await db.Set<ConsumerReceipt>().SingleOrDefaultAsync(r=>r.Id==cursor.LastReceiptId && r.FamilyId==student.FamilyId && r.StudentId==student.Id,ct);
            if(original==null || receipt==null)throw Invalid();await ValidateReceipt(db,original,receipt,ct);
        }
        var modernIds=pending.Where(o=>o.DomainEventId!=null).Select(o=>o.Id).ToArray();
        var known=(await db.Set<ConsumerReceipt>().Where(r=>r.FamilyId==student.FamilyId && r.StudentId==student.Id && r.ConsumerName==ProjectionJobs.Consumer && modernIds.Contains(r.EventId)).Select(r=>r.EventId).ToArrayAsync(ct)).ToHashSet();
        var newIds=modernIds.Where(id=>!known.Contains(id)).ToArray();if(newIds.Length==0)return null;
        var fresh=await db.Set<DomainEvent>().Where(e=>e.FamilyId==student.FamilyId && e.StudentId==student.Id && newIds.Contains(e.Id) && e.DispatchTarget==ProjectionJobs.Consumer).ToArrayAsync(ct);
        if(fresh.Length!=newIds.Length || fresh.Any(e=>e.EventSequence<=(cursor?.LastEventSequence??0)))throw Gap();
        var fence=fresh.Max(e=>e.EventSequence);var events=await db.Set<DomainEvent>().Where(e=>e.FamilyId==student.FamilyId && e.StudentId==student.Id && e.DispatchTarget==ProjectionJobs.Consumer && e.EventSequence>(cursor==null?0:cursor.LastEventSequence) && e.EventSequence<=fence).OrderBy(e=>e.EventSequence).ToArrayAsync(ct);
        var eventIds=events.Select(e=>e.Id).ToArray();var outbox=await db.Outbox.Where(o=>o.FamilyId==student.FamilyId && o.StudentId==student.Id && eventIds.Contains(o.Id)).ToDictionaryAsync(o=>o.Id,ct);var receipts=await db.Set<ConsumerReceipt>().Where(r=>r.FamilyId==student.FamilyId && r.StudentId==student.Id && r.ConsumerName==ProjectionJobs.Consumer && eventIds.Contains(r.EventId)).ToDictionaryAsync(r=>r.EventId,ct);
        foreach(var e in events)
        {
            if(!outbox.TryGetValue(e.Id,out var source) || source.DomainEventId!=e.Id)throw Gap();await DomainEvents.Validate(db,source,ct);
            if(!newIds.Contains(e.Id)){if(!receipts.TryGetValue(e.Id,out var receipt))throw Gap();await ValidateReceipt(db,source,receipt,ct);}
        }
        return new(cursor,events,newIds);
    }
    public static async Task<ConsumptionAdvance?> Describe(Database db,ConsumptionWindow? window,Student student,Outbox[] pending,CancellationToken ct)
    {
        if(window==null)return null;var ids=window.Events.Select(e=>e.Id).ToArray();var receipts=await db.Set<ConsumerReceipt>().Where(r=>r.FamilyId==student.FamilyId && r.StudentId==student.Id && r.ConsumerName==ProjectionJobs.Consumer && ids.Contains(r.EventId)).ToDictionaryAsync(r=>r.EventId,ct);
        foreach(var r in db.Set<ConsumerReceipt>().Local.Where(r=>r.FamilyId==student.FamilyId && r.StudentId==student.Id && ids.Contains(r.EventId)))receipts[r.EventId]=r;
        if(receipts.Count!=ids.Length)throw Gap();var last=window.Events[^1];return new("assessment-consumption/1",window.Cursor?.LastEventSequence,last.EventSequence,last.Id,receipts[last.Id].Id,ids,ids.Select(id=>receipts[id].Id).ToArray(),window.NewEventIds,pending.Where(o=>o.DomainEventId==null).Select(o=>o.Id).ToArray());
    }
    public static void Commit(Database db,Student student,AssessmentApplication application,DomainEvent applied)
    {
        var advance=application.Consumption;if(advance==null)return;var cursor=application.Window?.Cursor;
        if(cursor==null){cursor=new AssessmentConsumerCursor{FamilyId=student.FamilyId,StudentId=student.Id};db.Add(cursor);}
        cursor.LastEventSequence=advance.ThroughSequence;cursor.LastEventId=advance.LastEventId;cursor.LastReceiptId=advance.LastReceiptId;cursor.LastAppliedEventId=applied.Id;cursor.GenerationId=application.Generation.Id;cursor.InputHash=application.Generation.InputHash;cursor.UpdatedAt=DateTimeOffset.UtcNow;
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/students/{id:guid}/assessment-consumption",async(Guid id,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);await a.Student(db,id);var cursor=await db.Set<AssessmentConsumerCursor>().SingleOrDefaultAsync(c=>c.FamilyId==a.FamilyId && c.StudentId==id && c.ConsumerName==ProjectionJobs.Consumer);var pending=await db.Outbox.CountAsync(o=>o.FamilyId==a.FamilyId && o.StudentId==id && o.DomainEventId!=null && o.ProcessedAt==null);var legacy=await db.Outbox.CountAsync(o=>o.FamilyId==a.FamilyId && o.StudentId==id && o.DomainEventId==null && o.ProcessedAt==null);var unknown=await db.Outbox.CountAsync(o=>o.FamilyId==a.FamilyId && o.StudentId==id && o.DomainEventId==null && !db.Set<ConsumerReceipt>().Any(r=>r.EventId==o.Id && r.ConsumerName==ProjectionJobs.Consumer));await tx.CommitAsync();return new ConsumptionStatus(cursor,pending,legacy,unknown);});
    }
}
