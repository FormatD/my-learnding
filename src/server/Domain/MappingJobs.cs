using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Learning;
public class MappingPreparation:Row
{
    public Guid JobId {get;set;}
    public Guid RunId {get;set;}
    public Guid SourceDraftId {get;set;}
    public Guid LibraryReleaseId {get;set;}
    public Guid RequestedBy {get;set;}
    public string SourceTitle {get;set;}="";
    public string InputHash {get;set;}="";
    public string Snapshot {get;set;}="";
    public string SnapshotHash {get;set;}="";
}
public record MappingFrozenInput(string Version,Guid FamilyId,Guid RunId,Guid RequestedBy,Guid SourceDraftId,long SourceDraftVersion,string SourceTitle,string SourcePayload,string SourceHash,Guid LibraryReleaseId,string LibraryPayload,string LibraryHash,string InputHash,string Provider,string Model,string PromptVersion,MappingOwnerSelection[] Selections,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? ModelConfigPayload=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? ModelConfigHash=null);
public record MappingPreparationSummary(Guid Id,Guid JobId,Guid RunId,Guid SourceDraftId,string SourceTitle,Guid LibraryReleaseId,string Status,int AttemptCount,int RetryRound,string? Error,DateTimeOffset CreatedAt);
public static class MappingJobs
{
    public const string Type="MappingSuggestions";
    public static MappingPreparationSummary Summary(MappingPreparation p,BackgroundJob j)=>new(p.Id,j.Id,p.RunId,p.SourceDraftId,p.SourceTitle,p.LibraryReleaseId,j.Status,j.AttemptCount,j.RetryRound,j.LastErrorCode,p.CreatedAt);
    public static async Task<MappingPreparation> Enqueue(Database db,Actor a,MappingRunInput input)
    {
        var local=input.Provider=="LocalOmlx"?MappingLocalConfiguration.Current(db.RuntimeConfiguration):null;
        var (run,selections,_,_,_)=await MappingBuilder.Freeze(db,a,input,local);
        var previous=await db.Set<MappingPreparation>().SingleOrDefaultAsync(p=>p.FamilyId==a.FamilyId && p.InputHash==run.InputHash);if(previous!=null)return previous;
        var existing=await db.Set<MappingRun>().SingleOrDefaultAsync(r=>r.FamilyId==a.FamilyId && r.InputHash==run.InputHash);if(existing!=null)run.Id=existing.Id;
        var library=await db.Releases.SingleAsync(r=>r.Id==run.LibraryReleaseId && r.FamilyId==a.FamilyId);
        var snapshot=Json.Write(new MappingFrozenInput(local==null?"mapping-job/1":"mapping-job/2",a.FamilyId,run.Id,a.Id,run.SourceDraftId,run.SourceDraftVersion,run.SourceTitle,run.SourcePayload,run.SourceHash,run.LibraryReleaseId,library.Payload,run.LibraryHash,run.InputHash,run.Provider,run.Model,run.PromptVersion,selections,local==null?null:Json.Write(local),local==null?null:Content.Hash(Json.Write(local))));
        var p=new MappingPreparation{FamilyId=a.FamilyId,RunId=run.Id,RequestedBy=a.Id,SourceDraftId=run.SourceDraftId,SourceTitle=run.SourceTitle,LibraryReleaseId=run.LibraryReleaseId,InputHash=run.InputHash,Snapshot=snapshot,SnapshotHash=Content.Hash(snapshot)};
        var job=new BackgroundJob{FamilyId=a.FamilyId,Type=Type,InputRef=p.Id,IdempotencyKey="mapping:"+p.Id,InputPayload=snapshot,InputHash=p.SnapshotHash};p.JobId=job.Id;db.AddRange(p,job);
        db.Audits.Add(new(){FamilyId=a.FamilyId,ActorId=a.Id,Action="MappingPreparationQueued",Details=Json.Write(new{preparationId=p.Id,p.JobId,p.RunId,p.SourceDraftId,p.LibraryReleaseId,p.InputHash,p.SnapshotHash})});return p;
    }
    // Shared by capture and result commit: permissions and withdrawn libraries must be checked again after a model wait.
    public static async Task<(MappingPreparation Preparation,MappingFrozenInput Input,Catalog Source,KC[] Library,Guid AuthorizedBy)> ValidateFrozen(Database db,JobLease lease,CancellationToken ct)
    {
        var p=await db.Set<MappingPreparation>().AsNoTracking().SingleOrDefaultAsync(p=>p.Id==lease.Job.InputRef && p.FamilyId==lease.Job.FamilyId,ct)??throw new ApiError(422,"JOB_INPUT_MISSING","映射任务原输入不存在。");
        if(p.JobId!=lease.Job.Id || p.Snapshot!=lease.Job.InputPayload || p.SnapshotHash!=lease.Job.InputHash || Content.Hash(p.Snapshot)!=p.SnapshotHash)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","映射任务原输入摘要不一致，请重新准备。");
        var f=Json.Read<MappingFrozenInput>(p.Snapshot);
        if(f.FamilyId!=p.FamilyId || f.RunId!=p.RunId || f.RequestedBy!=p.RequestedBy || f.SourceDraftId!=p.SourceDraftId || f.LibraryReleaseId!=p.LibraryReleaseId || f.InputHash!=p.InputHash || f.SourceTitle!=p.SourceTitle || Content.Hash(f.SourcePayload)!=f.SourceHash || Content.Hash(f.LibraryPayload)!=f.LibraryHash)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","映射任务版本或固定输入不一致。");
        if(f.Provider=="LocalOmlx")ResolveLocal(f);
        else if(f.Version!="mapping-job/1" || f.ModelConfigPayload!=null || f.ModelConfigHash!=null || f.Provider is not ("Mock" or "Manual") || f.Model!=(f.Provider=="Manual"?"None":Retrieval.Space) || f.PromptVersion!=(f.Provider=="Manual"?"manual-source/1":"mapping-suggestion/1"))throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","映射任务版本或固定输入不一致。");
        var authorizedBy=p.RequestedBy;var recoveryAuthorized=lease.Job.RetryRound==0;
        if(lease.Job.RetryRound>0)
        {
            var audits=await db.Audits.AsNoTracking().Where(a=>a.FamilyId==p.FamilyId && a.Action=="MappingPreparationManualRetry" && a.Details.Contains(p.Id.ToString())).OrderByDescending(a=>a.CreatedAt).ThenBy(a=>a.Id).ToArrayAsync(ct);
            foreach(var audit in audits)
            {
                using var doc=JsonDocument.Parse(audit.Details);var data=doc.RootElement;
                if(data.GetProperty("preparationId").GetGuid()==p.Id && data.GetProperty("jobId").GetGuid()==lease.Job.Id && data.GetProperty("retryRound").GetInt32()==lease.Job.RetryRound && data.GetProperty("runId").GetGuid()==p.RunId && data.GetProperty("snapshotHash").GetString()==p.SnapshotHash){authorizedBy=audit.ActorId;recoveryAuthorized=true;break;}
            }
        }
        if(!recoveryAuthorized)throw new ApiError(422,"JOB_RETRY_AUTHORIZATION_MISSING","缺少本轮人工恢复依据，请核对后台任务记录。");
        var roles=(await db.Set<FamilyMembership>().AsNoTracking().SingleOrDefaultAsync(m=>m.FamilyId==p.FamilyId && m.AccountId==authorizedBy,ct))?.Roles??"";
        if(!new Actor(Guid.Empty,p.FamilyId,authorizedBy,null,"Parent",roles).Can("ContentEditor"))throw new ApiError(422,"JOB_REQUESTER_FORBIDDEN","内容维护权限已变化，请由有权限成员核对原输入后重新处理。");
        var release=await db.Releases.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==f.LibraryReleaseId && r.FamilyId==p.FamilyId,ct)??throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","冻结能力库不可用。");
        if(release.Withdrawn)throw new ApiError(422,"LIBRARY_WITHDRAWN","冻结能力库已撤回，请重新准备映射建议。");
        if(Content.Hash(release.Payload)!=f.LibraryHash)throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","冻结能力库摘要不一致。");
        var source=Json.Read<Catalog>(f.SourcePayload);MappingBuilder.Shape(source);var library=Json.Read<Catalog>(f.LibraryPayload).Kcs;
        if(f.Selections==null || f.Selections.Length is <1 or >100 || library.Length is <1 or >1000)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","固定选择超出本地处理范围。");
        var expected=InputHash(f.SourceDraftId,f.SourceDraftVersion,f.SourceHash,f.LibraryReleaseId,f.LibraryHash,f.Selections,f.Provider,f.Model,f.PromptVersion,f.ModelConfigHash);
        if(expected!=f.InputHash)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","固定映射选择与输入摘要不一致。");
        if(f.Provider=="LocalOmlx")MappingModelProtocol.Capture(source,f.SourceDraftId,f.Selections,library);
        return(p,f,source,library,authorizedBy);
    }
    // Preserve the exact v1 hash recipe for historical Mock/Manual snapshots.
    public static string InputHash(Guid id,long version,string sourceHash,Guid releaseId,string libraryHash,MappingOwnerSelection[] selections,string provider,string model,string promptVersion,string? modelConfigHash=null)=>modelConfigHash==null?
        Content.Hash(Json.Write(new{id,version,sourceHash,releaseId,libraryHash,selections,provider,model,promptVersion})):
        Content.Hash(Json.Write(new{id,version,sourceHash,releaseId,libraryHash,selections,provider,model,promptVersion,modelConfigHash}));
    public static MappingLocalConfiguration ResolveLocal(MappingFrozenInput input)
    {
        if(input.Version!="mapping-job/2" || input.Provider!="LocalOmlx" || input.ModelConfigPayload==null || Content.Hash(input.ModelConfigPayload)!=input.ModelConfigHash || input.PromptVersion!=MappingModelProtocol.Version)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","本机映射任务配置摘要不一致。");
        MappingLocalConfiguration configuration;
        try{configuration=Json.Read<MappingLocalConfiguration>(input.ModelConfigPayload)??throw new JsonException("Missing frozen configuration");}
        catch(JsonException){throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","本机映射任务固定配置无法读取。");}
        configuration.Validate();if(configuration.Model!=input.Model)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","本机映射任务固定模型不一致。");return configuration;
    }
    sealed record PreparedLocal(string Snapshot,MappingModelEnvelope? Output,Exception? Error);
    static bool LocalJob(BackgroundJob job)
    {
        try{using var doc=JsonDocument.Parse(job.InputPayload);return doc.RootElement.GetProperty("provider").GetString()=="LocalOmlx";}
        catch(Exception ex)when(ex is JsonException or InvalidOperationException or KeyNotFoundException){return false;}
    }
    static async Task<PreparedLocal> PrepareLocal(Database db,JobLease lease,IMappingModelProvider? provider,CancellationToken ct)
    {
        MappingPreparation preparation;MappingFrozenInput frozen;MappingModelInput input;
        try
        {
            await using(var tx=await db.Database.BeginTransactionAsync(ct))
            {
                await db.Lock(lease.Job.FamilyId,ct);
                if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","映射任务多次中断后已停止，请核对原调用后恢复。");
                var captured=await ValidateFrozen(db,lease,ct);preparation=captured.Preparation;frozen=captured.Input;
                input=MappingModelProtocol.Capture(captured.Source,frozen.SourceDraftId,frozen.Selections,captured.Library);
                await tx.CommitAsync(ct);
            }
            db.ChangeTracker.Clear();var config=ResolveLocal(frozen);
            var transport=provider??new LocalMappingModelProvider(config,db.RuntimeConfiguration["Omlx:ApiKeyFile"]??"");
            var response=await new MappingModelCallTracking(db,preparation,config,lease,transport).Generate(input,ct);
            if(!response.OutputComplete)throw new ApiError(422,"LOCAL_PROVIDER_OUTPUT_INCOMPLETE","本机模型返回截断内容，未保存建议。");
            return new(preparation.Snapshot,MappingModelProtocol.Validate(response.Output,input),null);
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return new(lease.Job.InputPayload,null,new ApiError(422,"LOCAL_PROVIDER_TIMEOUT","本机模型调用超时，请核对调用结束情况后人工恢复。"));}
        catch(Exception ex)when(ex is not OperationCanceledException){db.ChangeTracker.Clear();return new(lease.Job.InputPayload,null,ex);}
    }
    public static async Task ProcessOne(Database db,CancellationToken stopping=default,IMappingModelProvider? provider=null)
    {
        db.ChangeTracker.Clear();
        await using var lease=await BackgroundJobs.Claim(db,stopping,[Type]);if(lease==null)return;var ct=lease.Token;
        try
        {
            var prepared=LocalJob(lease.Job)?await PrepareLocal(db,lease,provider,ct):null;
            await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Lock(lease.Job.FamilyId,ct);await tx.CreateSavepointAsync("mapping_work",ct);
            try
            {
                if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","映射任务多次中断后已停止，请核对原输入后人工恢复。");
                var (p,f,source,library,authorizedBy)=await ValidateFrozen(db,lease,ct);
                if(prepared?.Error!=null)throw prepared.Error;
                if(f.Provider=="LocalOmlx" && (prepared?.Output==null || prepared.Snapshot!=p.Snapshot))throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","本机映射结果与原任务输入不一致。");
                var run=await db.Set<MappingRun>().SingleOrDefaultAsync(r=>r.Id==f.RunId && r.FamilyId==p.FamilyId,ct);
                if(run!=null)
                {
                    if(run.InputHash!=f.InputHash || run.SourceHash!=f.SourceHash || run.LibraryHash!=f.LibraryHash || run.Provider!=f.Provider || run.Model!=f.Model || run.PromptVersion!=f.PromptVersion || run.Status!="Completed")throw new ApiError(422,"MAPPING_OUTPUT_CONFLICT","固定运行标识已有不同结果，请核对原运行。");
                }
                else
                {
                    run=new MappingRun{Id=f.RunId,FamilyId=p.FamilyId,SourceDraftId=f.SourceDraftId,SourceDraftVersion=f.SourceDraftVersion,SourceTitle=f.SourceTitle,SourcePayload=f.SourcePayload,SourceHash=f.SourceHash,LibraryReleaseId=f.LibraryReleaseId,LibraryHash=f.LibraryHash,InputHash=f.InputHash,Provider=f.Provider,Model=f.Model,PromptVersion=f.PromptVersion};
                    db.Add(run);
                    if(f.Provider=="LocalOmlx")StoreLocal(db,run,source,prepared!.Output!,ct);
                    else MappingBuilder.Generate(db,run,source,library,f.Selections.Select(o=>MappingSuggestions.Owner(source,o)).ToArray(),ct);
                    run.CompletedAt=DateTimeOffset.UtcNow;
                    db.Audits.Add(new(){FamilyId=p.FamilyId,ActorId=authorizedBy,Action="MappingSuggestionsPrepared",Details=Json.Write(new{preparationId=p.Id,jobId=lease.Job.Id,runId=run.Id,originalRequestedBy=p.RequestedBy,authorizedBy,run.SourceDraftId,run.SourceDraftVersion,run.LibraryReleaseId,run.InputHash,owners=f.Selections.Length,provider=run.Provider})});
                }
                await lease.Finish(db,"Succeeded",null,null,ct);await tx.CommitAsync(ct);
            }
            catch(Exception ex)when(ex is not OperationCanceledException)
            {
                await tx.RollbackToSavepointAsync("mapping_work",ct);db.ChangeTracker.Clear();var retry=prepared==null && ex is not ApiError && lease.Job.AttemptCount<lease.Job.MaxAttempts;var code=ex is ApiError a?a.Code:"MAPPING_PROCESSING_FAILED";
                await lease.Finish(db,retry?"Retrying":"Failed",code,retry?DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2,lease.Job.AttemptCount)):null,ct);await tx.CommitAsync(ct);
            }
        }
        catch(OperationCanceledException)when(lease.Lost && !stopping.IsCancellationRequested){db.ChangeTracker.Clear();}
    }
    static void StoreLocal(Database db,MappingRun run,Catalog source,MappingModelEnvelope envelope,CancellationToken ct)
    {
        foreach(var result in envelope.Results)
        {
            ct.ThrowIfCancellationRequested();var owner=MappingSuggestions.Owner(source,new(result.OwnerType,result.OwnerId,result.OwnerRevisionId));
            db.Add(new MappingSuggestion{FamilyId=run.FamilyId,RunId=run.Id,OwnerType=owner.OwnerType,OwnerId=owner.Id,OwnerRevisionId=owner.RevisionId,OwnerTitle=owner.Title,EvidencePolicy=result.Proposal.EvidencePolicy,SuggestedItems=Json.Write(result.Proposal.Items),ModelResultPayload=Json.Write(result),ValidationFlags=Json.Write(new[]{"LocalModelUnreviewed",result.Decision=="NeedsReview"?"NeedsReview":"HumanReviewRequired","NotQualityEvaluated"})});
        }
    }
    public static async Task<MappingPreparationSummary> Retry(Database db,Actor a,Guid id,string reason)
    {
        a.Require("ContentEditor");if(string.IsNullOrWhiteSpace(reason) || reason.Length>4000)throw new ApiError(422,"REASON_REQUIRED","请填写重新处理的依据，最多4000字。");var p=await db.Set<MappingPreparation>().SingleOrDefaultAsync(p=>p.Id==id && p.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","映射准备任务不存在。");var j=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId && j.FamilyId==a.FamilyId);if(j.Status is not ("Failed" or "Cancelled"))throw new ApiError(409,"RUN_NOT_FAILED","只可重新处理已停止的失败任务。");if(j.LastErrorCode is not ("MAPPING_PROCESSING_FAILED" or "JOB_ATTEMPTS_EXHAUSTED" or "JOB_REQUESTER_FORBIDDEN" or "JOB_CANCELLED_BY_USER" or "BUILDER_CONCURRENCY_LIMIT" or "BUILDER_USAGE_RECONCILIATION_REQUIRED" or "BUILDER_CALL_BUDGET_LIMIT" or "BUILDER_DAILY_BUDGET_LIMIT" or "LOCAL_PROVIDER_TIMEOUT" or "LOCAL_PROVIDER_HTTP_FAILED" or "LOCAL_PROVIDER_AUTH_FAILED" or "LOCAL_PROVIDER_KEY_REQUIRED" or "LOCAL_PROVIDER_KEY_NOT_PRIVATE" or "LOCAL_PROVIDER_OUTPUT_INCOMPLETE" or "LOCAL_PROVIDER_RESPONSE_INVALID" or "LOCAL_PROVIDER_MODEL_MISMATCH" or "MAPPING_MODEL_OUTPUT_LIMIT" or "MAPPING_MODEL_SCHEMA_INVALID" or "MAPPING_MODEL_SOURCE_INVALID" or "MAPPING_MODEL_MAPPING_INVALID"))throw new ApiError(422,"RUN_RECREATE_REQUIRED","原输入或能力库需核对，请重新准备建议。");j.Status="Queued";j.AttemptCount=0;j.RetryRound++;j.LastErrorCode=null;j.NextRunAt=null;db.Audits.Add(new(){FamilyId=a.FamilyId,ActorId=a.Id,Action="MappingPreparationManualRetry",Details=Json.Write(new{preparationId=p.Id,jobId=j.Id,j.RetryRound,reason=reason.Trim(),p.RunId,p.SnapshotHash})});return Summary(p,j);
    }
    public static void Map(RouteGroupBuilder api)
    {
        MappingModelCallTracking.Map(api);
        api.MapPost("/builder/mapping-preparations",async(MappingRunInput input,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("ContentEditor");var p=await Enqueue(db,a,input);var job=db.Set<BackgroundJob>().Local.FirstOrDefault(j=>j.Id==p.JobId)??await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId && j.FamilyId==a.FamilyId);return TypedResults.Accepted("/api/v1/builder/mapping-preparations/"+p.Id,Summary(p,job));});
        api.MapGet("/builder/mapping-preparations",async(Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("ContentEditor");var rows=await(from p in db.Set<MappingPreparation>() join j in db.Set<BackgroundJob>() on p.JobId equals j.Id where p.FamilyId==a.FamilyId && j.FamilyId==a.FamilyId orderby p.CreatedAt descending,p.Id select new{p,j}).Take(50).ToArrayAsync();return rows.Select(r=>Summary(r.p,r.j)).ToArray();});
        api.MapGet("/builder/mapping-preparations/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("ContentEditor");var p=await db.Set<MappingPreparation>().SingleOrDefaultAsync(p=>p.Id==id && p.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","映射准备任务不存在。");return Summary(p,await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId && j.FamilyId==a.FamilyId));});
        api.MapPost("/builder/mapping-preparations/{id:guid}:retry",async(Guid id,ReasonInput input,Database db,HttpContext ctx)=>TypedResults.Accepted("/api/v1/builder/mapping-preparations/"+id,await Retry(db,ctx.Actor(),id,input.Reason)));
    }
}
