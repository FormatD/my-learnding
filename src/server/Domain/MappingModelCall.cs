using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
namespace Learning;

public class MappingModelCall:Row
{
    public Guid PreparationId {get;set;}
    public Guid ExecutionId {get;set;}
    public int RetryRound {get;set;}
    public int AttemptNumber {get;set;}
    public int CallNumber {get;set;}=1;
    public string Model {get;set;}="";
    public string InputHash {get;set;}="";
    public string ModelConfigHash {get;set;}="";
    public string ModelConfigPayload {get;set;}="";
    public string InputPayload {get;set;}="";
    public string Status {get;set;}="Started";
    public DateTimeOffset StartedAt {get;set;}=DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt {get;set;}
    public long? ElapsedMilliseconds {get;set;}
    public long? InputTokens {get;set;}
    public long? OutputTokens {get;set;}
    public decimal? ChargedCost {get;set;}
    public string? Currency {get;set;}
    public string BillingStatus {get;set;}="Unknown";
    public string? OutputHash {get;set;}
    public string? ErrorCode {get;set;}
    public DateOnly BudgetDay {get;set;}
    public string BudgetSnapshot {get;set;}="";
    public string BudgetState {get;set;}="Reserved";
}
public class MappingCallReconciliation:Row
{
    public Guid CallId {get;set;}
    public Guid ActorId {get;set;}
    public long? InputTokens {get;set;}
    public long? OutputTokens {get;set;}
    public string Reason {get;set;}="";
    public string ReceiptReference {get;set;}="";
}
public record MappingCallReconciliationInput(bool ProviderFinished,long? InputTokens,long? OutputTokens,string Reason,string ReceiptReference);

// A separate physical-call ledger references the queued mapping preparation, never a fabricated BuilderRun.
public sealed class MappingModelCallTracking(Database owner,MappingPreparation preparation,MappingLocalConfiguration frozen,JobLease lease,IMappingModelProvider inner):IMappingModelProvider
{
    readonly Guid executionId=Guid.NewGuid();int number;
    Database Open()=>BackgroundJobs.Open(owner);
    public async Task<BuilderProviderResponse> Generate(MappingModelInput input,CancellationToken ct)
    {
        frozen.Validate();MappingModelProtocol.ValidateInput(input);
        if(lease.Job.Type!=MappingJobs.Type || lease.Job.FamilyId!=preparation.FamilyId || lease.Job.InputRef!=preparation.Id || lease.Job.Id!=preparation.JobId || preparation.Snapshot!=lease.Job.InputPayload || preparation.SnapshotHash!=lease.Job.InputHash || Content.Hash(preparation.Snapshot)!=preparation.SnapshotHash)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","映射调用与所领取任务的冻结输入不一致。");
        if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","后台任务已达中断上限，未开始模型调用。");
        var snapshot=Json.Read<MappingFrozenInput>(preparation.Snapshot);
        if(snapshot.Version=="mapping-job/2" && Json.Write(MappingJobs.ResolveLocal(snapshot))!=Json.Write(frozen))throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","映射调用配置与原任务冻结配置不一致。");var expected=MappingModelProtocol.Capture(Json.Read<Catalog>(snapshot.SourcePayload),snapshot.SourceDraftId,snapshot.Selections,Json.Read<Catalog>(snapshot.LibraryPayload).Kcs);
        if(MappingModelProtocol.UserFor(expected)!=MappingModelProtocol.UserFor(input))throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","映射调用内容与原任务来源、对象或能力库不一致。");
        var call=new MappingModelCall{FamilyId=preparation.FamilyId,PreparationId=preparation.Id,ExecutionId=executionId,RetryRound=lease.Job.RetryRound,AttemptNumber=lease.Job.AttemptCount,CallNumber=++number,Model=frozen.Model,InputHash=Content.Hash(MappingModelProtocol.UserFor(input)),ModelConfigHash=Content.Hash(Json.Write(frozen)),ModelConfigPayload=Json.Write(frozen),InputPayload=MappingModelProtocol.UserFor(input)};
        await using(var db=Open())await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            await BuilderBudget.Lock(db,call.FamilyId,ct);await lease.PermitCall(db,ct);
            var policy=await db.Set<BuilderBudgetPolicy>().SingleOrDefaultAsync(p=>p.FamilyId==call.FamilyId,ct)??new BuilderBudgetPolicy{FamilyId=call.FamilyId};if(db.Entry(policy).State==EntityState.Detached)db.Add(policy);BuilderBudget.Validate(policy);
            call.BudgetDay=DateOnly.FromDateTime(DateTime.UtcNow);call.BudgetSnapshot=Json.Write(policy);var denied=BuilderBudget.Denial(policy,await BuilderBudget.State(db,call.FamilyId,call.BudgetDay,ct),BuilderQuote.Local);
            if(denied!=null){call.Status="Denied";call.BudgetState="Denied";call.ErrorCode=denied;call.FinishedAt=DateTimeOffset.UtcNow;}
            db.Add(call);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            if(denied!=null)throw new ApiError(422,denied,"家庭模型并发名额或预算不足，请先核对尚未确认结束的调用。");
        }
        var watch=Stopwatch.StartNew();
        try
        {
            var response=await inner.Generate(input,ct).WaitAsync(ct);BuilderCallTracking.ValidateUsage(response.Usage);
            if(response.Usage is {} usage && (usage.ChargedCost!=0 || usage.Currency!=null || usage.BillingStatus is not ("LocalMeasured" or "LocalNoCharge")))throw new ApiError(422,"BUILDER_USAGE_INVALID","本机映射不得记录未经验证的外部计费值。");
            await Finish(call.Id,"Returned",response,response.OutputComplete?null:"LOCAL_PROVIDER_OUTPUT_INCOMPLETE",watch.ElapsedMilliseconds);return response;
        }
        catch(Exception ex){await Finish(call.Id,ex is OperationCanceledException?"Cancelled":"Failed",null,ex is ApiError api?api.Code:ex is OperationCanceledException?"CALL_CANCELLED":"PROVIDER_CALL_FAILED",watch.ElapsedMilliseconds);throw;}
    }
    async Task Finish(Guid id,string status,BuilderProviderResponse? response,string? error,long elapsed)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync(timeout.Token);await BuilderBudget.Lock(db,preparation.FamilyId,timeout.Token);
        var call=await db.Set<MappingModelCall>().SingleOrDefaultAsync(c=>c.Id==id && c.FamilyId==preparation.FamilyId,timeout.Token);if(call==null || call.Status!="Started")return;
        call.Status=status;call.FinishedAt=DateTimeOffset.UtcNow;call.ElapsedMilliseconds=elapsed;call.ErrorCode=error;call.BudgetState="Unresolved";
        if(response!=null){call.OutputHash=Content.Hash(response.Output);call.InputTokens=response.Usage?.InputTokens;call.OutputTokens=response.Usage?.OutputTokens;call.ChargedCost=response.Usage?.ChargedCost;call.Currency=response.Usage?.Currency;call.BillingStatus=response.Usage?.BillingStatus??"Unknown";if(call.BillingStatus is "LocalMeasured" or "LocalNoCharge")call.BudgetState="Settled";}
        await db.SaveChangesAsync(timeout.Token);await tx.CommitAsync(timeout.Token);
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/builder/mapping-calls",async(int? page,int? pageSize,Guid? preparationId,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var p=page??1;var size=pageSize??20;if(p is <1 or >100_000 || size is <1 or >50)throw new ApiError(422,"INVALID_PAGE","请使用有效页码和每页1～50条。");
            await using var snapshot=await ReadSnapshot.Begin(db,ctx);
            if(preparationId!=null && !await db.Set<MappingPreparation>().AnyAsync(x=>x.Id==preparationId && x.FamilyId==a.FamilyId))throw new ApiError(404,"NOT_FOUND","找不到本家庭映射任务。");
            var query=db.Set<MappingModelCall>().AsNoTracking().Where(c=>c.FamilyId==a.FamilyId && (preparationId==null || c.PreparationId==preparationId));var total=await query.CountAsync();var calls=await query.OrderByDescending(c=>c.StartedAt).ThenBy(c=>c.Id).Skip((p-1)*size).Take(size).ToArrayAsync();var ids=calls.Select(c=>c.Id).ToArray();var reconciliations=await db.Set<MappingCallReconciliation>().AsNoTracking().Where(r=>r.FamilyId==a.FamilyId && ids.Contains(r.CallId)).ToArrayAsync();await snapshot.CommitAsync();return new{page=p,pageSize=size,total,calls,reconciliations};
        });
        api.MapPost("/builder/mapping-calls/{id:guid}:reconcile",async(Guid id,MappingCallReconciliationInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();await BuilderBudget.Owner(db,a);await BuilderBudget.Lock(db,a.FamilyId);var call=await db.Set<MappingModelCall>().SingleOrDefaultAsync(c=>c.Id==id && c.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到本家庭映射调用。");
            if(call.Status=="Denied" || call.BudgetState=="Settled" || await db.Set<MappingCallReconciliation>().AnyAsync(r=>r.CallId==id))throw new ApiError(409,"CALL_ALREADY_RESOLVED","调用已确认或无需核对，原记录继续保留。");
            if(!input.ProviderFinished || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>4000 || string.IsNullOrWhiteSpace(input.ReceiptReference) || input.ReceiptReference.Length>500 || input.InputTokens<0 || input.OutputTokens<0)throw new ApiError(422,"RECONCILIATION_REQUIRED","请确认本机调用已结束，填写原因和进程依据；未知Token可保留空值。");
            if(call.InputTokens!=null && input.InputTokens!=call.InputTokens || call.OutputTokens!=null && input.OutputTokens!=call.OutputTokens)throw new ApiError(422,"RECONCILIATION_CONFLICT","真实返回的Token不能改写。");
            var decision=new MappingCallReconciliation{FamilyId=a.FamilyId,CallId=id,ActorId=a.AccountId!.Value,InputTokens=input.InputTokens,OutputTokens=input.OutputTokens,Reason=input.Reason.Trim(),ReceiptReference=input.ReceiptReference.Trim()};db.Add(decision);return TypedResults.Created($"/api/v1/builder/mapping-calls/{id}",decision);
        });
    }
}
