using Microsoft.EntityFrameworkCore;
namespace Learning;
public record ResourceIssueInput(string Reason);
public record ResourceIssueDetail(Guid TaskId,string Title,string ResourceRef,string? ResourceUrl,Guid? ReleaseId,string Reason);
public record ResourceIssueView(Guid Id,DateTimeOffset CreatedAt,ResourceIssueDetail Issue);
public static class ResourceIssues
{
 public static void Map(RouteGroupBuilder api)
 {
  api.MapPost("/tasks/{id:guid}/resource-issues",async(Guid id,ResourceIssueInput input,Database db,HttpContext ctx)=>{
   var a=ctx.Actor();var task=await db.Tasks.SingleOrDefaultAsync(t=>t.Id==id&&t.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到任务。");var student=await a.Student(db,task.StudentId);
   if(task.QuestionId!=null)throw new ApiError(422,"NOT_RESOURCE_TASK","请在资源学习任务中报告资源问题。");
   if(string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Length>500)throw new ApiError(422,"REASON_REQUIRED","请填写500字以内的资源问题说明。");
   var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(student.TimeZone)).DateTime);
   var active=await (from plan in db.Plans join placement in db.Placements on plan.ActiveRevisionId equals (Guid?)placement.RevisionId where placement.TaskId==id&&plan.StudentId==student.Id&&(a.Role!="Child"||plan.Date==today) select plan).AnyAsync();
   if(!active)throw new ApiError(409,"TASK_NOT_ACTIVE","任务尚未发布或已被调整。");
   var issue=new ResourceIssueDetail(task.Id,task.Title,task.ResourceRef,task.ResourceUrl,task.ReleaseId,input.Reason.Trim());
   var row=new Audit{FamilyId=a.FamilyId,StudentId=student.Id,ActorId=a.Id,Action="ResourceIssueReported",Details=Json.Write(issue)};db.Audits.Add(row);
   return TypedResults.Created($"/api/v1/students/{student.Id}/resource-issues",new ResourceIssueView(row.Id,row.CreatedAt,issue));
  });
  api.MapGet("/students/{id:guid}/resource-issues",async(Guid id,Database db,HttpContext ctx)=>{
   var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);var rows=await db.Audits.AsNoTracking().Where(r=>r.FamilyId==a.FamilyId&&r.StudentId==id&&r.Action=="ResourceIssueReported").OrderByDescending(r=>r.CreatedAt).ThenBy(r=>r.Id).Take(50).ToArrayAsync(ctx.RequestAborted);
   return rows.Select(r=>new ResourceIssueView(r.Id,r.CreatedAt,Json.Read<ResourceIssueDetail>(r.Details))).ToArray();
  });
 }
}
