using Microsoft.EntityFrameworkCore;
namespace Learning;
public static class Provenance
{
    public static async Task<object[]> Citations(Database db,Guid family,Guid? kc=null,bool publishedOnly=false)
    {
        var rows=await (from c in db.Candidates join run in db.BuilderRuns on c.RunId equals run.Id join chunk in db.Chunks on c.ChunkId equals chunk.Id join source in db.Sources on chunk.SourceId equals source.Id
            where c.FamilyId==family && run.FamilyId==family && chunk.FamilyId==family && source.FamilyId==family && c.Status=="Accepted" && (c.CreatedKCId!=null || c.ExistingKCId!=null) && (kc==null || c.CreatedKCId==kc || c.ExistingKCId==kc) && (!publishedOnly || db.Set<ContentIdentity>().Any(k=>k.FamilyId==family && k.EntityType=="KC" && k.Id==(c.CreatedKCId??c.ExistingKCId)))
            orderby c.ReviewedAt select new {c,run,chunk,source}).ToListAsync();
        return rows.Select(x=>(object)new {kcId=x.c.CreatedKCId??x.c.ExistingKCId,candidateId=x.c.Id,x.c.CreatedDraftId,source=new {x.source.Id,x.source.Title,x.source.Hash,x.source.UsageScope,x.source.FileId},citation=new {x.chunk.Id,x.chunk.Locator,x.c.Quote},run=new {x.run.Id,x.run.Provider,x.run.Model,x.run.PromptVersion,x.run.InputVersion,x.run.InputHash,x.run.LibraryReleaseId},review=new {x.c.Decision,x.c.ReviewReason,x.c.ReviewedBy,x.c.ReviewedAt}}).ToArray();
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/content/kcs/{id:guid}/provenance",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");
            if(!await db.Set<ContentIdentity>().AnyAsync(k=>k.Id==id && k.FamilyId==a.FamilyId && k.EntityType=="KC") && !await db.Candidates.AnyAsync(c=>c.CreatedKCId==id && c.FamilyId==a.FamilyId))throw new ApiError(404,"NOT_FOUND","能力不存在。");
            var citations=await Citations(db,a.FamilyId,id);return TypedResults.Ok(new {kcId=id,citations,notice=citations.Length==0?"没有建库引用记录，可能是人工内容或旧数据；不推定来源。":"引用保留原始片段、位置、建库输入版本与人工审核记录。"});
        });
    }
}
