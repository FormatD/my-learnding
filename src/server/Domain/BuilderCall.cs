namespace Learning;
public class BuilderCall:Row
{
    public DateOnly? BudgetDay {get;set;}
    public decimal? ReservedCost {get;set;}
    public long? ReservedTokens {get;set;}
    public string? QuotePayload {get;set;}
    public string? BudgetSnapshot {get;set;}
    public string? BudgetState {get;set;}
    public Guid RunId {get;set;}
    public Guid ExecutionId {get;set;}
    public int RetryRound {get;set;}
    public int AttemptNumber {get;set;}
    public int CallNumber {get;set;}
    public bool Repair {get;set;}
    public string Provider {get;set;}="";
    public string Model {get;set;}="";
    public string InputHash {get;set;}="";
    public string? ModelConfigHash {get;set;}
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
}
public record BuilderUsage(long? InputTokens,long? OutputTokens,decimal? ChargedCost,string? Currency,string BillingStatus="Unknown");
public record BuilderProviderResponse(string Output,BuilderUsage? Usage=null,bool OutputComplete=true);
