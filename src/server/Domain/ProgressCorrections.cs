using Microsoft.EntityFrameworkCore;
namespace Learning;
public class ProgressChange : Row
{
    public Guid StudentId { get; set; }
    public Guid OldProgressId { get; set; }
    public Guid? NewProgressId { get; set; }
    public Guid ConfirmedBy { get; set; }
    public string Reason { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
}
public record ProgressCorrectionInput(Guid? LessonId,string Reason);
public static class ProgressCorrections
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/students/{id:guid}/progress-changes",async(Guid id,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);return await db.Set<ProgressChange>().Where(c=>c.StudentId==id).OrderByDescending(c=>c.CreatedAt).Take(100).ToListAsync();});
        api.MapPost("/students/{id:guid}/progress/{progressId:guid}:correct",async(Guid id,Guid progressId,ProgressCorrectionInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Parent");var student=await a.Student(db,id);
            var old=await db.Progresses.SingleOrDefaultAsync(p=>p.Id==progressId && p.StudentId==id && p.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","进度记录不存在。");
            if(old.Status!="Confirmed")throw new ApiError(409,"PROGRESS_ALREADY_CORRECTED","进度已经被更正或撤回，请刷新。");
            if(string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>500 || input.LessonId==old.LessonId)throw new ApiError(422,"PROGRESS_CORRECTION_INVALID","选择不同课时或撤回，并填写 1～500 字的更正原因。");
            Release? release=null;
            if(input.LessonId!=null)
            {
                release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==student.ActiveReleaseId && r.FamilyId==a.FamilyId && !r.Withdrawn);
                if(release==null || !Json.Read<Catalog>(release.Payload).Lessons.Any(l=>l.Id==input.LessonId))throw new ApiError(422,"LESSON_UNPUBLISHED","更正课时必须属于当前发布内容。");
            }
            var before=Json.Write(old);old.Status=input.LessonId==null?"Withdrawn":"Superseded";
            Progress? replacement=null;
            if(input.LessonId!=null)
            {
                replacement=await db.Progresses.SingleOrDefaultAsync(p=>p.StudentId==id && p.Date==old.Date && p.LessonId==input.LessonId);
                if(replacement==null){replacement=new Progress {FamilyId=a.FamilyId,StudentId=id,Date=old.Date,LessonId=input.LessonId.Value};db.Add(replacement);}
                replacement.Status="Confirmed";replacement.Source="ParentCorrected";replacement.ReleaseId=release!.Id;
            }
            var change=new ProgressChange {FamilyId=a.FamilyId,StudentId=id,OldProgressId=old.Id,NewProgressId=replacement?.Id,ConfirmedBy=a.Id,Reason=input.Reason,Before=before,After=Json.Write(new {previous=old,replacement})};db.Add(change);
            return Results.Ok(new {previous=old,replacement,change,notice="后续草稿采用更正后的进度；已发布计划和历史作答保留。"});
        });
    }
}
