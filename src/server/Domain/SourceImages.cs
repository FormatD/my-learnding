using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
namespace Learning;

// Append-only source evidence. It records provenance, never an OCR approval.
public class SourceImage : Row
{
    public Guid SourceId {get;set;}
    public Guid FileId {get;set;}
    public string SourceHash {get;set;}="";
    public string DocumentHash {get;set;}="";
    public int Page {get;set;}
    public string PrintedPage {get;set;}="";
}
public record SourceImageInput(string Name,string MimeType,string Base64,string ExpectedSourceHash,string DocumentHash,int Page,string PrintedPage);
public record SourceImagePage(Guid Id,int Page,string PrintedPage,string DocumentHash,string ImageHash,string Url);
public record SourceImageContext(Guid Id,string Title,string Text,string Hash,Chunk[] Chunks,SourceImagePage[] Pages);
public static class SourceImages
{
    static bool Hash(string? value)=>value is {Length:64} && value.All(c=>char.IsAsciiHexDigit(c)&&!char.IsUpper(c));
    public static async Task<Guid[]> Resolve(Database db,Guid family,string stage,Guid id,CancellationToken ct)
    {
        IQueryable<T> Owned<T>()where T:Row=>db.Set<T>().AsNoTracking().Where(x=>x.FamilyId==family);
        async Task<T> One<T>()where T:Row=>await Owned<T>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiError(404,"NOT_FOUND","找不到本家庭原图来源。");
        async Task<Guid[]> Run(Guid run)=>await Owned<BuilderRun>().Where(x=>x.Id==run).Select(x=>x.SourceId).ToArrayAsync(ct);
        async Task<Guid[]> Drafts(Guid[] roots)
        {
            var seen=new HashSet<Guid>();var sources=new HashSet<Guid>();var next=roots;
            while(next.Length>0)
            {
                var current=next.Where(seen.Add).ToArray();if(current.Length==0)break;
                if(seen.Count>512)throw new ApiError(422,"SOURCE_LINEAGE_LIMIT","来源关联过多，请逐份草稿查看。");
                var kcIds=new HashSet<Guid>();var refs=new HashSet<Guid>();
                foreach(var payload in await Owned<ContentDraft>().Where(d=>current.Contains(d.Id)).Select(d=>d.Payload).ToArrayAsync(ct))
                {
                    var catalog=Json.Read<Catalog>(payload);foreach(var k in catalog.Kcs)kcIds.Add(k.Id);
                    foreach(var r in (catalog.Textbooks??[]).Where(t=>t.SourceId!=null).Select(t=>t.SourceId!.Value).Concat((catalog.Courses??[]).SelectMany(c=>c.SourceRefs??[])).Concat(catalog.Lessons.SelectMany(l=>l.SourceRefs??[])))refs.Add(r);
                }
                foreach(var s in await Owned<Source>().Where(s=>refs.Contains(s.Id)).Select(s=>s.Id).ToArrayAsync(ct))sources.Add(s);
                foreach(var s in await Owned<Chunk>().Where(c=>refs.Contains(c.Id)).Select(c=>c.SourceId).ToArrayAsync(ct))sources.Add(s);
                // Accepted ability provenance also survives a new draft revision with the same ability identity.
                var runs=Owned<Candidate>().Where(c=>c.CreatedDraftId!=null&&current.Contains(c.CreatedDraftId.Value)||c.Status=="Accepted"&&c.CreatedKCId!=null&&kcIds.Contains(c.CreatedKCId.Value)).Select(c=>c.RunId);
                foreach(var s in await Owned<BuilderRun>().Where(r=>runs.Contains(r.Id)).Select(r=>r.SourceId).ToArrayAsync(ct))sources.Add(s);
                var mappingRuns=from decision in Owned<MappingReviewDecision>() join suggestion in Owned<MappingSuggestion>() on decision.SuggestionId equals suggestion.Id join run in Owned<MappingRun>() on suggestion.RunId equals run.Id where decision.CreatedDraftId!=null&&current.Contains(decision.CreatedDraftId.Value) select run.SourceDraftId;
                var independent=from set in Owned<MappingSetRevision>() join input in Owned<IndependentMappingDraft>() on set.Id equals input.SetRevisionId where current.Contains(set.DraftId) select input.SourceDraftId;
                next=(await mappingRuns.ToArrayAsync(ct)).Concat(await independent.ToArrayAsync(ct)).Distinct().ToArray();
            }
            return sources.ToArray();
        }
        switch(stage)
        {
            case "sources":return [(await One<Source>()).Id];
            case "parsing":case "extraction":return [(await One<BuilderRun>()).SourceId];
            case "review":return await Run((await One<Candidate>()).RunId);
            case "semantic":return await Run((await One<BuilderSemanticPreparation>()).RunId);
            case "drafts":return await Drafts([(await One<ContentDraft>()).Id]);
            case "mapping":return await Drafts([(await One<MappingPreparation>()).SourceDraftId]);
            case "mapping-review":return await Drafts([(await One<MappingRun>()).SourceDraftId]);
            case "publication":await One<Release>();return await Drafts(await Owned<ContentReviewRecord>().Where(r=>r.PublishedReleaseId==id).Select(r=>r.DraftId).ToArrayAsync(ct));
            case "indexes":await One<ModelEmbeddingIndex>();return [];
            default:throw new ApiError(422,"INVALID_BUILDER_STAGE","请选择有效的来源阶段。");
        }
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/content/sources/{id:guid}/images",async(Guid id,SourceImageInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var source=await db.Sources.SingleOrDefaultAsync(s=>s.FamilyId==a.FamilyId&&s.Id==id)??throw new ApiError(404,"NOT_FOUND","找不到来源。");
            if(source.Hash!=input.ExpectedSourceHash)throw new ApiError(412,"SOURCE_CHANGED","来源文字已变化，请重新核对原图。");
            if(!Hash(input.DocumentHash)||input.Page is <1 or >10000||string.IsNullOrWhiteSpace(input.PrintedPage)||input.PrintedPage.Length>100||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>200||input.Name.Any(char.IsControl))throw new ApiError(422,"INVALID_SOURCE_IMAGE","请记录教材哈希、有效页码及文件名。");
            byte[] bytes;try{bytes=Convert.FromBase64String(input.Base64);}catch(FormatException){throw new ApiError(422,"INVALID_SOURCE_IMAGE","图片编码无效。");}
            if(bytes.Length is 0 or >10_000_000||input.MimeType is not ("image/png" or "image/jpeg")||!ResourceFiles.ValidHeader(input.MimeType,bytes))throw new ApiError(422,"INVALID_SOURCE_IMAGE","原图须为10 MB以内的PNG或JPEG。");
            var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();var old=await db.Set<SourceImage>().SingleOrDefaultAsync(p=>p.FamilyId==a.FamilyId&&p.SourceId==id&&p.DocumentHash==input.DocumentHash&&p.Page==input.Page);
            if(old!=null){var file=await db.Set<PrivateFile>().SingleAsync(f=>f.Id==old.FileId&&f.FamilyId==a.FamilyId);if(file.Hash!=hash||old.PrintedPage!=input.PrintedPage||old.SourceHash!=source.Hash)throw new ApiError(409,"SOURCE_IMAGE_CONFLICT","此页已关联另一份原图，请保留原证据并另建来源。");return old;}
            var image=new PrivateFile{FamilyId=a.FamilyId,Name=Path.GetFileName(input.Name),MimeType=input.MimeType,Hash=hash,Bytes=bytes};db.Add(image);
            var page=new SourceImage{FamilyId=a.FamilyId,SourceId=id,FileId=image.Id,SourceHash=source.Hash,DocumentHash=input.DocumentHash,Page=input.Page,PrintedPage=input.PrintedPage};db.Add(page);return page;
        });
        api.MapGet("/builder/reference-images/{stage}/{id:guid}",async(string stage,Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var ct=ctx.RequestAborted;await using var snapshot=await ReadSnapshot.Begin(db,ctx);var ids=await Resolve(db,a.FamilyId,stage,id,ct);var result=new List<SourceImageContext>();
            foreach(var source in await db.Sources.AsNoTracking().Where(s=>s.FamilyId==a.FamilyId&&ids.Contains(s.Id)).OrderBy(s=>s.CreatedAt).ThenBy(s=>s.Id).ToArrayAsync(ct))
            {
                var pages=await (from p in db.Set<SourceImage>().AsNoTracking() join f in db.Set<PrivateFile>() on p.FileId equals f.Id where p.FamilyId==a.FamilyId&&f.FamilyId==a.FamilyId&&p.SourceId==source.Id select new{p,f.Hash}).OrderBy(x=>x.p.DocumentHash).ThenBy(x=>x.p.Page).ToArrayAsync(ct);
                result.Add(new(source.Id,source.Title,source.Text,source.Hash,await db.Chunks.AsNoTracking().Where(c=>c.FamilyId==a.FamilyId&&c.SourceId==source.Id).OrderBy(c=>c.CreatedAt).ThenBy(c=>c.Id).ToArrayAsync(ct),pages.Select(x=>new SourceImagePage(x.p.Id,x.p.Page,x.p.PrintedPage,x.p.DocumentHash,x.Hash,$"/api/v1/content/source-images/{x.p.Id}")).ToArray()));
            }
            await snapshot.CommitAsync(ct);return result.ToArray();
        });
        api.MapGet("/content/source-images/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var page=await db.Set<SourceImage>().AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id&&p.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到原图。");var file=await db.Set<PrivateFile>().AsNoTracking().SingleAsync(f=>f.Id==page.FileId&&f.FamilyId==a.FamilyId);
            if(file.Hash!=Convert.ToHexString(SHA256.HashData(file.Bytes)).ToLowerInvariant()||!ResourceFiles.ValidHeader(file.MimeType,file.Bytes))throw new ApiError(422,"SOURCE_IMAGE_CHANGED","原图完整性检查失败。");ctx.Response.Headers.CacheControl="private, no-store";return Results.File(file.Bytes,file.MimeType);
        }).WithMetadata(new DownloadResponseMetadata(["image/png","image/jpeg"]));
    }
}
