using Microsoft.EntityFrameworkCore;

namespace Learning;
public record SourceInput(string Title,string Text,bool AllowExternalAI=false,string UsageScope="FamilyOnly");
public record RunInput(Guid SourceId,string Provider="Mock");
public record DecisionInput(string Decision,string Reason,string? Name=null,string? Behavior=null,string? Boundary=null,Guid? ExistingKCId=null);
public static class Builder
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/builder",async (Database db,HttpContext ctx) => { ctx.Actor().Require("ContentEditor");var family=ctx.Actor().FamilyId;return new { sources=await db.Sources.Where(s => s.FamilyId==family).OrderByDescending(s => s.CreatedAt).ToListAsync(),chunks=await db.Chunks.Where(s => s.FamilyId==family).ToListAsync(),runs=await db.BuilderRuns.Where(s => s.FamilyId==family).OrderByDescending(s => s.CreatedAt).ToListAsync(),candidates=await db.Candidates.Where(s => s.FamilyId==family).OrderByDescending(s => s.CreatedAt).ToListAsync(),provider="Mock · 仅验证流程，不代表模型效果" }; });
        api.MapPost("/content/sources",async (SourceInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("ContentEditor");if (string.IsNullOrWhiteSpace(input.Title) || input.Text.Length<5 || input.Text.Length>100_000 || string.IsNullOrWhiteSpace(input.UsageScope)) throw new ApiError(422,"INVALID_SOURCE","来源需标题、许可范围与 5～100000 字文本。");
            var hash=Content.Hash(input.Text);var old=await db.Sources.SingleOrDefaultAsync(s => s.FamilyId==a.FamilyId && s.Hash==hash);if (old!=null) return Results.Ok(old);
            var source=new Source { FamilyId=a.FamilyId,Title=input.Title,Text=input.Text,Hash=hash,UsageScope=input.UsageScope,AllowExternalAI=input.AllowExternalAI };db.Sources.Add(source);
            var paragraphs=input.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length>0).ToArray();
            for (var i=0;i<paragraphs.Length;i++) db.Chunks.Add(new() { FamilyId=a.FamilyId,SourceId=source.Id,Locator=$"段落 {i+1}",Text=paragraphs[i] });
            return Results.Created($"/api/v1/content/sources/{source.Id}",source);
        });
        api.MapPost("/builder/runs",async (RunInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var source=await db.Sources.SingleOrDefaultAsync(s => s.Id==input.SourceId && s.FamilyId==a.FamilyId) ?? throw new ApiError(404,"NOT_FOUND","来源不存在。");
            if (input.Provider!="Mock") throw new ApiError(422,source.AllowExternalAI?"PROVIDER_UNCONFIGURED":"EXTERNAL_AI_DENIED",source.AllowExternalAI?"模型尚未配置，学习功能可继续使用。":"来源未允许发送外部模型。");
            if (string.IsNullOrWhiteSpace(source.Text)) throw new ApiError(422,"SOURCE_NOT_READY","来源尚未解析完成，或没有文本层。");
            var libraryHash=await db.Releases.Where(r=>r.FamilyId==a.FamilyId && !r.Withdrawn).OrderByDescending(r=>r.Number).Select(r=>r.Hash).FirstOrDefaultAsync();
            var hash=Content.Hash(source.Hash+":"+input.Provider+":fixture/1:kc-candidate/1:"+libraryHash);var old=await db.BuilderRuns.SingleOrDefaultAsync(r => r.FamilyId==a.FamilyId && r.InputHash==hash);if (old!=null) return Results.Ok(old);
            var run=new BuilderRun { FamilyId=a.FamilyId,SourceId=source.Id,InputHash=hash };db.BuilderRuns.Add(run);return Results.Accepted("/api/v1/builder",run);
        });
        api.MapPost("/builder/candidates/{id:guid}:decide",async (Guid id,DecisionInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var c=await db.Candidates.SingleOrDefaultAsync(c => c.Id==id && c.FamilyId==a.FamilyId) ?? throw new ApiError(404,"NOT_FOUND","候选不存在。");
            if (c.Status!="Pending") throw new ApiError(409,"ALREADY_REVIEWED","此候选已经处理。");
            if (string.IsNullOrWhiteSpace(input.Reason)) throw new ApiError(422,"REASON_REQUIRED","请填写审核依据。");
            if (input.Decision=="Reject") c.Status="Rejected";
            else if (input.Decision=="LinkExisting")
            {
                var releases=await db.Releases.Where(r => r.FamilyId==a.FamilyId && !r.Withdrawn).ToListAsync();
                if (!releases.Any(r => Json.Read<Catalog>(r.Payload).Kcs.Any(k => k.Id==input.ExistingKCId))) throw new ApiError(422,"KC_NOT_PUBLISHED","请关联本家庭已发布 KC。");
                c.ExistingKCId=input.ExistingKCId;c.Status="Accepted";
                var normalized=Retrieval.Normalize(input.Name??c.Name);
                if (!await db.Set<Alias>().AnyAsync(alias=>alias.FamilyId==a.FamilyId && alias.KCId==input.ExistingKCId && alias.Normalized==normalized)) db.Add(new Alias { FamilyId=a.FamilyId,KCId=input.ExistingKCId!.Value,CandidateId=id,Text=input.Name??c.Name,Normalized=normalized,ReviewedBy=a.Id });
            }
            else if (input.Decision=="CreateDraft")
            {
                var name=input.Name??c.Name;var behavior=input.Behavior??c.Behavior;var boundary=input.Boundary??c.Boundary;
                if (string.IsNullOrWhiteSpace(behavior) || string.IsNullOrWhiteSpace(boundary) || name.Length>100) throw new ApiError(422,"INVALID_DEFINITION","请补充可测行为和边界。");
                var k=new KC(Guid.NewGuid(),Guid.NewGuid(),$"MATH.CUSTOM.{Guid.NewGuid():N}",name,behavior,boundary);
                db.Drafts.Add(new() { FamilyId=a.FamilyId,Title=$"Builder 审核草稿 · {name}",Payload=Json.Write(new Catalog([k],[],[],[],[])) });c.Status="Accepted";
            }
            else throw new ApiError(422,"INVALID_DECISION","支持新建草稿、关联已有或拒绝。");
            db.Audits.Add(new() { FamilyId=a.FamilyId,ActorId=a.Id,Action="CandidateReview",Details=Json.Write(new { candidateId=id,input.Decision,input.Reason,input.ExistingKCId }) });
            return Results.Ok(c);
        });
    }
    public static async Task ProcessOne(Database db,CancellationToken ct)
    {
        var run=await db.BuilderRuns.Where(r => r.Status=="Queued").OrderBy(r => r.CreatedAt).FirstOrDefaultAsync(ct);if (run==null) return;
        await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Lock(run.FamilyId,ct);await db.Entry(run).ReloadAsync(ct);if (run.Status!="Queued") return;
        if (run.Type=="ParsePDF")
        {
            var source=await db.Sources.SingleAsync(s=>s.Id==run.SourceId,ct);
            var file=await db.Set<PrivateFile>().SingleAsync(f=>f.Id==source.FileId && f.FamilyId==run.FamilyId,ct);
            try
            {
                var pages=Files.ParsePDF(file.Bytes);
                source.Text=string.Join('\n',pages.Select(p=>p.Text));
                foreach (var (page,text) in pages.Where(p=>!string.IsNullOrWhiteSpace(p.Text))) db.Chunks.Add(new() {FamilyId=run.FamilyId,SourceId=source.Id,Locator=$"PDF 第 {page} 页",Text=text});
                run.Status="Completed";
            }
            catch(ApiError e){run.Status=e.Code=="NEEDS_OCR"?"NeedsOCR":"Failed";run.Error=e.Code;}
            run.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return;
        }
        var chunks=await db.Chunks.Where(c => c.SourceId==run.SourceId).OrderBy(c => c.Locator).Take(100).ToListAsync(ct);
        var release=await db.Releases.Where(r=>r.FamilyId==run.FamilyId && !r.Withdrawn).OrderByDescending(r=>r.Number).FirstOrDefaultAsync(ct);
        var kcs=release==null ? [] : Json.Read<Catalog>(release.Payload).Kcs;
        foreach (var kc in kcs)
            if (!await db.Set<Embedding>().AnyAsync(e=>e.FamilyId==run.FamilyId && e.EntityRevisionId==kc.RevisionId && e.Space==Retrieval.Space,ct)) db.Add(new Embedding { FamilyId=run.FamilyId,EntityRevisionId=kc.RevisionId,TextHash=Content.Hash(kc.Name+kc.Behavior+kc.Boundary),Vector=Json.Write(Retrieval.Vector(kc.Name+" "+kc.Behavior+" "+kc.Boundary)) });
        foreach (var chunk in chunks)
        {
            // Mock output is deliberately recognizable and quotes only the real input.
            var quote=chunk.Text[..Math.Min(80,chunk.Text.Length)];
            db.Candidates.Add(new() { FamilyId=run.FamilyId,RunId=run.Id,ChunkId=chunk.Id,Name=quote[..Math.Min(24,quote.Length)],Quote=quote,Behavior="请审核者补充独立可测行为",Boundary="请审核者补充排除范围",SuggestedAction="NeedsReview",Matches=Json.Write(Retrieval.TopK(chunk.Text,kcs)) });
        }
        run.Status="Completed";run.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
