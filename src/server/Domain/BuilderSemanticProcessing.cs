using Microsoft.EntityFrameworkCore;
namespace Learning;

// Independent raw receipt survives validation failure or a stale business result.
public class BuilderSemanticResponse:Row
{
    public Guid CallId {get;set;}
    public string OutputPayload {get;set;}="";
    public string OutputHash {get;set;}="";
    public bool OutputComplete {get;set;}
}
// An immutable model suggestion, never an artificial human review or Candidate update.
public class BuilderSemanticSuggestion:Row
{
    public Guid PreparationId {get;set;}
    public Guid ResponseId {get;set;}
    public string ResultPayload {get;set;}="";
    public string InputHash {get;set;}="";
}
public static class BuilderSemanticProcessing
{
    sealed record Prepared(Guid ResponseId,string Snapshot,BuilderSemanticResult? Result,Exception? Error);
    static async Task<Prepared> Generate(Database db,JobLease lease,IBuilderSemanticProvider? provider,CancellationToken ct)
    {
        try
        {
            BuilderSemanticPreparation preparation;BuilderSemanticFrozenInput frozen;BuilderSemanticInput input;
            await using(var tx=await db.Database.BeginTransactionAsync(ct))
            {
                await db.Lock(lease.Job.FamilyId,ct);
                if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","语义任务多次中断，需先核对原调用。");
                var captured=await BuilderSemanticJobs.ValidateFrozen(db,lease,ct);preparation=captured.Preparation;frozen=captured.Frozen;input=captured.Input;await tx.CommitAsync(ct);
            }
            db.ChangeTracker.Clear();var config=BuilderSemanticJobs.ResolveLocal(frozen);
            // A committed physical return can outlive a crashed result transaction.
            // Reuse its exact bytes within the same fixed input/retry round, never repair its output.
            var saved=await (from r in db.Set<BuilderSemanticResponse>().AsNoTracking() join c in db.Set<BuilderSemanticCall>().AsNoTracking() on r.CallId equals c.Id where r.FamilyId==preparation.FamilyId && c.FamilyId==preparation.FamilyId && c.PreparationId==preparation.Id && c.RetryRound==lease.Job.RetryRound && c.Status=="Returned" && c.InputPayload==frozen.ModelInputPayload && c.ModelConfigPayload==frozen.ModelConfigPayload orderby c.StartedAt descending,c.Id select r).FirstOrDefaultAsync(ct);
            if(saved!=null)
            {
                if(!saved.OutputComplete)throw new ApiError(422,"LOCAL_PROVIDER_OUTPUT_INCOMPLETE","原模型返回截断内容，未形成语义建议。");
                if(Content.Hash(saved.OutputPayload)!=saved.OutputHash)throw new ApiError(422,"SEMANTIC_RESPONSE_CONFLICT","原返回回执摘要不一致。");
                return new(saved.Id,preparation.Snapshot,BuilderSemanticProtocol.Validate(saved.OutputPayload,input),null);
            }
            var tracker=new BuilderSemanticCallTracking(db,preparation,config,lease,provider??new LocalBuilderSemanticProvider(config,db.RuntimeConfiguration["Omlx:ApiKeyFile"]??""));
            var response=await tracker.Generate(input,ct);
            if(!response.OutputComplete)throw new ApiError(422,"LOCAL_PROVIDER_OUTPUT_INCOMPLETE","模型返回截断内容，未形成语义建议。");
            var result=BuilderSemanticProtocol.Validate(response.Output,input);
            return new(tracker.ResponseId??throw new ApiError(422,"SEMANTIC_RESPONSE_MISSING","原模型返回回执不存在。"),preparation.Snapshot,result,null);
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return new(Guid.Empty,lease.Job.InputPayload,null,new ApiError(422,"LOCAL_PROVIDER_TIMEOUT","模型等待超时，请核对实际调用是否结束。"));}
        catch(Exception ex)when(ex is not OperationCanceledException){db.ChangeTracker.Clear();return new(Guid.Empty,lease.Job.InputPayload,null,ex);}
    }
    public static async Task ProcessOne(Database db,CancellationToken stopping=default,IBuilderSemanticProvider? provider=null)
    {
        db.ChangeTracker.Clear();await using var lease=await BackgroundJobs.Claim(db,stopping,[BuilderSemanticJobs.Type]);if(lease==null)return;var ct=lease.Token;
        try
        {
            var prepared=await Generate(db,lease,provider,ct);
            await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Lock(lease.Job.FamilyId,ct);await tx.CreateSavepointAsync("semantic_work",ct);
            try
            {
                if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","语义任务多次中断，需先核对原调用。");
                var captured=await BuilderSemanticJobs.ValidateFrozen(db,lease,ct);
                if(prepared.Error!=null)throw prepared.Error;
                if(prepared.Result==null || prepared.Snapshot!=captured.Preparation.Snapshot)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","原语义输入已变化。");
                var receipt=await db.Set<BuilderSemanticResponse>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==prepared.ResponseId && r.FamilyId==lease.Job.FamilyId,ct)??throw new ApiError(422,"SEMANTIC_RESPONSE_MISSING","原模型返回不存在。");
                var call=await db.Set<BuilderSemanticCall>().AsNoTracking().SingleAsync(c=>c.Id==receipt.CallId && c.FamilyId==receipt.FamilyId,ct);
                if(call.PreparationId!=captured.Preparation.Id || call.Status!="Returned" || call.RetryRound!=lease.Job.RetryRound || call.AttemptNumber>lease.Job.AttemptCount || call.InputPayload!=captured.Frozen.ModelInputPayload || call.ModelConfigPayload!=captured.Frozen.ModelConfigPayload || call.OutputHash!=receipt.OutputHash || Content.Hash(receipt.OutputPayload)!=receipt.OutputHash || !receipt.OutputComplete || Json.Write(BuilderSemanticProtocol.Validate(receipt.OutputPayload,captured.Input))!=Json.Write(prepared.Result))throw new ApiError(422,"SEMANTIC_RESPONSE_CONFLICT","返回回执与原任务或已核对结果不一致。");
                if(await db.Set<BuilderSemanticSuggestion>().AnyAsync(s=>s.PreparationId==captured.Preparation.Id,ct))throw new ApiError(409,"SEMANTIC_RESULT_CONFLICT","原准备任务已有建议，请核对原记录。");
                var suggestion=new BuilderSemanticSuggestion{FamilyId=receipt.FamilyId,PreparationId=captured.Preparation.Id,ResponseId=receipt.Id,ResultPayload=Json.Write(prepared.Result),InputHash=captured.Preparation.InputHash};db.Add(suggestion);
                db.Audits.Add(new(){FamilyId=receipt.FamilyId,ActorId=captured.AuthorizedBy,Action="BuilderSemanticSuggestionPrepared",Details=Json.Write(new{preparationId=captured.Preparation.Id,jobId=lease.Job.Id,suggestionId=suggestion.Id,responseId=receipt.Id,callId=call.Id,prepared.Result.Decision,inputHash=suggestion.InputHash})});
                // The suggestion, audit and actual lease terminal receipt commit together.
                await lease.Finish(db,"Succeeded",null,null,ct);await tx.CommitAsync(ct);
            }
            catch(Exception ex)when(ex is not OperationCanceledException)
            {
                await tx.RollbackToSavepointAsync("semantic_work",ct);db.ChangeTracker.Clear();await lease.Finish(db,"Failed",ex is ApiError a?a.Code:"SEMANTIC_PROCESSING_FAILED",null,ct);await tx.CommitAsync(ct);
            }
        }
        catch(OperationCanceledException)when(lease.Lost && !stopping.IsCancellationRequested){db.ChangeTracker.Clear();}
    }
}
