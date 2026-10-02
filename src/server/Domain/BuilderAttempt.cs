namespace Learning;
public class BuilderAttempt:Row
{
    public Guid RunId {get;set;}
    public int RetryRound {get;set;}
    public int Number {get;set;}
    public DateTimeOffset StartedAt {get;set;}
    public DateTimeOffset FinishedAt {get;set;}
    public string Status {get;set;}="Completed";
    public string? ErrorCode {get;set;}
    public DateTimeOffset? NextAttemptAt {get;set;}
    public string? ProtocolResult {get;set;}
    public string InputSnapshot {get;set;}="";
}
