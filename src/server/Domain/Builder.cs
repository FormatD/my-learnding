using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Learning;
public record SourceInput(string Title,string Text,bool AllowExternalAI=false,string UsageScope="FamilyOnly");
public record RunInput(Guid SourceId,string Provider="Mock");
public record DecisionInput(string Decision,string Reason,string? Name=null,string? Behavior=null,string? Boundary=null,Guid? ExistingKCId=null);
public static class Builder
{
    public static void Map(RouteGroupBuilder api)
    {
        Provenance.Map(api);BuilderCallTracking.Map(api);BuilderBudget.Map(api);BackgroundJobs.Map(api);ProjectionJobs.Map(api);DomainEvents.Map(api);
        api.MapGet("/builder",async (Database db,HttpContext ctx) => { ctx.Actor().Require("ContentEditor");var family=ctx.Actor().FamilyId;return new { sources=await db.Sources.Where(s => s.FamilyId==family).OrderByDescending(s => s.CreatedAt).ToListAsync(),chunks=await db.Chunks.Where(s => s.FamilyId==family).ToListAsync(),runs=await db.BuilderRuns.Where(s => s.FamilyId==family).OrderByDescending(s => s.CreatedAt).ToListAsync(),attempts=await db.Set<BuilderAttempt>().Where(a=>a.FamilyId==family).OrderBy(a=>a.CreatedAt).ToArrayAsync(),candidates=await db.Candidates.Where(s => s.FamilyId==family).OrderByDescending(s => s.CreatedAt).ToListAsync(),libraries=await db.Releases.Where(r=>r.FamilyId==family).Select(r=>new {r.Id,r.Number,r.Hash,r.Withdrawn}).ToListAsync(),provider="Mock · 仅验证流程，不代表模型效果" }; });
        api.MapPost("/content/sources",async Task<Results<Ok<Source>,Created<Source>>> (SourceInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("ContentEditor");if (string.IsNullOrWhiteSpace(input.Title) || input.Text.Length<5 || input.Text.Length>100_000 || string.IsNullOrWhiteSpace(input.UsageScope)) throw new ApiError(422,"INVALID_SOURCE","来源需标题、许可范围与 5～100000 字文本。");
            var hash=Content.Hash(input.Text);var old=await db.Sources.SingleOrDefaultAsync(s => s.FamilyId==a.FamilyId && s.Hash==hash);if (old!=null) return TypedResults.Ok(old);
            var source=new Source { FamilyId=a.FamilyId,Title=input.Title,Text=input.Text,Hash=hash,UsageScope=input.UsageScope,AllowExternalAI=input.AllowExternalAI };db.Sources.Add(source);
            var paragraphs=input.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length>0).ToArray();
            for (var i=0;i<paragraphs.Length;i++) db.Chunks.Add(new() { FamilyId=a.FamilyId,SourceId=source.Id,Locator=$"段落 {i+1}",Text=paragraphs[i] });
            return TypedResults.Created($"/api/v1/content/sources/{source.Id}",source);
        });
        api.MapPost("/builder/runs",async Task<Results<Ok<BuilderRun>,Accepted<BuilderRun>>> (RunInput input,Database db,HttpContext ctx,IConfiguration configuration) =>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var source=await db.Sources.SingleOrDefaultAsync(s => s.Id==input.SourceId && s.FamilyId==a.FamilyId) ?? throw new ApiError(404,"NOT_FOUND","来源不存在。");
            if (input.Provider!="Mock") throw new ApiError(422,source.AllowExternalAI?"PROVIDER_UNCONFIGURED":"EXTERNAL_AI_DENIED",source.AllowExternalAI?"模型尚未配置，学习功能可继续使用。":"来源未允许发送外部模型。");
            if (string.IsNullOrWhiteSpace(source.Text)) throw new ApiError(422,"SOURCE_NOT_READY","来源尚未解析完成，或没有文本层。");
            var library=await db.Releases.Where(r=>r.FamilyId==a.FamilyId && !r.Withdrawn).OrderByDescending(r=>r.Number).FirstOrDefaultAsync();
            var configPayload=Json.Write(BuilderConfiguration.Current(configuration));var configHash=Content.Hash(configPayload);
            var hash=Content.Hash(source.Hash+":"+input.Provider+":fixture/1:kc-candidate/1:builder-input/3:"+library?.Hash+":"+configHash);var old=await db.BuilderRuns.SingleOrDefaultAsync(r => r.FamilyId==a.FamilyId && r.InputHash==hash);if (old!=null) return TypedResults.Ok(old);
            var run=new BuilderRun { FamilyId=a.FamilyId,SourceId=source.Id,LibraryReleaseId=library?.Id,InputVersion="builder-input/3",InputHash=hash,ModelConfigPayload=configPayload,ModelConfigHash=configHash };db.BuilderRuns.Add(run);return TypedResults.Accepted("/api/v1/builder",run);
        });
        api.MapPost("/builder/runs/{id:guid}:retry",async(Guid id,ReasonInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var run=await db.BuilderRuns.SingleOrDefaultAsync(r=>r.Id==id && r.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到建库任务。");
            if(string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>4000)throw new ApiError(422,"REASON_REQUIRED","请填写重新处理的依据，最多4000字。");
            if(run.Status!="Failed")throw new ApiError(409,"RUN_NOT_FAILED","只可重新处理已停止的失败任务。");
            if(run.Error is not ("BUILDER_PROCESSING_FAILED" or "BUILDER_CONCURRENCY_LIMIT" or "BUILDER_USAGE_RECONCILIATION_REQUIRED" or "BUILDER_CALL_BUDGET_LIMIT" or "BUILDER_DAILY_BUDGET_LIMIT" or "JOB_ATTEMPTS_EXHAUSTED"))throw new ApiError(422,"RUN_RECREATE_REQUIRED","此任务需要修复来源或重新准备输入，不能直接重试。");
            await ValidateRun(db,run);
            if(await db.Candidates.AnyAsync(c=>c.RunId==id) || run.Type=="ParsePDF" && await db.Chunks.AnyAsync(c=>c.SourceId==run.SourceId))throw new ApiError(422,"RUN_OUTPUT_EXISTS","已有输出不能再次生成；请检查原运行记录。");
            var before=Json.Write(run);run.Status="Queued";run.Retries=0;run.RetryRound++;run.NextAttemptAt=null;run.CompletedAt=null;run.Error=null;
            db.Audits.Add(new(){FamilyId=a.FamilyId,ActorId=a.Id,Action="BuilderManualRetry",Details=Json.Write(new{runId=id,before,reason=input.Reason.Trim(),run.RetryRound})});return TypedResults.Accepted("/api/v1/builder",run);
        });
        api.MapPost("/builder/candidates/{id:guid}:decide",async (Guid id,DecisionInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var c=await db.Candidates.SingleOrDefaultAsync(c => c.Id==id && c.FamilyId==a.FamilyId) ?? throw new ApiError(404,"NOT_FOUND","候选不存在。");
            if (c.Status!="Pending") throw new ApiError(409,"ALREADY_REVIEWED","此候选已经处理。");
            if (string.IsNullOrWhiteSpace(input.Reason)) throw new ApiError(422,"REASON_REQUIRED","请填写审核依据。");
            var chunk=await db.Chunks.SingleAsync(x=>x.Id==c.ChunkId && x.FamilyId==a.FamilyId);
            if(input.Decision!="Reject" && (string.IsNullOrWhiteSpace(c.Quote) || !chunk.Text.Contains(c.Quote,StringComparison.Ordinal)))throw new ApiError(422,"SOURCE_QUOTE_INVALID","候选引用与原始片段不符，不能接受或发布。");
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
                if (string.IsNullOrWhiteSpace(behavior) || string.IsNullOrWhiteSpace(boundary) || string.IsNullOrWhiteSpace(name) || name.Length>100 || behavior.Contains("请审核者补充") || boundary.Contains("请审核者补充")) throw new ApiError(422,"INVALID_DEFINITION","请补充可测行为和边界。");
                var k=new KC(Guid.NewGuid(),Guid.NewGuid(),$"MATH.CUSTOM.{Guid.NewGuid():N}",name,behavior,boundary,c.Type);
                var draft=new ContentDraft { FamilyId=a.FamilyId,Title=$"Builder 审核草稿 · {name}",Payload=Json.Write(new Catalog([k],[],[],[],[])) };db.Drafts.Add(draft);c.CreatedDraftId=draft.Id;c.CreatedKCId=k.Id;c.Status="Accepted";
            }
            else throw new ApiError(422,"INVALID_DECISION","支持新建草稿、关联已有或拒绝。");
            c.Decision=input.Decision;c.ReviewReason=input.Reason;c.ReviewedBy=a.Id;c.ReviewedAt=DateTimeOffset.UtcNow;
            db.Audits.Add(new() { FamilyId=a.FamilyId,ActorId=a.Id,Action="CandidateReview",Details=Json.Write(new { candidateId=id,input.Decision,input.Reason,input.ExistingKCId,input.Name,input.Behavior,input.Boundary,c.CreatedDraftId,c.CreatedKCId }) });
            return TypedResults.Ok(c);
        });
    }
    public static async Task ValidateRun(Database db,BuilderRun run,CancellationToken ct=default)
    {
        var source=await db.Sources.SingleOrDefaultAsync(s=>s.Id==run.SourceId && s.FamilyId==run.FamilyId,ct)??throw new ApiError(422,"SOURCE_UNAVAILABLE","来源已不可用。");
        if(run.Type=="ParsePDF")
        {
            if(run.Provider!="LocalParser" || run.Model!="pdfpig/0.1.16" || run.PromptVersion!="parser/1")throw new ApiError(422,"RUN_CONFIGURATION_UNKNOWN","原解析配置无法恢复，请重新准备任务。");
            if(!await db.Set<PrivateFile>().AnyAsync(f=>f.Id==source.FileId && f.FamilyId==run.FamilyId && f.MimeType=="application/pdf",ct))throw new ApiError(422,"SOURCE_UNAVAILABLE","原始PDF已不可用。");
        }
        else
        {
            if(run.Type!="Candidates" || run.InputVersion is not ("builder-input/2" or "builder-input/3"))throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","原输入快照未记录，请重新准备任务。");
            if(run.Provider!="Mock")throw new ApiError(422,source.AllowExternalAI?"PROVIDER_UNCONFIGURED":"EXTERNAL_AI_DENIED","外部模型尚未配置，不能借重试发送来源。");
            if(run.Model!="fixture/1" || run.PromptVersion!="kc-candidate/1")throw new ApiError(422,"RUN_CONFIGURATION_UNKNOWN","原模型或提示配置无法恢复，请重新准备任务。");
            BuilderConfiguration.Resolve(run);
            if(run.LibraryReleaseId!=null && !await db.Releases.AnyAsync(r=>r.Id==run.LibraryReleaseId && r.FamilyId==run.FamilyId,ct))throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","原正式库输入已不可用。");
            if(run.ModelConfigHash!=null)
            {
                var libraryHash=run.LibraryReleaseId==null?null:await db.Releases.Where(r=>r.Id==run.LibraryReleaseId && r.FamilyId==run.FamilyId).Select(r=>r.Hash).SingleAsync(ct);
                if(run.InputHash!=Content.Hash(source.Hash+":"+run.Provider+":"+run.Model+":"+run.PromptVersion+":"+run.InputVersion+":"+libraryHash+":"+run.ModelConfigHash))throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","建库输入摘要与原来源、正式库或配置不一致。");
            }
            if(string.IsNullOrWhiteSpace(source.Text))throw new ApiError(422,"SOURCE_NOT_READY","来源尚未准备完成。");
        }
    }
    public static async Task ProcessOne(Database db,CancellationToken ct,ILogger? logger=null)
    {
        var stopping=ct;await BackgroundJobs.EnsureBuilderJobs(db,ct);await using var lease=await BackgroundJobs.Claim(db,ct);if(lease==null)return;ct=lease.Token;
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Lock(lease.Job.FamilyId,ct);
            var run=await db.BuilderRuns.SingleOrDefaultAsync(r=>r.Id==lease.Job.InputRef && r.FamilyId==lease.Job.FamilyId,ct);
            if(run==null){await lease.Finish(db,"Failed","JOB_INPUT_MISSING",null,ct);await tx.CommitAsync(ct);return;}
            await db.Entry(run).ReloadAsync(ct);
            if(run.Status!="Queued"){await lease.Finish(db,run.Status=="Completed"?"Succeeded":"Failed",run.Error,null,ct);await tx.CommitAsync(ct);return;}
            if(run.NextAttemptAt>DateTimeOffset.UtcNow){await lease.Finish(db,"Retrying",run.Error,run.NextAttemptAt,ct);await tx.CommitAsync(ct);return;}
            var started=DateTimeOffset.UtcNow;var number=(await db.Set<BuilderAttempt>().Where(a=>a.RunId==run.Id && a.RetryRound==run.RetryRound).MaxAsync(a=>(int?)a.Number,ct)??0)+1;
            var id=run.Id;await tx.CreateSavepointAsync("builder_work",ct);
            try
            {
                if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","后台任务多次中断后已停止，请核对原调用后人工恢复。");
                if(Content.Hash(lease.Job.InputPayload)!=lease.Job.InputHash || BackgroundJobs.Snapshot(run)!=lease.Job.InputPayload)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","后台任务原输入已变化，请重新准备来源，不直接重试。");
                await ValidateRun(db,run,ct);var protocol=await Execute(db,run,number,ct);if(run.Status=="Completed")run.Error=null;if(run.Status!="Queued")run.NextAttemptAt=null;
                var attempt=Attempt(run,number,started,run.Status);attempt.ProtocolResult=protocol==null?null:Json.Write(protocol);db.Add(attempt);await db.SaveChangesAsync(ct);
            }
            catch(Exception ex)when(ex is not OperationCanceledException)
            {
                await tx.RollbackToSavepointAsync("builder_work",ct);db.ChangeTracker.Clear();run=await db.BuilderRuns.SingleAsync(r=>r.Id==id,ct);
                var retry=ex is not ApiError && run.Retries<3;run.Error=ex is ApiError apiError?apiError.Code:"BUILDER_PROCESSING_FAILED";
                if(retry){run.Retries++;run.NextAttemptAt=DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2,run.Retries));run.Status="Queued";run.CompletedAt=null;}
                else{run.Status="Failed";run.NextAttemptAt=null;run.CompletedAt=DateTimeOffset.UtcNow;}
                db.Add(Attempt(run,number,started,retry?"RetryScheduled":"Failed"));await db.SaveChangesAsync(ct);logger?.LogError(ex,"Builder processing failed {RunId}; retry scheduled {RetryScheduled}",id,retry);
            }
            await lease.Finish(db,run.Status=="Completed"?"Succeeded":run.Status=="Queued"?"Retrying":"Failed",run.Error,run.NextAttemptAt,ct);
            await tx.CommitAsync(ct);
        }
        catch(OperationCanceledException)when(lease.Lost && !stopping.IsCancellationRequested){db.ChangeTracker.Clear();logger?.LogWarning("Builder lease lost; uncommitted result discarded {JobId}",lease.Job.Id);}
    }
    static BuilderAttempt Attempt(BuilderRun run,int number,DateTimeOffset started,string status)=>new(){FamilyId=run.FamilyId,RunId=run.Id,RetryRound=run.RetryRound,Number=number,StartedAt=started,FinishedAt=DateTimeOffset.UtcNow,Status=status,ErrorCode=run.Error,NextAttemptAt=run.NextAttemptAt,InputSnapshot=BackgroundJobs.Snapshot(run)};
    static async Task<BuilderProtocolResult?> Execute(Database db,BuilderRun run,int attemptNumber,CancellationToken ct)
    {
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
            run.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);return null;
        }
        var chunks=await db.Chunks.Where(c => c.SourceId==run.SourceId).OrderBy(c => c.Locator).ToListAsync(ct);
        if(run.InputVersion is not ("builder-input/2" or "builder-input/3"))
        {run.Status="Failed";run.Error="INPUT_SNAPSHOT_UNKNOWN";run.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);return null;}
        var release=run.LibraryReleaseId==null?null:await db.Releases.SingleAsync(r=>r.Id==run.LibraryReleaseId && r.FamilyId==run.FamilyId,ct);
        var kcs=release==null ? [] : Json.Read<Catalog>(release.Payload).Kcs;
        foreach (var kc in kcs)
            if (!await db.Set<Embedding>().AnyAsync(e=>e.FamilyId==run.FamilyId && e.EntityRevisionId==kc.RevisionId && e.Space==Retrieval.Space,ct)) db.Add(new Embedding { FamilyId=run.FamilyId,EntityRevisionId=kc.RevisionId,TextHash=Content.Hash(kc.Name+kc.Behavior+kc.Boundary),Vector=Json.Write(Retrieval.Vector(kc.Name+" "+kc.Behavior+" "+kc.Boundary)) });
        var output=await BuilderProtocol.Run(new BuilderCallTracking(db,run,attemptNumber,new MockBuilderCandidateProvider()),chunks.Select(c=>new BuilderFragment(c.Id,c.Text)).ToArray(),BuilderConfiguration.Resolve(run),ct);
        foreach (var candidate in output.Output.Candidates)
        {
            var chunk=chunks.Single(c=>c.Id==candidate.SourceChunkIds[0]);
            db.Candidates.Add(new() { FamilyId=run.FamilyId,RunId=run.Id,ProtocolPayload=Json.Write(candidate),ChunkId=chunk.Id,Name=candidate.Name,Quote=candidate.SupportingQuotes[0],Behavior=candidate.MeasurableBehavior,Boundary=candidate.Boundary,Type=candidate.KcType,SuggestedAction="NeedsReview",Matches=Json.Write(Retrieval.TopK(chunk.Text,kcs)) });
        }
        run.Status="Completed";run.Error=null;run.NextAttemptAt=null;run.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);return output;
    }
}
