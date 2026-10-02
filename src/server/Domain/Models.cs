using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Learning;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
}
public abstract class Row
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class Family
{
    public Guid? OwnerAccountId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "我的家庭";
    public long Version { get; set; } = 1;
}
public class Account : Row
{
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Roles { get; set; } = "Parent,ContentEditor,Publisher";
}
public class FamilyMembership : Row
{
    public Guid AccountId { get; set; }
    public string Roles { get; set; } = "Parent";
}
public class AuthSession : Row
{
    public Guid? AccountId { get; set; }
    public Guid? StudentId { get; set; }
    public string Role { get; set; } = "Parent";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Revoked { get; set; }
}
public class Student : Row
{
    public string Name { get; set; } = "";
    public int Grade { get; set; } = 3;
    public string TimeZone { get; set; } = "Asia/Shanghai";
    public int DailyMinutes { get; set; } = 30;
    public Guid? ActiveReleaseId { get; set; }
    public Guid? ActiveGenerationId { get; set; }
}
public class Availability : Row
{
    public Guid StudentId { get; set; }
    public DateOnly Date { get; set; }
    public int Minutes { get; set; }
    public int Reserved { get; set; }
}
public class Progress : Row
{
    public string Status { get; set; } = "Confirmed";
    public Guid? ReleaseId { get; set; }
    public Guid StudentId { get; set; }
    public Guid LessonId { get; set; }
    public DateOnly Date { get; set; }
    public string Source { get; set; } = "ParentConfirmed";
}
public class Goal : Row
{
    public Guid? CourseId { get; set; }
    public Guid? UnitId { get; set; }
    public string Subject { get; set; } = "Unspecified";
    public string GoalType { get; set; } = "Activity";
    public Guid? KCId { get; set; }
    public string Period { get; set; } = "Daily";
    public int TargetValue { get; set; } = 1;
    public string ScheduleRule { get; set; } = "[1,2,3,4,5,6,0]";
    public int Priority { get; set; } = 3;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public long Version { get; set; } = 1;
    public Guid StudentId { get; set; }
    public string Title { get; set; } = "每天阅读";
    public int Minutes { get; set; } = 10;
    public string PaperReference { get; set; } = "自选图书";
    public bool Active { get; set; } = true;
}
public class ContentDraft : Row
{
    public string Title { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public long Version { get; set; } = 1;
    public Guid? ReviewedBy { get; set; }
}
public class Release : Row
{
    public int Number { get; set; }
    public string Payload { get; set; } = "";
    public string Hash { get; set; } = "";
    public Guid PublishedBy { get; set; }
    public bool Withdrawn { get; set; }
}
public record KC(Guid Id, Guid RevisionId, string Code, string Name, string Behavior, string Boundary, string Type = "Procedure", string[]? RequiredCoverage = null);
public record Mapping(Guid KCId, string Role = "Primary", decimal Share = 1, string Mode = "WholeItem", string? Step = null);
public record Question(Guid Id, Guid RevisionId, string Stem, string Answer, string Explanation, string Type, string Difficulty, string Policy, Mapping[] Mappings, Guid? VariantGroupId = null, string Coverage = "Basic", string? Hint = null);
public record Resource(Guid Id, string Title, string PaperReference, int Minutes, Guid[] KCIds, string? Url = null,Guid? RevisionId=null);
public record Textbook(Guid Id,Guid RevisionId,string Publisher,string Edition,string Subject,int Grade,string Semester,Guid? SourceId=null);
public record TextbookUnit(Guid Id,Guid RevisionId,Guid TextbookId,string Title,int Sequence);
public record Course(Guid Id,Guid RevisionId,string Provider,string Subject,string Title,Guid[]? SourceRefs=null);
public record Lesson(Guid Id, string Title, int Sequence, Guid[] KCIds,Guid? UnitId=null,Guid? CourseId=null,Guid? RevisionId=null,int EstimatedMinutes=5,Guid[]? SourceRefs=null);
public record Relation(Guid From, Guid To, string Type = "Prerequisite");
public record Catalog(KC[] Kcs, Question[] Questions, Resource[] Resources, Lesson[] Lessons, Relation[] Relations,Textbook[]? Textbooks=null,TextbookUnit[]? Units=null,Course[]? Courses=null,MappingCoverage[]? MappingCoverage=null);
public class Plan : Row
{
    public Guid StudentId { get; set; }
    public DateOnly Date { get; set; }
    public Guid? ActiveRevisionId { get; set; }
    public string Status { get; set; } = "Draft";
}
public class PlanRevision : Row
{
    public string RuleVersion { get; set; } = "legacy/unknown";
    public Guid PlanId { get; set; }
    public Guid ReleaseId { get; set; }
    public int Number { get; set; }
    public int Budget { get; set; }
    public int Reserved { get; set; }
    public int Overflow { get; set; }
    public string Status { get; set; } = "Draft";
    public string InputHash { get; set; } = "";
    public string Candidates { get; set; } = "[]";
    public string Warnings { get; set; } = "[]";
}
public class StudyTask : Row
{
    public string GoalSnapshots { get; set; } = "[]";
    public int TrackedSeconds { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid StudentId { get; set; }
    public Guid ReleaseId { get; set; }
    public string Type { get; set; } = "Practice";
    public string Title { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid? KCId { get; set; }
    public Guid? QuestionId { get; set; }
    public Guid? ReviewTargetId { get; set; }
    public string ResourceRef { get; set; } = "";
    public string? ResourceUrl { get; set; }
    public int Minutes { get; set; } = 5;
    public int? ActualMinutes { get; set; }
    public bool Locked { get; set; }
    public bool Mandatory { get; set; }
    public string Status { get; set; } = "Planned";
}
public class Placement : Row
{
    public Guid RevisionId { get; set; }
    public Guid TaskId { get; set; }
    public int Sequence { get; set; }
}
public class LearningSession : Row
{
    public Guid? QuestionRevisionId { get; set; }
    public Guid? MappingSetRevisionId { get; set; }
    public Guid StudentId { get; set; }
    public Guid TaskId { get; set; }
    public Guid ReleaseId { get; set; }
    public Guid QuestionId { get; set; }
    public int HintLevel { get; set; }
    public bool AnswerShown { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class Attempt : Row
{
    public Guid? QuestionRevisionId { get; set; }
    public Guid? MappingSetRevisionId { get; set; }
    public Guid StudentId { get; set; }
    public Guid SessionId { get; set; }
    public Guid ClientSubmissionId { get; set; }
    public int Number { get; set; }
    public long Sequence { get; set; }
    public string Answer { get; set; } = "";
    public string AnswerSource { get; set; } = "Child";
    public int HintLevel { get; set; }
    public bool AnswerShown { get; set; }
}
public class Grading : Row
{
    public Guid? CorrectionBatchId { get; set; }
    public Guid AttemptId { get; set; }
    public int Number { get; set; }
    public string Result { get; set; } = "Pending";
    public string Method { get; set; } = "Rule";
    public string Reason { get; set; } = "";
    public Guid? GradedBy { get; set; }
    public string Steps { get; set; } = "[]";
}
public record ObservedStep(string Step, string Result, int HintLevel = 0);
public class Outbox : Row
{
    public Guid StudentId { get; set; }
    public Guid AttemptId { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Retries { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}
public class Generation : Row
{
    public string ModelVersion { get; set; } = "mastery/1";
    public Guid StudentId { get; set; }
    public string Status { get; set; } = "Shadow";
    public string InputHash { get; set; } = "";
    public string RuleVersion { get; set; } = "evidence/1";
    public long Cursor { get; set; }
}
public class AssessmentContext : Row
{
    public Guid? GradingCorrectionBatchId { get; set; }
    public Guid? MappingCorrectionBatchId { get; set; }
    public Guid StudentId { get; set; }
    public Guid GenerationId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid GradingRevisionId { get; set; }
    public Guid? MappingSetRevisionId { get; set; }
    public Guid QuestionRevisionId { get; set; }
    public Guid ContentReleaseId { get; set; }
    public Guid MappingReleaseId { get; set; }
    public Guid? CorrectionBatchId { get; set; }
    public string EvidenceRuleVersion { get; set; } = "";
    public string ActivationStatus { get; set; } = "Shadow";
    public string MappingSource { get; set; } = "";
    public string EvidencePolicy { get; set; } = "";
    public string AdmissionStatus { get; set; } = "";
}
public class Evidence : Row
{
    public Guid? ContextId { get; set; }
    public Guid? MappingSetRevisionId { get; set; }
    public Guid? MappingReleaseId { get; set; }
    public Guid? CorrectionBatchId { get; set; }
    public Guid GenerationId { get; set; }
    public Guid StudentId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid GradingId { get; set; }
    public Guid ReleaseId { get; set; }
    public Guid KCId { get; set; }
    public Guid KCRevisionId { get; set; }
    public string Part { get; set; } = "WholeItem";
    public bool Positive { get; set; }
    public decimal RawWeight { get; set; }
    public decimal Weight { get; set; }
    public string Factors { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
public class Mastery : Row
{
    public Guid GenerationId { get; set; }
    public Guid StudentId { get; set; }
    public Guid KCId { get; set; }
    public decimal Alpha { get; set; } = 2;
    public decimal Beta { get; set; } = 2;
    public decimal Probability { get; set; } = .5m;
    public decimal EffectiveEvidence { get; set; }
    public string Confidence { get; set; } = "Low";
    public string Status { get; set; } = "Unknown";
    public bool NeedsRecheck { get; set; }
    public int DistinctQuestions { get; set; }
    public string Gaps { get; set; } = "[]";
    public string Reason { get; set; } = "NO_EVIDENCE";
}
public class Review : Row
{
    public Guid GenerationId { get; set; }
    public Guid StudentId { get; set; }
    public Guid TargetId { get; set; }
    public Guid? KCId { get; set; }
    public string TargetType { get; set; } = "WrongQuestion";
    public string Stage { get; set; } = "R1";
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = "Pending";
    public int WrongCount { get; set; }
    public string ErrorType { get; set; } = "Unknown";
}
public class Source : Row
{
    public Guid? FileId { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Hash { get; set; } = "";
    public string UsageScope { get; set; } = "FamilyOnly";
    public bool AllowExternalAI { get; set; }
}
public class Chunk : Row
{
    public Guid SourceId { get; set; }
    public string Locator { get; set; } = "";
    public string Text { get; set; } = "";
}
public class BuilderRun : Row
{
    public Guid? LibraryReleaseId { get; set; }
    public string InputVersion { get; set; } = "builder-input/2";
    public string Type { get; set; } = "Candidates";
    public Guid SourceId { get; set; }
    public string Status { get; set; } = "Queued";
    public string Provider { get; set; } = "Mock";
    public string Model { get; set; } = "fixture/1";
    public string PromptVersion { get; set; } = "kc-candidate/1";
    public string InputHash { get; set; } = "";
    public string? Error { get; set; }
    public int Retries { get; set; }
    public int RetryRound { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
public class Candidate : Row
{
    public string? ProtocolPayload { get; set; }
    public Guid? CreatedDraftId { get; set; }
    public Guid? CreatedKCId { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string ReviewReason { get; set; } = "";
    public string Decision { get; set; } = "";
    public Guid RunId { get; set; }
    public Guid ChunkId { get; set; }
    public string Name { get; set; } = "";
    public string Behavior { get; set; } = "";
    public string Boundary { get; set; } = "";
    public string Quote { get; set; } = "";
    public string Type { get; set; } = "Procedure";
    public string Status { get; set; } = "Pending";
    public string SuggestedAction { get; set; } = "NeedsReview";
    public string Matches { get; set; } = "[]";
    public Guid? ExistingKCId { get; set; }
}
public class Audit : Row
{
    public Guid? StudentId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}
public class CommandRecord : Row
{
    public string? CookieCipher { get; set; }
    public Guid ActorId { get; set; }
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Response { get; set; } = "";
    public int StatusCode { get; set; }
}
public class ApiError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
