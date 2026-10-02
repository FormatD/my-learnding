using Microsoft.EntityFrameworkCore;
namespace Learning;
public class EvidenceRevocation : Row
{
    public Guid StudentId { get; set; }
    public Guid EvidenceId { get; set; }
    public Guid CorrectionBatchId { get; set; }
    public Guid ReplacementGenerationId { get; set; }
    public string Reason { get; set; } = "";
    public string Effect { get; set; } = "";
}
public record EvidenceRevocationPage(EvidenceRevocation[] Revocations,Evidence[] Evidence,CorrectionBatch[] Batches,int Total,int Offset,int Limit);
public static class EvidenceRevocations
{
    public static bool Equivalent(Evidence left,Evidence right)=>left.AttemptId==right.AttemptId && left.KCId==right.KCId && left.Part==right.Part && left.Positive==right.Positive && decimal.Round(left.RawWeight,6,MidpointRounding.AwayFromZero)==decimal.Round(right.RawWeight,6,MidpointRounding.AwayFromZero) && decimal.Round(left.Weight,6,MidpointRounding.AwayFromZero)==decimal.Round(right.Weight,6,MidpointRounding.AwayFromZero) && left.OccurredAt==right.OccurredAt;
    public static async Task Apply(Database db,Student student,Generation replacement,AssessmentOutput output,CancellationToken ct)
    {
        // Ordinary arrivals and same-result re-reviews retain the cause of an equivalent existing result.
        if(student.ActiveGenerationId is {} active && await db.Generations.AnyAsync(g=>g.Id==active && g.FamilyId==student.FamilyId && g.StudentId==student.Id && g.RuleVersion==replacement.RuleVersion && g.ModelVersion==replacement.ModelVersion,ct))
        {
            var prior=await db.Evidence.Where(e=>e.FamilyId==student.FamilyId && e.StudentId==student.Id && e.GenerationId==active).ToArrayAsync(ct);
            var priorContexts=await db.Set<AssessmentContext>().Where(c=>c.FamilyId==student.FamilyId && c.StudentId==student.Id && c.GenerationId==active).ToArrayAsync(ct);
            var priorByAttempt=prior.ToLookup(e=>e.AttemptId);var currentByAttempt=output.Evidence.ToLookup(e=>e.AttemptId);var contextByAttempt=priorContexts.ToDictionary(c=>c.AttemptId);
            foreach(var evidence in output.Evidence){var matches=priorByAttempt[evidence.AttemptId].Where(e=>Equivalent(e,evidence)).ToArray();if(matches.Length==1)evidence.CorrectionBatchId=matches[0].CorrectionBatchId;}
            foreach(var context in output.Contexts)
            {
                var previous=contextByAttempt.GetValueOrDefault(context.AttemptId);var before=priorByAttempt[context.AttemptId].ToArray();var after=currentByAttempt[context.AttemptId].ToArray();
                if(previous!=null && before.Length>0 && before.Length==after.Length && before.All(e=>after.Any(n=>Equivalent(e,n))))context.CorrectionBatchId=previous.CorrectionBatchId;
            }
        }
        // Retirement alone is not an educational error. Only newly confirmed corrections trigger this audit.
        var pending=await db.Set<CorrectionBatch>().Where(b=>b.FamilyId==student.FamilyId && b.StudentId==student.Id && b.Status=="Confirmed" && (b.Cause=="Mapping" || b.Cause=="Grading")).OrderBy(b=>b.CreatedAt).ThenBy(b=>b.Id).ToArrayAsync(ct);
        if(pending.Length==0)return;
        var referenced=output.Contexts.SelectMany(c=>new[]{c.GradingCorrectionBatchId,c.MappingCorrectionBatchId}).Where(id=>id!=null).Select(id=>id!.Value).ToHashSet();
        var effective=pending.Where(b=>referenced.Contains(b.Id)).ToArray();
        if(effective.Length>0)
        {
            var generations=await db.Generations.Where(g=>g.FamilyId==student.FamilyId && g.StudentId==student.Id && g.RuleVersion==replacement.RuleVersion && g.ModelVersion==replacement.ModelVersion).Select(g=>g.Id).ToArrayAsync(ct);
            var revoked=await db.Set<EvidenceRevocation>().Where(r=>r.FamilyId==student.FamilyId && r.StudentId==student.Id).Select(r=>r.EvidenceId).ToArrayAsync(ct);
            var old=await db.Evidence.Where(e=>e.FamilyId==student.FamilyId && e.StudentId==student.Id && generations.Contains(e.GenerationId) && !revoked.Contains(e.Id)).ToArrayAsync(ct);
            var current=output.Evidence.ToLookup(e=>e.AttemptId);
            var changedActive=old.Where(e=>e.GenerationId==student.ActiveGenerationId && !current[e.AttemptId].Any(n=>Equivalent(e,n))).ToLookup(e=>e.AttemptId);
            // Replay the actually adopted corrections in confirmation order. Exclude superseded inputs
            // throughout, and include new attempts in every snapshot so arrivals are never blamed on a correction.
            var excluded=pending.Select(b=>b.Id).ToHashSet();
            async Task<ILookup<Guid,Evidence>> Snapshot(){var (inputs,teaching)=await Assessment.LoadInputs(db,student,ct,excluded);return Assessment.Replay(student.FamilyId,student.Id,replacement.Id,student.TimeZone,inputs,teaching).Evidence.ToLookup(e=>e.AttemptId);}
            var previous=await Snapshot();var trace=new List<(CorrectionBatch Batch,ILookup<Guid,Evidence> Before,ILookup<Guid,Evidence> After)>();
            foreach(var batch in effective){excluded.Remove(batch.Id);var next=await Snapshot();trace.Add((batch,previous,next));previous=next;}
            var roots=effective.ToDictionary(b=>b.Id,b=>Json.Read<Guid[]>(b.AffectedAttemptIds??"[]").ToHashSet());
            var contexts=output.Contexts.ToDictionary(c=>c.AttemptId);
            foreach(var evidence in old)
            {
                if(current[evidence.AttemptId].Any(e=>Equivalent(evidence,e)))continue;
                var cause=trace.LastOrDefault(step=>step.Before[evidence.AttemptId].Any(e=>Equivalent(evidence,e)) && !step.After[evidence.AttemptId].Any(e=>Equivalent(evidence,e))).Batch;
                if(cause==null)continue; // No confirmed correction actually displaced this particular historical part.
                var direct=roots[cause.Id].Contains(evidence.AttemptId);
                if(!direct && !changedActive[evidence.AttemptId].Any(e=>Equivalent(evidence,e)))continue;
                db.Add(new EvidenceRevocation{FamilyId=student.FamilyId,StudentId=student.Id,EvidenceId=evidence.Id,CorrectionBatchId=cause.Id,ReplacementGenerationId=replacement.Id,Reason=cause.Reason,Effect=direct?"DirectCorrection":"ReplayDependency"});
            }
            // Contexts summarize the last actual mathematical change; each evidence part retains its own cause.
            foreach(var context in contexts.Values)
            {
                var changes=trace.Where(step=>step.Before[context.AttemptId].Any(e=>!step.After[context.AttemptId].Any(n=>Equivalent(e,n))) || step.After[context.AttemptId].Any(e=>!step.Before[context.AttemptId].Any(n=>Equivalent(e,n)))).ToArray();
                if(changes.Length>0)context.CorrectionBatchId=changes[^1].Batch.Id;
                foreach(var evidence in current[context.AttemptId])
                {
                    var cause=trace.LastOrDefault(step=>step.After[evidence.AttemptId].Any(e=>Equivalent(evidence,e)) && !step.Before[evidence.AttemptId].Any(e=>Equivalent(evidence,e))).Batch;
                    if(cause!=null)evidence.CorrectionBatchId=cause.Id;
                }
            }
        }
        foreach(var batch in pending)batch.Status=referenced.Contains(batch.Id)?"Applied":"Superseded";
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/students/{id:guid}/evidence-revocations",async(Guid id,int? offset,int? limit,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Parent");var skip=offset??0;var take=limit??20;if(skip<0 || take is <1 or >100)throw new ApiError(422,"INVALID_PAGE","页码须非负，每页1至100项。");
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);await a.Student(db,id);
            var query=db.Set<EvidenceRevocation>().Where(r=>r.FamilyId==a.FamilyId && r.StudentId==id);var total=await query.CountAsync();var rows=await query.OrderByDescending(r=>r.CreatedAt).ThenBy(r=>r.Id).Skip(skip).Take(take).ToArrayAsync();
            var evidenceIds=rows.Select(r=>r.EvidenceId).ToArray();var batchIds=rows.Select(r=>r.CorrectionBatchId).ToArray();
            var evidence=await db.Evidence.Where(e=>e.FamilyId==a.FamilyId && e.StudentId==id && evidenceIds.Contains(e.Id)).ToArrayAsync();var batches=await db.Set<CorrectionBatch>().Where(b=>b.FamilyId==a.FamilyId && b.StudentId==id && batchIds.Contains(b.Id)).ToArrayAsync();await tx.CommitAsync();return new EvidenceRevocationPage(rows,evidence,batches,total,skip,take);
        });
    }
}
