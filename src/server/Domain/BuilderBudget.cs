using Microsoft.EntityFrameworkCore;
namespace Learning;
public class BuilderBudgetPolicy:Row
{
    public decimal DailyCostLimit {get;set;}
    public long DailyTokenLimit {get;set;}
    public decimal PerCallCostLimit {get;set;}
    public long PerCallTokenLimit {get;set;}
    public int MaxConcurrentCalls {get;set;}=1;
    public int Revision {get;set;}=1;
    public string Currency {get;set;}="USD";
}
public class BuilderBudgetReconciliation:Row
{
    public Guid CallId {get;set;}
    public Guid ActorId {get;set;}
    public decimal ChargedCost {get;set;}
    public string? Currency {get;set;}
    public long? InputTokens {get;set;}
    public long? OutputTokens {get;set;}
    public string Reason {get;set;}="";
    public string ReceiptReference {get;set;}="";
    public string Source {get;set;}="ParentConfirmed";
}
public record BuilderQuote(decimal MaxCost,long MaxTokens,string? Currency,bool LocalNoCharge=false)
{
    public static readonly BuilderQuote Local=new(0,0,null,true);
    public void Validate(){if(MaxCost<0 || MaxCost>9_999_999_999.999999m || decimal.Round(MaxCost,6)!=MaxCost || MaxTokens<0 || MaxTokens>1_000_000_000_000 || LocalNoCharge && (MaxCost!=0 || MaxTokens!=0 || Currency!=null) || !LocalNoCharge && Currency!="USD")throw new ApiError(422,"BUILDER_QUOTE_INVALID","无法验证调用最大费用或Token额度，未开始调用。");}
}
public record BuilderBudgetInput(decimal DailyCostLimit,long DailyTokenLimit,decimal PerCallCostLimit,long PerCallTokenLimit,int MaxConcurrentCalls,string Reason);
public record BuilderReconciliationInput(bool ProviderFinished,decimal ChargedCost,string? Currency,long? InputTokens,long? OutputTokens,string Reason,string ReceiptReference);
public record BuilderBudgetState(decimal CostCommitted,decimal TokensCommitted,int ActiveCalls,int UnboundedUnknownCalls,int UnresolvedCalls,int OverrunCalls);
public static class BuilderBudget
{
    public static Task Lock(Database db,Guid family,CancellationToken ct=default)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"builder-budget:"+family.ToString()},0))",ct);
    public static void Validate(BuilderBudgetPolicy p){if(p.Currency!="USD" || p.DailyCostLimit<0 || p.DailyCostLimit>9_999_999_999.999999m || p.PerCallCostLimit<0 || p.PerCallCostLimit>p.DailyCostLimit || decimal.Round(p.DailyCostLimit,6)!=p.DailyCostLimit || decimal.Round(p.PerCallCostLimit,6)!=p.PerCallCostLimit || p.DailyTokenLimit<0 || p.DailyTokenLimit>1_000_000_000_000 || p.PerCallTokenLimit<0 || p.PerCallTokenLimit>p.DailyTokenLimit || p.MaxConcurrentCalls<1 || p.MaxConcurrentCalls>10)throw new ApiError(422,"BUILDER_BUDGET_INVALID","预算须为非负有效值，单次不超过每日额度，并发1～10。");}
    public static BuilderBudgetState Calculate(BuilderCall[] calls,BuilderBudgetReconciliation[] reconciliations,DateOnly day)
    {
        var decisions=reconciliations.ToDictionary(r=>r.CallId);decimal cost=0,tokens=0;int active=0,unknown=0,unresolved=0,overruns=0;
        foreach(var c in calls.Where(c=>c.Status!="Denied"))
        {
            decisions.TryGetValue(c.Id,out var decision);if(c.BudgetState=="Overrun" && decision==null)overruns++;var closed=decision!=null || c.Status=="Returned";if(!closed)active++;
            var free=decision!=null?decision.Currency==null && decision.ChargedCost==0:c.BillingStatus=="LocalNoCharge";
            var knownCost=free || decision!=null || c.BillingStatus=="Confirmed" && c.ChargedCost!=null && c.Currency=="USD";
            var knownTokens=free || decision!=null && decision.InputTokens!=null && decision.OutputTokens!=null || decision==null && c.InputTokens!=null && c.OutputTokens!=null;
            var today=(c.BudgetDay??DateOnly.FromDateTime(c.StartedAt.UtcDateTime))==day;
            if(knownCost){if(today)cost+=decision?.ChargedCost??c.ChargedCost??0;}else if(c.ReservedCost!=null)cost+=c.ReservedCost.Value;
            if(knownTokens){if(today && !free)tokens+=(decimal)(decision?.InputTokens??c.InputTokens??0)+(decision?.OutputTokens??c.OutputTokens??0);}else if(c.ReservedTokens!=null)tokens+=c.ReservedTokens.Value;
            if(!knownCost || !knownTokens){unresolved++;if(c.ReservedCost==null || c.ReservedTokens==null)unknown++;}
        }
        return new(cost,tokens,active,unknown,unresolved,overruns);
    }
    public static async Task<BuilderBudgetState> State(Database db,Guid family,DateOnly day,CancellationToken ct=default)=>Calculate(await db.Set<BuilderCall>().AsNoTracking().Where(c=>c.FamilyId==family).ToArrayAsync(ct),await db.Set<BuilderBudgetReconciliation>().AsNoTracking().Where(r=>r.FamilyId==family).ToArrayAsync(ct),day);
    public static async Task<string?> Reserve(Database db,BuilderCall call,BuilderQuote quote,CancellationToken ct)
    {
        quote.Validate();await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,call.FamilyId,ct);
        var policy=await db.Set<BuilderBudgetPolicy>().SingleOrDefaultAsync(p=>p.FamilyId==call.FamilyId,ct)??new BuilderBudgetPolicy{FamilyId=call.FamilyId};if(db.Entry(policy).State==EntityState.Detached)db.Add(policy);Validate(policy);
        var day=DateOnly.FromDateTime(DateTime.UtcNow);var state=await State(db,call.FamilyId,day,ct);string? denied=null;
        if(state.OverrunCalls>0)denied="BUILDER_USAGE_RECONCILIATION_REQUIRED";
        else if(state.ActiveCalls>=policy.MaxConcurrentCalls)denied="BUILDER_CONCURRENCY_LIMIT";
        else if(!quote.LocalNoCharge && state.UnboundedUnknownCalls>0)denied="BUILDER_USAGE_RECONCILIATION_REQUIRED";
        else if(quote.MaxCost>policy.PerCallCostLimit || quote.MaxTokens>policy.PerCallTokenLimit)denied="BUILDER_CALL_BUDGET_LIMIT";
        else if(state.CostCommitted+quote.MaxCost>policy.DailyCostLimit || state.TokensCommitted+quote.MaxTokens>policy.DailyTokenLimit)denied="BUILDER_DAILY_BUDGET_LIMIT";
        call.BudgetDay=day;call.BudgetSnapshot=Json.Write(policy);call.QuotePayload=Json.Write(quote);call.BudgetState=denied==null?"Reserved":"Denied";
        if(denied==null){call.ReservedCost=quote.MaxCost;call.ReservedTokens=quote.MaxTokens;}else{call.Status="Denied";call.ErrorCode=denied;call.FinishedAt=DateTimeOffset.UtcNow;}
        db.Add(call);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return denied;
    }
    static async Task Owner(Database db,Actor actor){actor.Require("ContentEditor");if((await db.Families.SingleAsync(f=>f.Id==actor.FamilyId)).OwnerAccountId!=actor.AccountId)throw new ApiError(403,"OWNER_REQUIRED","只有家庭负责人可以调整或核对调用预算。");}
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/builder/budget",async(Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("ContentEditor");await using var tx=await db.Database.BeginTransactionAsync();await Lock(db,a.FamilyId);var policy=await db.Set<BuilderBudgetPolicy>().SingleOrDefaultAsync(p=>p.FamilyId==a.FamilyId)??new BuilderBudgetPolicy{FamilyId=a.FamilyId};var day=DateOnly.FromDateTime(DateTime.UtcNow);var state=await State(db,a.FamilyId,day);await tx.CommitAsync();return new{policy,day,window="UTC",state};});
        api.MapPut("/builder/budget",async(BuilderBudgetInput input,Database db,HttpContext ctx)=>{var a=ctx.Actor();await Owner(db,a);if(string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>4000)throw new ApiError(422,"REASON_REQUIRED","请填写调整依据，最多4000字。");await Lock(db,a.FamilyId);var p=await db.Set<BuilderBudgetPolicy>().SingleOrDefaultAsync(p=>p.FamilyId==a.FamilyId)??new BuilderBudgetPolicy{FamilyId=a.FamilyId};var before=Json.Write(p);p.DailyCostLimit=input.DailyCostLimit;p.DailyTokenLimit=input.DailyTokenLimit;p.PerCallCostLimit=input.PerCallCostLimit;p.PerCallTokenLimit=input.PerCallTokenLimit;p.MaxConcurrentCalls=input.MaxConcurrentCalls;p.Revision++;Validate(p);if(db.Entry(p).State==EntityState.Detached)db.Add(p);db.Audits.Add(new(){FamilyId=a.FamilyId,ActorId=a.Id,Action="BuilderBudgetChanged",Details=Json.Write(new{before,after=p,reason=input.Reason.Trim()})});return p;});
        api.MapPost("/builder/calls/{id:guid}:reconcile",async(Guid id,BuilderReconciliationInput input,Database db,HttpContext ctx)=>{var a=ctx.Actor();await Owner(db,a);await Lock(db,a.FamilyId);var call=await db.Set<BuilderCall>().SingleOrDefaultAsync(c=>c.FamilyId==a.FamilyId && c.Id==id)??throw new ApiError(404,"NOT_FOUND","找不到本家庭调用。");if(call.Status=="Denied" || call.BudgetState=="Settled" || await db.Set<BuilderBudgetReconciliation>().AnyAsync(r=>r.CallId==id))throw new ApiError(409,"CALL_ALREADY_RESOLVED","此调用无需或已完成核对，原记录继续保留。");if(!input.ProviderFinished || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>4000 || string.IsNullOrWhiteSpace(input.ReceiptReference) || input.ReceiptReference.Length>500)throw new ApiError(422,"RECONCILIATION_REQUIRED","请确认提供者已结束，并填写核对原因及账单或本地进程依据。");var quote=call.QuotePayload==null?null:Json.Read<BuilderQuote>(call.QuotePayload);var free=quote?.LocalNoCharge==true && call.BillingStatus!="Confirmed";if(call.BillingStatus=="Confirmed" && call.Currency=="USD" && (input.ChargedCost!=call.ChargedCost || input.Currency!=call.Currency) || call.InputTokens!=null && input.InputTokens!=call.InputTokens || call.OutputTokens!=null && input.OutputTokens!=call.OutputTokens)throw new ApiError(422,"RECONCILIATION_CONFLICT","已确认的费用或Token不能改写，核对只补充未知信息。 ");BuilderCallTracking.ValidateUsage(new(input.InputTokens,input.OutputTokens,input.ChargedCost,input.Currency,free?"LocalNoCharge":"Confirmed"));if(!free && (input.Currency!="USD" || input.InputTokens==null || input.OutputTokens==null))throw new ApiError(422,"RECONCILIATION_REQUIRED","请填写USD实际费用及完整Token用量，不用估计值补齐未知。");var decision=new BuilderBudgetReconciliation{FamilyId=a.FamilyId,CallId=id,ActorId=a.AccountId!.Value,ChargedCost=input.ChargedCost,Currency=input.Currency,InputTokens=input.InputTokens,OutputTokens=input.OutputTokens,Reason=input.Reason.Trim(),ReceiptReference=input.ReceiptReference.Trim()};db.Add(decision);return TypedResults.Created($"/api/v1/builder/calls/{id}",decision);});
    }
}
