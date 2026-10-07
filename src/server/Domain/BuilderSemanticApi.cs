using Microsoft.EntityFrameworkCore;
namespace Learning;
public record BuilderSemanticPreparationSummary(Guid Id,Guid CandidateId,Guid RunId,Guid JobId,Guid? LibraryReleaseId,string Status,int AttemptCount,int RetryRound,string? Error,Guid? SuggestionId,DateTimeOffset CreatedAt);
public static class BuilderSemanticApi
{
    static async Task<BuilderSemanticPreparationSummary> Summary(Database db,BuilderSemanticPreparation p,CancellationToken ct=default)
    {
        var j=db.Set<BackgroundJob>().Local.FirstOrDefault(j=>j.Id==p.JobId)??await db.Set<BackgroundJob>().AsNoTracking().SingleAsync(j=>j.Id==p.JobId && j.FamilyId==p.FamilyId,ct);
        var sid=await db.Set<BuilderSemanticSuggestion>().Where(s=>s.PreparationId==p.Id && s.FamilyId==p.FamilyId).Select(s=>(Guid?)s.Id).SingleOrDefaultAsync(ct);return new(p.Id,p.CandidateId,p.RunId,p.JobId,p.LibraryReleaseId,j.Status,j.AttemptCount,j.RetryRound,j.LastErrorCode,sid,p.CreatedAt);
    }
    static (int Page,int Size) Page(int? page,int? pageSize){var p=page??1;var size=pageSize??20;if(p is <1 or >100000 || size is <1 or >50)throw new ApiError(422,"INVALID_PAGE","请使用有效页码和每页1～50条。");return(p,size);}
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/builder/candidates/{id:guid}/semantic-preparations",async(Guid id,Database db,HttpContext ctx)=>{var p=await BuilderSemanticJobs.Enqueue(db,ctx.Actor(),id,ctx.RequestAborted);return TypedResults.Accepted("/api/v1/builder/semantic-preparations/"+p.Id,await Summary(db,p,ctx.RequestAborted));});
        api.MapGet("/builder/semantic-preparations",async(Guid? candidateId,int? page,int? pageSize,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var (p,size)=Page(page,pageSize);var ct=ctx.RequestAborted;await using var snapshot=await ReadSnapshot.Begin(db,ctx);
            if(candidateId!=null && !await db.Candidates.AnyAsync(c=>c.Id==candidateId && c.FamilyId==a.FamilyId,ct))throw new ApiError(404,"NOT_FOUND","找不到本家庭候选。");
            var query=db.Set<BuilderSemanticPreparation>().AsNoTracking().Where(x=>x.FamilyId==a.FamilyId && (candidateId==null || x.CandidateId==candidateId));var total=await query.CountAsync(ct);var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip((p-1)*size).Take(size).ToArrayAsync(ct);var items=new List<BuilderSemanticPreparationSummary>();foreach(var row in rows)items.Add(await Summary(db,row,ct));await snapshot.CommitAsync(ct);return new{page=p,pageSize=size,total,items};
        });
        api.MapGet("/builder/semantic-preparations/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var ct=ctx.RequestAborted;await using var snapshot=await ReadSnapshot.Begin(db,ctx);
            var p=await db.Set<BuilderSemanticPreparation>().AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id && p.FamilyId==a.FamilyId,ct)??throw new ApiError(404,"NOT_FOUND","找不到本家庭语义任务。");
            var summary=await Summary(db,p,ct);var frozen=Json.Read<BuilderSemanticFrozenInput>(p.Snapshot);var suggestion=await db.Set<BuilderSemanticSuggestion>().AsNoTracking().SingleOrDefaultAsync(s=>s.PreparationId==p.Id && s.FamilyId==a.FamilyId,ct);var result=suggestion==null?null:Json.Read<BuilderSemanticResult>(suggestion.ResultPayload);await snapshot.CommitAsync(ct);return new{preparation=summary,input=Json.Read<BuilderSemanticInput>(frozen.ModelInputPayload),model=BuilderSemanticJobs.ResolveLocal(frozen),suggestion,result};
        });
        api.MapPost("/builder/semantic-preparations/{id:guid}:retry",async(Guid id,ReasonInput input,Database db,HttpContext ctx)=>{var p=await BuilderSemanticJobs.Retry(db,ctx.Actor(),id,input.Reason,ctx.RequestAborted);return TypedResults.Accepted("/api/v1/builder/semantic-preparations/"+id,await Summary(db,p,ctx.RequestAborted));});
        api.MapGet("/builder/semantic-calls",async(Guid? preparationId,int? page,int? pageSize,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var (p,size)=Page(page,pageSize);var ct=ctx.RequestAborted;await using var snapshot=await ReadSnapshot.Begin(db,ctx);
            if(preparationId!=null && !await db.Set<BuilderSemanticPreparation>().AnyAsync(x=>x.Id==preparationId && x.FamilyId==a.FamilyId,ct))throw new ApiError(404,"NOT_FOUND","找不到本家庭语义任务。");
            var query=db.Set<BuilderSemanticCall>().AsNoTracking().Where(c=>c.FamilyId==a.FamilyId && (preparationId==null || c.PreparationId==preparationId));var total=await query.CountAsync(ct);var calls=await query.OrderByDescending(c=>c.StartedAt).ThenBy(c=>c.Id).Skip((p-1)*size).Take(size).ToArrayAsync(ct);var ids=calls.Select(c=>c.Id).ToArray();var reconciliations=await db.Set<BuilderSemanticReconciliation>().AsNoTracking().Where(r=>r.FamilyId==a.FamilyId && ids.Contains(r.CallId)).ToArrayAsync(ct);var responses=await db.Set<BuilderSemanticResponse>().AsNoTracking().Where(r=>r.FamilyId==a.FamilyId && ids.Contains(r.CallId)).Select(r=>new{r.Id,r.CallId,r.OutputHash,r.OutputComplete,r.CreatedAt}).ToArrayAsync(ct);await snapshot.CommitAsync(ct);return new{page=p,pageSize=size,total,calls,reconciliations,responses};
        });
        api.MapGet("/builder/semantic-responses/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("ContentEditor");return await db.Set<BuilderSemanticResponse>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id && r.FamilyId==a.FamilyId,ctx.RequestAborted)??throw new ApiError(404,"NOT_FOUND","找不到本家庭模型返回。");});
        api.MapPost("/builder/semantic-calls/{id:guid}:reconcile",async(Guid id,BuilderSemanticReconciliationInput input,Database db,HttpContext ctx)=>TypedResults.Created("/api/v1/builder/semantic-calls/"+id,await BuilderSemanticCallTracking.Reconcile(db,ctx.Actor(),id,input,ctx.RequestAborted)));
    }
}
