using Microsoft.EntityFrameworkCore;
namespace Learning;
public class ReviewTargetConfirmation:Row
{
    public long Sequence {get;set;}
    public Guid StudentId {get;set;}
    public Guid AttemptId {get;set;}
    public Guid TaskId {get;set;}
    public Guid OriginalTargetId {get;set;}
    public Guid ConfirmedTargetId {get;set;}
    public Guid MappingReleaseId {get;set;}
    public Guid QuestionRevisionId {get;set;}
    public Guid? MappingSetRevisionId {get;set;}
    public Guid ConfirmedBy {get;set;}
    public string Action {get;set;}="";
    public string Reason {get;set;}="";
    public string MeasurementSnapshot {get;set;}="";
    public string MeasurementHash {get;set;}="";
    public string PreviewHash {get;set;}="";
}
public record ReviewTargetDecisionInput(string Action,Guid? TargetId,string Reason,string? PreviewHash=null);
public record ReviewMeasurement(string Version,Guid FamilyId,Guid StudentId,Guid AttemptId,Guid TaskId,Guid OriginalTargetId,Guid MappingReleaseId,Guid? MappingSetRevisionId,Guid? MappingCorrectionBatchId,Question Question);
public record ReviewTargetChoice(Guid Id,string Name);
public record ReviewTargetChange(Guid AttemptId,Guid TaskId,Guid OriginalTargetId,string OriginalTargetName,string QuestionStem,ReviewTargetChoice[] MeasuredTargets,ReviewTargetConfirmation? Confirmation,string MeasurementHash);
public record ReviewTargetDecisionPreview(string PreviewHash,ReviewTargetChange Change,string Action,Guid TargetId,string Reason,string Notice);
public static class ReviewTargetConfirmations
{
    public static ReviewMeasurement Measurement(AssessmentInput input)=>new("review-measurement/1",input.Attempt.FamilyId,input.Attempt.StudentId,input.Attempt.Id,input.Task.Id,input.Task.ReviewTargetId!.Value,input.MappingReleaseId??input.Session.ReleaseId,input.MappingSetRevisionId,input.MappingCorrectionBatchId,input.Question);
    public static string MeasurementHash(AssessmentInput input)=>Content.Hash(Json.Write(Measurement(input)));
    public static bool Applies(AssessmentInput input,ReviewTargetConfirmation? decision)=>decision!=null && input.Task.Type=="Review" && input.Task.ReviewTargetId!=null && decision.FamilyId==input.Attempt.FamilyId && decision.StudentId==input.Attempt.StudentId && decision.AttemptId==input.Attempt.Id && decision.TaskId==input.Task.Id && decision.OriginalTargetId==input.Task.ReviewTargetId && decision.MeasurementHash==MeasurementHash(input) && Content.Hash(decision.MeasurementSnapshot)==decision.MeasurementHash && (decision.Action=="KeepOriginal" && decision.ConfirmedTargetId==decision.OriginalTargetId || decision.Action=="AdoptMeasuredTarget" && ReviewTargets.Measures(input.Question,decision.ConfirmedTargetId));
    public static bool Changed(AssessmentInput input)=>input.Attempt.Number==1 && input.Task.Type=="Review" && input.Task.ReviewTargetId is Guid target && target!=input.Question.Id && !ReviewTargets.Measures(input.Question,target);
    static ReviewTargetChange Describe(AssessmentInput input)=>new(input.Attempt.Id,input.Task.Id,input.Task.ReviewTargetId!.Value,input.Kcs.FirstOrDefault(k=>k.Id==input.Task.ReviewTargetId)?.Name??"原知识点",input.Question.Stem,input.Kcs.Where(k=>ReviewTargets.Measures(input.Question,k.Id)).Select(k=>new ReviewTargetChoice(k.Id,k.Name)).ToArray(),input.ReviewConfirmation,MeasurementHash(input));
    public static async Task<ReviewTargetChange[]> Read(Database db,Actor actor,Guid studentId,CancellationToken ct=default)
    {
        actor.Require("Parent");var student=await actor.Student(db,studentId);var (inputs,_)=await Assessment.LoadInputs(db,student,ct);return inputs.Where(Changed).OrderByDescending(i=>i.Attempt.Sequence).Select(Describe).ToArray();
    }
    public static async Task<ReviewTargetDecisionPreview> Preview(Database db,Actor actor,Guid studentId,Guid attemptId,ReviewTargetDecisionInput input,CancellationToken ct=default)
    {
        actor.Require("Parent");var student=await actor.Student(db,studentId);
        if(input.Action is not ("KeepOriginal" or "AdoptMeasuredTarget") || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>1000)throw new ApiError(422,"REVIEW_TARGET_DECISION_INVALID","选择保留原目标或确认实际测量目标，并填写1～1000字依据。");
        var (inputs,_)=await Assessment.LoadInputs(db,student,ct);var row=inputs.SingleOrDefault(i=>i.Attempt.Id==attemptId)??throw new ApiError(404,"NOT_FOUND","找不到该学生的作答。");
        if(!Changed(row))throw new ApiError(409,"REVIEW_TARGET_UNCHANGED","该首次复习答案的测量目标未发生变化，请刷新。");
        var target=input.Action=="KeepOriginal"?row.Task.ReviewTargetId!.Value:input.TargetId??Guid.Empty;
        if(input.Action=="KeepOriginal" && input.TargetId!=null && input.TargetId!=target || input.Action=="AdoptMeasuredTarget" && (target==row.Task.ReviewTargetId || !ReviewTargets.Measures(row.Question,target)))throw new ApiError(422,"REVIEW_TARGET_NOT_MEASURED","确认的新目标必须由当前有效题目实际测量，不能转给未测量的知识点。");
        var hash=Content.Hash(Json.Write(new{version="review-target-decision/1",measurement=MeasurementHash(row),student.ActiveGenerationId,cursor=inputs.LastOrDefault()?.Attempt.Sequence??0,grade=row.Grade,previous=row.ReviewConfirmation?.Id,input.Action,target,input.Reason}));
        return new(hash,Describe(row),input.Action,target,input.Reason,input.Action=="KeepOriginal"?"原目标继续保持待复习；本次不能替代原目标测量。确认后请重新生成计划，核对当前发布题目再安排原目标。":"本次仅按实际测量的新目标核对复习日程，原目标仍保留待复习；未到期、未知步骤、提示或重试不能因此晋级，不转移掌握度。后台重算后再重新生成计划。");
    }
    public static async Task<ReviewTargetConfirmation> Confirm(Database db,Actor actor,Guid studentId,Guid attemptId,ReviewTargetDecisionInput input,CancellationToken ct=default)
    {
        var preview=await Preview(db,actor,studentId,attemptId,input,ct);
        if(preview.PreviewHash!=input.PreviewHash)throw new ApiError(412,"REVIEW_TARGET_PREVIEW_CHANGED","判分、映射、评估或确认依据已变化，请重新预览。");
        var student=await actor.Student(db,studentId);var (inputs,_)=await Assessment.LoadInputs(db,student,ct);var source=inputs.Single(i=>i.Attempt.Id==attemptId);var measurement=Measurement(source);
        var row=new ReviewTargetConfirmation{FamilyId=actor.FamilyId,StudentId=studentId,AttemptId=attemptId,TaskId=source.Task.Id,OriginalTargetId=measurement.OriginalTargetId,ConfirmedTargetId=preview.TargetId,MappingReleaseId=measurement.MappingReleaseId,QuestionRevisionId=source.Question.RevisionId,MappingSetRevisionId=measurement.MappingSetRevisionId,ConfirmedBy=actor.Id,Action=input.Action,Reason=input.Reason,MeasurementSnapshot=Json.Write(measurement),MeasurementHash=preview.Change.MeasurementHash,PreviewHash=preview.PreviewHash};db.Add(row);
        db.Audits.Add(new Audit{FamilyId=actor.FamilyId,StudentId=studentId,ActorId=actor.Id,Action="ReviewTargetConfirmed",Details=Json.Write(new{confirmationId=row.Id,row.AttemptId,row.OriginalTargetId,row.ConfirmedTargetId,row.Action,row.Reason,row.MeasurementHash})});
        await DomainEvents.Append(db,actor.FamilyId,studentId,attemptId,"Attempt","CorrectionConfirmed",new{attemptId,reviewTargetConfirmationId=row.Id,row.OriginalTargetId,row.ConfirmedTargetId,row.Action,row.MeasurementHash},attemptId,ct);
        await db.SaveChangesAsync(ct);
        await db.Entry(row).ReloadAsync(ct);
        return row;
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/students/{id:guid}/review-target-changes",async(Guid id,Database db,HttpContext ctx,CancellationToken ct)=>TypedResults.Ok(await Read(db,ctx.Actor(),id,ct)));
        api.MapPost("/students/{id:guid}/attempts/{attemptId:guid}/review-target:preview",async(Guid id,Guid attemptId,ReviewTargetDecisionInput input,Database db,HttpContext ctx,CancellationToken ct)=>TypedResults.Ok(await Preview(db,ctx.Actor(),id,attemptId,input,ct)));
        api.MapPost("/students/{id:guid}/attempts/{attemptId:guid}/review-target:confirm",async(Guid id,Guid attemptId,ReviewTargetDecisionInput input,Database db,HttpContext ctx,CancellationToken ct)=>TypedResults.Accepted("/api/v1/students/"+id+"/review-target-changes",await Confirm(db,ctx.Actor(),id,attemptId,input,ct)));
    }
}
