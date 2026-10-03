using Microsoft.EntityFrameworkCore;

namespace Learning;
public class CorrectionBatch : Row
{
    public string? Cause { get; set; }
    public string? AffectedAttemptIds { get; set; }
    public Guid? SourceGradingRevisionId { get; set; }
    public Guid StudentId { get; set; }
    public Guid ReleaseId { get; set; }
    public string PreviewHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid ConfirmedBy { get; set; }
    public string Status { get; set; } = "Confirmed";
}
public class CorrectionItem : Row
{
    public Guid? QuestionRevisionId { get; set; }
    public Guid? MappingSetRevisionId { get; set; }
    public long Sequence { get; set; }
    public Guid BatchId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid MappingReleaseId { get; set; }
}
public record MappingCorrectionInput(Guid ReleaseId,Guid[] AttemptIds,string Reason,string? PreviewHash=null);
public static class Corrections
{
    static async Task<(Student Student,Release Release,string Hash,object[] Changes)> Preview(Database db,Actor actor,Guid studentId,MappingCorrectionInput input)
    {
        var s=await actor.Student(db,studentId);
        if(input.AttemptIds.Length==0 || input.AttemptIds.Length>100 || input.AttemptIds.Distinct().Count()!=input.AttemptIds.Length || string.IsNullOrWhiteSpace(input.Reason))throw new ApiError(422,"CORRECTION_INVALID","选择 1～100 条不同作答并填写更正依据。");
        var release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==input.ReleaseId && r.FamilyId==actor.FamilyId && !r.Withdrawn)??throw new ApiError(404,"NOT_FOUND","更正内容版本不存在。");
        var catalog=Json.Read<Catalog>(release.Payload);var changes=new List<object>();
        var attempts=await db.Attempts.Where(a=>a.StudentId==studentId && input.AttemptIds.Contains(a.Id)).OrderBy(a=>a.Sequence).ToListAsync();
        if(attempts.Count!=input.AttemptIds.Length || attempts.Any(a=>a.Number!=1))throw new ApiError(422,"FIRST_ATTEMPTS_ONLY","只能更正该学生的首次作答映射。");
        foreach(var attempt in attempts)
        {
            var session=await db.Sessions.SingleAsync(x=>x.Id==attempt.SessionId);
            var question=catalog.Questions.SingleOrDefault(q=>q.Id==session.QuestionId)??throw new ApiError(422,"QUESTION_IDENTITY_MISMATCH","更正版本必须保留同一题目稳定身份。");
            var old=Json.Read<Catalog>((await db.Releases.SingleAsync(r=>r.Id==session.ReleaseId)).Payload).Questions.Single(q=>q.Id==session.QuestionId);
            if(old.Stem!=question.Stem || old.Answer!=question.Answer || old.Type!=question.Type || old.Difficulty!=question.Difficulty || old.VariantGroupId!=question.VariantGroupId)throw new ApiError(422,"MAPPING_ONLY_CORRECTION","本命令只更正映射；题干、答案或难度更正须单独处理判分依据。");
            changes.Add(new {attemptId=attempt.Id,before=old.Mappings,after=question.Mappings,old.Policy,newPolicy=question.Policy});
        }
        var cursor=await db.Attempts.Where(a=>a.StudentId==studentId).MaxAsync(a=>(long?)a.Sequence)??0;
        var hash=Content.Hash(Json.Write(new {studentId,release.Id,release.Hash,s.ActiveGenerationId,cursor,changes,input.Reason}));
        return(s,release,hash,changes.ToArray());
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/students/{id:guid}/mapping-corrections:preview",async (Guid id,MappingCorrectionInput input,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");var p=await Preview(db,a,id,input);return TypedResults.Ok(new {previewHash=p.Hash,changes=p.Changes,notice="使用审核发布的映射重放证据和日程，原始作答与领取内容不改。"});});
        api.MapPost("/students/{id:guid}/mapping-corrections:confirm",async (Guid id,MappingCorrectionInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Parent");var p=await Preview(db,a,id,input);
            if(input.PreviewHash!=p.Hash)throw new ApiError(412,"CORRECTION_PREVIEW_CHANGED","作答或评估世代已变化，请重新预览。");
            var batch=new CorrectionBatch {FamilyId=a.FamilyId,StudentId=id,ReleaseId=p.Release.Id,PreviewHash=p.Hash,Reason=input.Reason,ConfirmedBy=a.Id,Cause="Mapping",AffectedAttemptIds=Json.Write(input.AttemptIds)};db.Add(batch);
            await DomainEvents.Append(db,a.FamilyId,id,batch.Id,"CorrectionBatch","CorrectionConfirmed",new{batchId=batch.Id,batch.Cause,attemptIds=input.AttemptIds,batch.ReleaseId,batch.ConfirmedBy});
            foreach(var attemptId in input.AttemptIds)
            {
                var attempt=await db.Attempts.SingleAsync(x=>x.Id==attemptId && x.FamilyId==a.FamilyId);
                var session=await db.Sessions.SingleAsync(x=>x.Id==attempt.SessionId && x.FamilyId==a.FamilyId);
                var question=Json.Read<Catalog>(p.Release.Payload).Questions.Single(q=>q.Id==session.QuestionId);
                var mapping=await PublishedMappings.Resolve(db,p.Release,question);
                db.Add(new CorrectionItem {FamilyId=a.FamilyId,BatchId=batch.Id,AttemptId=attemptId,MappingReleaseId=p.Release.Id,QuestionRevisionId=question.RevisionId,MappingSetRevisionId=mapping});
                await DomainEvents.Append(db,a.FamilyId,id,attemptId,"Attempt","CorrectionConfirmed",new{attemptId,batchId=batch.Id,mappingReleaseId=p.Release.Id,questionRevisionId=question.RevisionId,mappingSetRevisionId=mapping},attemptId);
            }
            return TypedResults.Accepted("/api/v1/students/"+id+"/mastery",batch);
        });
    }
}
