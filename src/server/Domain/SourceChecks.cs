using Microsoft.EntityFrameworkCore;
namespace Learning;
public class SourceCheck : Row
{
    public Guid OriginalSourceId {get;set;}
    public Guid SourceId {get;set;}
    public Guid ActorId {get;set;}
    public string Operation {get;set;}="Revision";
    public string Reason {get;set;}="";
    public string Snapshot {get;set;}="";
    public string SnapshotHash {get;set;}="";
}
public record SourceCheckInput(string ExpectedSnapshotHash,string Reason);
public record SourceCorrectionInput(string ExpectedSnapshotHash,string Title,string Text,string Reason);
public record SourceCheckContext(Source Source,Chunk[] Fragments,string SnapshotHash,bool Confirmed,SourceCheck[] History);
public static class SourceChecks
{
    static async Task<(Source source,Chunk[] chunks,string snapshot,string hash)> Capture(Database db,Guid family,Guid id,CancellationToken ct)
    {
        var source=await db.Sources.AsNoTracking().SingleOrDefaultAsync(s=>s.FamilyId==family&&s.Id==id,ct)??throw new ApiError(404,"NOT_FOUND","找不到本家庭来源。");
        var chunks=await db.Chunks.AsNoTracking().Where(c=>c.FamilyId==family&&c.SourceId==id).OrderBy(c=>c.CreatedAt).ThenBy(c=>c.Id).ToArrayAsync(ct);
        var images=await(from p in db.Set<SourceImage>().AsNoTracking() join f in db.Set<PrivateFile>().AsNoTracking() on p.FileId equals f.Id where p.FamilyId==family&&f.FamilyId==family&&p.SourceId==id orderby p.DocumentHash,p.Page,p.Id select new{p.Id,p.FileId,p.SourceHash,p.DocumentHash,p.Page,p.PrintedPage,imageHash=f.Hash}).ToArrayAsync(ct);
        var snapshot=Json.Write(new{version="source-check/1",source,chunks,images});return(source,chunks,snapshot,Content.Hash(snapshot));
    }
    static void Validate(string expected,string actual,string reason)
    {
        if(expected!=actual)throw new ApiError(412,"SOURCE_CHANGED","来源文字、分段或原图已变化，请重新读取后核对；未提交修改请先保留。");
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>4000)throw new ApiError(422,"REASON_REQUIRED","请填写1至4000字校对或复核依据。");
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/content/sources/{id:guid}/review",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var ct=ctx.RequestAborted;await using var read=await ReadSnapshot.Begin(db,ctx);var captured=await Capture(db,a.FamilyId,id,ct);
            var history=await db.Set<SourceCheck>().AsNoTracking().Where(c=>c.FamilyId==a.FamilyId&&(c.SourceId==id||c.OriginalSourceId==id)).OrderByDescending(c=>c.CreatedAt).ThenBy(c=>c.Id).ToArrayAsync(ct);
            var result=new SourceCheckContext(captured.source,captured.chunks,captured.hash,history.Any(h=>h.SourceId==id&&h.Operation=="Confirmation"&&h.SnapshotHash==captured.hash),history);await read.CommitAsync(ct);return result;
        });
        api.MapPost("/content/sources/{id:guid}/corrections",async(Guid id,SourceCorrectionInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var c=await Capture(db,a.FamilyId,id,ctx.RequestAborted);Validate(input.ExpectedSnapshotHash,c.hash,input.Reason);
            if(string.IsNullOrWhiteSpace(input.Title)||input.Title.Length>500||input.Text is not {Length:>=5 and <=100000}||!input.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Any(s=>!string.IsNullOrWhiteSpace(s)))throw new ApiError(422,"INVALID_SOURCE","请填写标题和5至100000字正文。");
            if(input.Text==c.source.Text)throw new ApiError(422,"SOURCE_NOT_CHANGED","正文未修改，可直接确认本来源复核。");
            var hash=Content.Hash(input.Text);if(await db.Sources.AnyAsync(s=>s.FamilyId==a.FamilyId&&s.Hash==hash))throw new ApiError(409,"SOURCE_ALREADY_EXISTS","相同正文的来源已存在，请直接查看并复核该来源。");
            var source=new Source{FamilyId=a.FamilyId,Title=input.Title.Trim(),Text=input.Text,Hash=hash,UsageScope=c.source.UsageScope,AllowExternalAI=c.source.AllowExternalAI};db.Sources.Add(source);
            var lines=input.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim()).Where(t=>t.Length>0).ToArray();for(var i=0;i<lines.Length;i++)db.Chunks.Add(new(){FamilyId=a.FamilyId,SourceId=source.Id,Locator=$"段落 {i+1}",Text=lines[i]});
            foreach(var image in await db.Set<SourceImage>().Where(p=>p.FamilyId==a.FamilyId&&p.SourceId==id).ToArrayAsync())db.Add(new SourceImage{FamilyId=a.FamilyId,SourceId=source.Id,FileId=image.FileId,SourceHash=hash,DocumentHash=image.DocumentHash,Page=image.Page,PrintedPage=image.PrintedPage});
            // Save the correction's original input as evidence; correction alone is not approval.
            var check=new SourceCheck{FamilyId=a.FamilyId,OriginalSourceId=id,SourceId=source.Id,ActorId=a.Id,Operation="Revision",Reason=input.Reason.Trim(),Snapshot=c.snapshot,SnapshotHash=c.hash};db.Add(check);return source;
        });
        api.MapPost("/content/sources/{id:guid}/confirmations",async(Guid id,SourceCheckInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var c=await Capture(db,a.FamilyId,id,ctx.RequestAborted);Validate(input.ExpectedSnapshotHash,c.hash,input.Reason);
            if(string.IsNullOrWhiteSpace(c.source.Text)||c.chunks.Length==0)throw new ApiError(422,"SOURCE_NOT_READY","请先准备可核对的文字和分段。");
            var old=await db.Set<SourceCheck>().SingleOrDefaultAsync(s=>s.FamilyId==a.FamilyId&&s.SourceId==id&&s.Operation=="Confirmation"&&s.SnapshotHash==c.hash);if(old!=null)return old;
            var check=new SourceCheck{FamilyId=a.FamilyId,OriginalSourceId=id,SourceId=id,ActorId=a.Id,Operation="Confirmation",Reason=input.Reason.Trim(),Snapshot=c.snapshot,SnapshotHash=c.hash};db.Add(check);return check;
        });
    }
}
