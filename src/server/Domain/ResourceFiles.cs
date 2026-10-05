using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
namespace Learning;
public static class ResourceFiles
{
 public static string Snapshot(PrivateFile file)=>Content.Hash(Json.Write(new{file.Id,file.Name,file.MimeType,file.Hash,purpose="LearningResource"}));
 static string BytesHash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 public static bool ValidHeader(string mime,byte[] bytes)=>mime switch{
  "application/pdf"=>bytes.AsSpan().StartsWith("%PDF-"u8),
  "image/png"=>bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}),
  "image/jpeg"=>bytes.Length>3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255,
  "audio/wav"=>bytes.Length>=12&&bytes.AsSpan(0,4).SequenceEqual("RIFF"u8)&&bytes.AsSpan(8,4).SequenceEqual("WAVE"u8),
  "audio/mpeg"=>bytes.AsSpan().StartsWith("ID3"u8)||bytes.Length>=2&&bytes[0]==255&&(bytes[1]&224)==224,
  _=>false};
 public static async Task<PrivateFile> Resolve(Database db,Guid family,Resource resource,CancellationToken ct=default)
 {
  var file=await db.Set<PrivateFile>().AsNoTracking().SingleOrDefaultAsync(f=>f.Id==resource.FileId&&f.FamilyId==family&&f.Purpose=="LearningResource",ct)??throw new ApiError(422,"RESOURCE_FILE_UNKNOWN","学习文件不可用，请重新上传并审核资源。");
  if(resource.FileSnapshotHash!=Snapshot(file)||file.Hash!=BytesHash(file.Bytes)||!ValidHeader(file.MimeType,file.Bytes))throw new ApiError(422,"RESOURCE_FILE_CHANGED","学习文件与审核快照不一致，请重新核对并发布新修订。");return file;
 }
 public static async Task Validate(Database db,Guid family,Catalog catalog)
 {
  foreach(var resource in catalog.Resources.Where(r=>r.FileId!=null))await Resolve(db,family,resource);
 }
 public static void Bind(StudyTask task,Resource resource)
 {
  task.ResourceId=resource.Id;task.ResourceRevisionId=resource.RevisionId;task.ResourceRef=resource.PaperReference;
  task.ResourceUrl=resource.FileId==null?resource.Url:$"/api/v1/tasks/{task.Id}/resource-file";
 }
 public static void Map(RouteGroupBuilder api)
 {
  api.MapPost("/content/resource-files",(FileInput input,Database db,HttpContext ctx)=>{
   var actor=ctx.Actor();actor.Require("ContentEditor");byte[] bytes;try{bytes=Convert.FromBase64String(input.Base64);}catch(Exception ex)when(ex is FormatException or ArgumentException){throw new ApiError(422,"INVALID_FILE","文件编码无效。");}
   if(bytes.Length==0||bytes.Length>10_000_000||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>200||string.IsNullOrWhiteSpace(Path.GetFileName(input.Name))||input.Name.Any(char.IsControl))throw new ApiError(422,"FILE_SIZE","文件须在10 MB以内且有有效名称。");
   if(!ValidHeader(input.MimeType,bytes))throw new ApiError(422,"FILE_TYPE","学习文件支持PDF、PNG、JPEG、WAV或MP3，须有正确文件头。");
   var file=new PrivateFile{FamilyId=actor.FamilyId,Purpose="LearningResource",Name=Path.GetFileName(input.Name),MimeType=input.MimeType,Hash=BytesHash(bytes),Bytes=bytes};db.Add(file);
   return TypedResults.Created($"/api/v1/files/{file.Id}",new{file.Id,file.Name,file.MimeType,file.Hash,size=bytes.Length,fileSnapshotHash=Snapshot(file)});
  });
  api.MapGet("/tasks/{id:guid}/resource-file",async(Guid id,Database db,HttpContext ctx)=>{
   var actor=ctx.Actor();var task=await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==id&&t.FamilyId==actor.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到任务材料。");var student=await actor.Student(db,task.StudentId);
   var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(student.TimeZone)).DateTime);
   var active=await(from plan in db.Plans join placement in db.Placements on plan.ActiveRevisionId equals (Guid?)placement.RevisionId where placement.TaskId==id&&plan.StudentId==student.Id&&(actor.Role!="Child"||plan.Date==today)select plan).AnyAsync(ctx.RequestAborted);
   if(!active)throw new ApiError(409,"TASK_NOT_ACTIVE","任务尚未发布或已被调整。");
   var release=await db.Releases.AsNoTracking().SingleAsync(r=>r.Id==task.ReleaseId&&r.FamilyId==actor.FamilyId,ctx.RequestAborted);if(release.Withdrawn)throw new ApiError(422,"WITHDRAWN","学习材料版本已撤回，请联系家长。");
   var resource=Json.Read<Catalog>(release.Payload).Resources.SingleOrDefault(r=>r.Id==task.ResourceId&&r.RevisionId==task.ResourceRevisionId&&r.FileId!=null)??throw new ApiError(404,"NOT_FOUND","该任务没有已审核的文件材料。");
   var file=await Resolve(db,actor.FamilyId,resource,ctx.RequestAborted);return Results.File(file.Bytes,file.MimeType,file.Name,enableRangeProcessing:true);
  }).WithMetadata(new DownloadResponseMetadata(["application/pdf","image/png","image/jpeg","audio/wav","audio/mpeg"],SupportsRanges:true));
  api.MapGet("/plans/{id:guid}/resources",async(Guid id,Database db,HttpContext ctx)=>{
   var actor=ctx.Actor();actor.Require("Parent");var revision=await db.PlanRevisions.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id&&r.FamilyId==actor.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到计划。");var plan=await db.Plans.SingleAsync(p=>p.Id==revision.PlanId&&p.FamilyId==actor.FamilyId);await actor.Student(db,plan.StudentId);
   var release=await db.Releases.SingleAsync(r=>r.Id==revision.ReleaseId&&r.FamilyId==actor.FamilyId);return Json.Read<Catalog>(release.Payload).Resources;
  });
 }
}
