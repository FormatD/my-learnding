using Microsoft.EntityFrameworkCore;
namespace Learning;
public record AssessmentApplication(Generation Generation,Guid[] OutboxIds,Guid[] DomainEventIds);
public static class AssessmentProjection
{
    public static async Task<AssessmentApplication> Apply(Database db,Student student,Guid target,Guid jobId,CancellationToken ct)
    {
        var all=await db.Outbox.Where(o=>o.FamilyId==student.FamilyId && o.StudentId==student.Id && o.ProcessedAt==null).ToArrayAsync(ct);
        foreach(var item in all)await DomainEvents.Validate(db,item,ct);
        await Assessment.Rebuild(db,student,ct,target);
        var generation=await db.Generations.SingleAsync(g=>g.Id==student.ActiveGenerationId && g.FamilyId==student.FamilyId && g.StudentId==student.Id,ct);
        var ids=all.Select(o=>o.Id).ToArray();var existing=(await db.Set<ConsumerReceipt>().Where(r=>r.FamilyId==student.FamilyId && r.ConsumerName==ProjectionJobs.Consumer && ids.Contains(r.EventId)).Select(r=>r.EventId).ToArrayAsync(ct)).ToHashSet();
        foreach(var item in all){item.ProcessedAt=DateTimeOffset.UtcNow;if(!existing.Contains(item.Id))db.Add(new ConsumerReceipt{FamilyId=student.FamilyId,StudentId=student.Id,EventId=item.Id,DomainEventId=item.DomainEventId,JobId=jobId,GenerationId=generation.Id,InputHash=generation.InputHash});}
        return new(generation,ids,all.Where(o=>o.DomainEventId!=null).Select(o=>o.DomainEventId!.Value).ToArray());
    }
}
