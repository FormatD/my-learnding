using Microsoft.EntityFrameworkCore;

namespace Learning;
public record PaperDraftInput(Guid ReleaseId,Guid KCId,string Answer,string Explanation,string Type="ShortAnswer");
public record PaperConfirmation(Guid ReleaseId,Guid QuestionId,string Result,string Reason,bool SameQuestionConfirmed=false,int HintLevel=0,bool AnswerShown=false,ObservedStep[]? Steps=null,string? PreviewHash=null);
public static class PaperLearning
{
    static readonly string[] ErrorTypes=["Concept","Calculation","Reading","Unit","Expression","Careless","Unknown"];
    static async Task<PaperWrong> Owned(Database db,Actor actor,Guid id)=>await db.Set<PaperWrong>().SingleOrDefaultAsync(w=>w.Id==id && w.FamilyId==actor.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到纸质错题。");
    internal static async Task ValidateInput(Database db,Actor actor,PaperInput input)
    {
        if(string.IsNullOrWhiteSpace(input.Stem) && input.FileId==null || input.Stem.Length>5000 || input.Answer.Length>5000 || !ErrorTypes.Contains(input.ErrorType))throw new ApiError(422,"PAPER_INVALID","请提供原题或图片，正文与答案各不超过 5000 字，并选择错误类型。");
        if(input.FileId!=null && !await db.Set<PrivateFile>().AnyAsync(f=>f.Id==input.FileId && f.FamilyId==actor.FamilyId && f.MimeType.StartsWith("image/")))throw new ApiError(404,"NOT_FOUND","私有图片不存在。");
    }
    static void Pending(PaperWrong wrong)
    {
        if(wrong.AttemptId!=null || wrong.Status=="Confirmed")throw new ApiError(409,"PAPER_ALREADY_CONFIRMED","已确认的原始记录不能覆盖，请通过判分或映射更正保留历史。");
    }
    static async Task<(PaperWrong Wrong,Release Release,Catalog Catalog,Question Question,string Hash)> Preview(Database db,Actor actor,Guid id,PaperConfirmation input)
    {
        var wrong=await Owned(db,actor,id);Pending(wrong);var student=await actor.Student(db,wrong.StudentId);
        var release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==input.ReleaseId && r.FamilyId==actor.FamilyId && !r.Withdrawn)??throw new ApiError(404,"NOT_FOUND","请选择未撤回的正式内容版本。");
        var catalog=Json.Read<Catalog>(release.Payload);var q=catalog.Questions.SingleOrDefault(q=>q.Id==input.QuestionId)??throw new ApiError(422,"QUESTION_UNPUBLISHED","题目未在这个正式版本中发布。");
        if(string.IsNullOrWhiteSpace(wrong.Answer))throw new ApiError(422,"PAPER_ANSWER_REQUIRED","请先补齐孩子的原始答案；不能用标准答案代替。");
        if(!input.SameQuestionConfirmed)throw new ApiError(422,"SAME_QUESTION_REQUIRED","请核对纸质原题与正式题干、条件一致，不能关联一道相似题代替原题。");
        if(input.HintLevel is <0 or >3)throw new ApiError(422,"INVALID_HINT","提示程度无效。");
        Endpoints.ValidateGrade(new(input.Result,input.Reason,input.Steps),q);
        var count=await db.Attempts.CountAsync(a=>a.StudentId==student.Id);
        var hash=Content.Hash(Json.Write(new {wrong,release.Id,release.Hash,student.ActiveGenerationId,count,input.ReleaseId,input.QuestionId,input.Result,input.Reason,input.SameQuestionConfirmed,input.HintLevel,input.AnswerShown,input.Steps}));
        return(wrong,release,catalog,q,hash);
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPut("/paper-wrongs/{id:guid}",async(Guid id,PaperInput input,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("Parent");var wrong=await Owned(db,actor,id);Pending(wrong);
            await ValidateInput(db,actor,input);
            wrong.Stem=input.Stem;wrong.Answer=input.Answer;wrong.FileId=input.FileId;wrong.ErrorType=input.ErrorType;return Results.Ok(wrong);
        });
        api.MapPost("/paper-wrongs/{id:guid}/draft",async(Guid id,PaperDraftInput input,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("ContentEditor");var wrong=await Owned(db,actor,id);Pending(wrong);
            if(wrong.DraftId!=null)throw new ApiError(409,"PAPER_DRAFT_EXISTS","这条纸质错题已有草稿，请继续编辑原草稿。");
            var release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==input.ReleaseId && r.FamilyId==actor.FamilyId && !r.Withdrawn)??throw new ApiError(404,"NOT_FOUND","正式内容版本不存在。");
            var catalog=Json.Read<Catalog>(release.Payload);
            if(!catalog.Kcs.Any(k=>k.Id==input.KCId) || string.IsNullOrWhiteSpace(wrong.Stem) || string.IsNullOrWhiteSpace(input.Answer) || string.IsNullOrWhiteSpace(input.Explanation) || input.Type is not "Numeric" and not "ShortAnswer")throw new ApiError(422,"PAPER_DRAFT_INVALID","补齐原题、参考答案、解析，并选择已发布能力及题型。");
            var question=new Question(Guid.NewGuid(),Guid.NewGuid(),wrong.Stem,input.Answer,input.Explanation,input.Type,"Medium","SingleKC",[new(input.KCId)]);
            var draft=new ContentDraft {FamilyId=actor.FamilyId,Title="纸质题目审核草稿",Payload=Json.Write(catalog with {Questions=[..catalog.Questions,question]})};
            db.Drafts.Add(draft);wrong.DraftId=draft.Id;wrong.QuestionId=question.Id;return Results.Created("/api/v1/content",new {draft,questionId=question.Id,notice="参考答案与映射尚需人工审核发布；草稿不产生证据。"});
        });
        api.MapPost("/paper-wrongs/{id:guid}/preview",async(Guid id,PaperConfirmation input,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("Parent");var p=await Preview(db,actor,id,input);
            return Results.Ok(new {previewHash=p.Hash,question=p.Question,kcs=p.Catalog.Kcs.Where(k=>p.Question.Mappings.Any(m=>m.KCId==k.Id)),result=input.Result,answer=p.Wrong.Answer,notice="确认后以本次确认时间代录首次作答；不会伪造历史练习间隔。只有已审核映射与已观察步骤可以产生证据。"});
        });
        api.MapPost("/paper-wrongs/{id:guid}/confirm",async(Guid id,PaperConfirmation input,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("Parent");var p=await Preview(db,actor,id,input);
            if(input.PreviewHash!=p.Hash)throw new ApiError(412,"PAPER_PREVIEW_CHANGED","原题、正式内容或学习记录已变化，请重新预览。");
            var task=new StudyTask {FamilyId=actor.FamilyId,StudentId=p.Wrong.StudentId,ReleaseId=p.Release.Id,QuestionId=p.Question.Id,KCId=p.Question.Mappings.FirstOrDefault(m=>m.Mode!="None")?.KCId,Type="PaperRecorded",Title="纸质结果代录",Status="Completed",Minutes=0,ReasonCode="PARENT_PAPER_CONFIRMED",Reason=input.Reason};
            var session=new LearningSession {FamilyId=actor.FamilyId,StudentId=p.Wrong.StudentId,TaskId=task.Id,ReleaseId=p.Release.Id,QuestionId=p.Question.Id,HintLevel=input.HintLevel,AnswerShown=input.AnswerShown};
            var attempt=new Attempt {FamilyId=actor.FamilyId,StudentId=p.Wrong.StudentId,SessionId=session.Id,ClientSubmissionId=p.Wrong.Id,Number=1,Answer=p.Wrong.Answer,AnswerSource="ParentPaperConfirmed",HintLevel=input.HintLevel,AnswerShown=input.AnswerShown};
            var grade=new Grading {FamilyId=actor.FamilyId,AttemptId=attempt.Id,Number=1,Result=input.Result,Method="ParentConfirmed",Reason=input.Reason,GradedBy=actor.Id,Steps=Json.Write(input.Steps??[])};
            db.Tasks.Add(task);db.Sessions.Add(session);db.Attempts.Add(attempt);db.Gradings.Add(grade);db.Outbox.Add(new() {FamilyId=actor.FamilyId,StudentId=p.Wrong.StudentId,AttemptId=attempt.Id});
            p.Wrong.Status="Confirmed";p.Wrong.QuestionId=p.Question.Id;p.Wrong.ReleaseId=p.Release.Id;p.Wrong.AttemptId=attempt.Id;p.Wrong.ConfirmedBy=actor.Id;p.Wrong.ConfirmedAt=DateTimeOffset.UtcNow;p.Wrong.ConfirmationReason=input.Reason;
            return Results.Accepted("/api/v1/students/"+p.Wrong.StudentId+"/mastery",p.Wrong);
        });
    }
}
