using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
namespace Learning;

public class BuilderSemanticCall:Row
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
public class BuilderSemanticReconciliation:Row
{
    public Guid CallId {get;set;}
    public Guid ActorId {get;set;}
    public long? InputTokens {get;set;}
    public long? OutputTokens {get;set;}
    public string Reason {get;set;}="";
    public string ReceiptReference {get;set;}="";
}
public record BuilderSemanticReconciliationInput(bool ProviderFinished,long? InputTokens,long? OutputTokens,string Reason,string ReceiptReference);

// Physical semantic calls reference a real preparation, independently of candidate extraction or review.
public sealed class BuilderSemanticCallTracking(Database owner,BuilderSemanticPreparation preparation,BuilderSemanticLocalConfiguration frozen,JobLease lease,IBuilderSemanticProvider inner):IBuilderSemanticProvider
{
    readonly Guid executionId=Guid.NewGuid();int number;
    Database Open()=>BackgroundJobs.Open(owner);
    public async Task<BuilderProviderResponse> Generate(BuilderSemanticInput input,CancellationToken ct)
    {
        frozen.Validate();BuilderSemanticProtocol.ValidateInput(input);
        if(lease.Job.Type!=BuilderSemanticJobs.Type || lease.Job.FamilyId!=preparation.FamilyId || lease.Job.InputRef!=preparation.Id || lease.Job.Id!=preparation.JobId || preparation.Snapshot!=lease.Job.InputPayload || preparation.SnapshotHash!=lease.Job.InputHash || Content.Hash(preparation.Snapshot)!=preparation.SnapshotHash)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","语义调用与所领取任务的冻结输入不一致。");
        if(!lease.CanExecute)throw new ApiError(422,"JOB_ATTEMPTS_EXHAUSTED","后台任务已达中断上限，未开始模型调用。");
        var snapshot=Json.Read<BuilderSemanticFrozenInput>(preparation.Snapshot);
        if(Json.Write(BuilderSemanticJobs.ResolveLocal(snapshot))!=Json.Write(frozen) || snapshot.ModelInputPayload!=BuilderSemanticProtocol.UserFor(input))throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","语义调用输入/配置与原任务不一致。");
        var call=new BuilderSemanticCall{FamilyId=preparation.FamilyId,PreparationId=preparation.Id,ExecutionId=executionId,RetryRound=lease.Job.RetryRound,AttemptNumber=lease.Job.AttemptCount,CallNumber=++number,Model=frozen.Model,InputHash=Content.Hash(BuilderSemanticProtocol.UserFor(input)),ModelConfigHash=Content.Hash(Json.Write(frozen)),ModelConfigPayload=Json.Write(frozen),InputPayload=BuilderSemanticProtocol.UserFor(input)};
        await using(var db=Open())await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            await db.Lock(call.FamilyId,ct);var current=await BuilderSemanticJobs.ValidateFrozen(db,lease,ct);
            if(current.Preparation.Id!=preparation.Id || current.Frozen.ModelInputPayload!=call.InputPayload || Json.Write(BuilderSemanticJobs.ResolveLocal(current.Frozen))!=call.ModelConfigPayload)throw new ApiError(422,"JOB_INPUT_SNAPSHOT_CHANGED","实际开始调用前原语义输入已变化。");
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
            if(response.Usage is {} usage && (usage.ChargedCost!=0 || usage.Currency!=null || usage.BillingStatus is not ("LocalMeasured" or "LocalNoCharge")))throw new ApiError(422,"BUILDER_USAGE_INVALID","本机语义不得记录未经验证的外部计费值。");
            await Finish(call.Id,"Returned",response,response.OutputComplete?null:"LOCAL_PROVIDER_OUTPUT_INCOMPLETE",watch.ElapsedMilliseconds);return response;
        }
        catch(Exception ex){await Finish(call.Id,ex is OperationCanceledException?"Cancelled":"Failed",null,ex is ApiError api?api.Code:ex is OperationCanceledException?"CALL_CANCELLED":"PROVIDER_CALL_FAILED",watch.ElapsedMilliseconds);throw;}
    }
    async Task Finish(Guid id,string status,BuilderProviderResponse? response,string? error,long elapsed)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync(timeout.Token);await BuilderBudget.Lock(db,preparation.FamilyId,timeout.Token);
        var call=await db.Set<BuilderSemanticCall>().SingleOrDefaultAsync(c=>c.Id==id && c.FamilyId==preparation.FamilyId,timeout.Token);if(call==null || call.Status!="Started")return;
        call.Status=status;call.FinishedAt=DateTimeOffset.UtcNow;call.ElapsedMilliseconds=elapsed;call.ErrorCode=error;call.BudgetState="Unresolved";
        if(response!=null){call.OutputHash=Content.Hash(response.Output);call.InputTokens=response.Usage?.InputTokens;call.OutputTokens=response.Usage?.OutputTokens;call.ChargedCost=response.Usage?.ChargedCost;call.Currency=response.Usage?.Currency;call.BillingStatus=response.Usage?.BillingStatus??"Unknown";if(call.BillingStatus is "LocalMeasured" or "LocalNoCharge")call.BudgetState="Settled";}
        await db.SaveChangesAsync(timeout.Token);await tx.CommitAsync(timeout.Token);
    }
    public static async Task<BuilderSemanticReconciliation> Reconcile(Database db,Actor actor,Guid id,BuilderSemanticReconciliationInput input,CancellationToken ct=default)
    {
        await BuilderBudget.Owner(db,actor);await BuilderBudget.Lock(db,actor.FamilyId,ct);var call=await db.Set<BuilderSemanticCall>().SingleOrDefaultAsync(c=>c.Id==id && c.FamilyId==actor.FamilyId,ct)??throw new ApiError(404,"NOT_FOUND","找不到本家庭语义调用。");
        if(call.Status=="Denied" || call.BudgetState=="Settled" || await db.Set<BuilderSemanticReconciliation>().AnyAsync(r=>r.CallId==id,ct))throw new ApiError(409,"CALL_ALREADY_RESOLVED","调用已确认或无需核对，原记录保留。");
        if(!input.ProviderFinished || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>4000 || string.IsNullOrWhiteSpace(input.ReceiptReference) || input.ReceiptReference.Length>500 || input.InputTokens<0 || input.OutputTokens<0)throw new ApiError(422,"RECONCILIATION_REQUIRED","请实际确认本机调用已结束，填写原因和进程依据；未知Token可留空。");
        if(call.InputTokens!=null && input.InputTokens!=call.InputTokens || call.OutputTokens!=null && input.OutputTokens!=call.OutputTokens)throw new ApiError(422,"RECONCILIATION_CONFLICT","实际返回的Token不能改写。");
        var decision=new BuilderSemanticReconciliation{FamilyId=actor.FamilyId,CallId=id,ActorId=actor.Id,InputTokens=input.InputTokens,OutputTokens=input.OutputTokens,Reason=input.Reason.Trim(),ReceiptReference=input.ReceiptReference.Trim()};db.Add(decision);return decision;
    }
}
