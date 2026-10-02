using Microsoft.EntityFrameworkCore;
namespace Learning;
public record AssessmentContextPage(Generation? Generation,Generation[] Generations,AssessmentContext[] Contexts,int Total,int LegacyEvidenceCount,int Offset,int Limit);
public static class AssessmentContexts
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/students/{id:guid}/assessment-contexts",async(Guid id,Guid? generation,int? offset,int? limit,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("Parent");var skip=offset??0;var take=limit??50;
            if(skip<0 || take is <1 or >100)throw new ApiError(422,"INVALID_PAGE","页码须非负，每页1至100项。");
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var student=await actor.Student(db,id);var target=generation??student.ActiveGenerationId;
            var selected=target==null?null:await db.Generations.SingleOrDefaultAsync(g=>g.FamilyId==actor.FamilyId && g.StudentId==id && g.Id==target)??throw new ApiError(404,"NOT_FOUND","评估版本不属于当前学生。");
            var generations=await db.Generations.Where(g=>g.FamilyId==actor.FamilyId && g.StudentId==id).OrderByDescending(g=>g.CreatedAt).ThenByDescending(g=>g.Id).Take(50).ToArrayAsync();
            var query=db.Set<AssessmentContext>().Where(c=>c.FamilyId==actor.FamilyId && c.StudentId==id && c.GenerationId==target);
            var total=await query.CountAsync();var rows=await query.OrderBy(c=>c.CreatedAt).ThenBy(c=>c.AttemptId).Skip(skip).Take(take).ToArrayAsync();
            var legacy=await db.Evidence.CountAsync(e=>e.FamilyId==actor.FamilyId && e.StudentId==id && e.GenerationId==target && e.ContextId==null);
            await tx.CommitAsync();return new AssessmentContextPage(selected,generations,rows,total,legacy,skip,take);
        });
    }
}
