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
            var roots=effective.ToDictionary(b=>b.Id,b=>Json.Read<Guid[]>(b.AffectedAttemptIds??"[]").ToHashSet());
            var contexts=output.Contexts.ToDictionary(c=>c.AttemptId);
            foreach(var evidence in old)
            {
                if(current[evidence.AttemptId].Any(e=>Equivalent(evidence,e)))continue;
                var directCauses=effective.Where(b=>roots[b.Id].Contains(evidence.AttemptId)).ToArray();
                if(directCauses.Length==0 && !changedActive[evidence.AttemptId].Any(e=>Equivalent(evidence,e)))continue;
                var context=contexts.GetValueOrDefault(evidence.AttemptId);
                var cause=directCauses.LastOrDefault(b=>b.Id==context?.CorrectionBatchId)??directCauses.LastOrDefault()??effective[^1];
                var direct=directCauses.Length>0;
                if(context!=null)context.CorrectionBatchId=cause.Id;
                foreach(var next in current[evidence.AttemptId])next.CorrectionBatchId=cause.Id;
                db.Add(new EvidenceRevocation{FamilyId=student.FamilyId,StudentId=student.Id,EvidenceId=evidence.Id,CorrectionBatchId=cause.Id,ReplacementGenerationId=replacement.Id,Reason=cause.Reason,Effect=direct?"DirectCorrection":"ReplayDependency"});
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
