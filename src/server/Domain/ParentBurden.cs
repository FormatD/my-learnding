using Microsoft.EntityFrameworkCore;
namespace Learning;

public class ParentBurdenRecord : Row
{
    public Guid StudentId { get; set; }
    public Guid RecordedBy { get; set; }
    public DateOnly Date { get; set; }
    public string Category { get; set; } = "Daily";
    public decimal Minutes { get; set; }
    public string Note { get; set; } = "";
    public Guid? SupersedesId { get; set; }
    public string CorrectionReason { get; set; } = "";
    public string Method { get; set; } = "ParentReported";
}
public record ParentBurdenInput(DateOnly Date,string Category,decimal Minutes,string? Note=null,string? Reason=null);
public record ParentBurdenSummary(string Method,decimal DailyMinutes,decimal ContentReviewMinutes,int RecordedDays,int RecordCount,ParentBurdenRecord[] Records,ParentBurdenRecord[] History);
public static class ParentBurden
{
    static void Validate(ParentBurdenInput input,Student s)
    {
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone)).DateTime);
        if(input.Date>today || input.Date<today.AddDays(-27) || input.Category is not "Daily" and not "ContentReview" || input.Minutes is <.1m or >240m || decimal.Round(input.Minutes,1)!=input.Minutes || (input.Note?.Length??0)>500 || (input.Reason?.Length??0)>500)
            throw new ApiError(422,"INVALID_BURDEN_RECORD","请填写最近28天的真实投入，选择日常维护或内容审核；分钟为0.1～240，最多一位小数，说明不超过500字。");
    }
    public static async Task<ParentBurdenSummary> Summary(Database db,Guid studentId,DateOnly start,DateOnly end)
    {
        var all=await db.Set<ParentBurdenRecord>().Where(r=>r.StudentId==studentId).OrderBy(r=>r.CreatedAt).ThenBy(r=>r.Id).ToListAsync();
        var replaced=all.Where(r=>r.SupersedesId!=null).Select(r=>r.SupersedesId!.Value).ToHashSet();
        var active=all.Where(r=>!replaced.Contains(r.Id) && r.Date>=start && r.Date<=end).ToArray();
        var included=all.Where(r=>r.Date>=start && r.Date<=end).Select(r=>r.Id).ToHashSet();
        var byId=all.ToDictionary(r=>r.Id);
        foreach(var record in active){var previous=record.SupersedesId;while(previous!=null && byId.TryGetValue(previous.Value,out var ancestor)){included.Add(ancestor.Id);previous=ancestor.SupersedesId;}}
        return new("ParentReported",active.Where(r=>r.Category=="Daily").Sum(r=>r.Minutes),active.Where(r=>r.Category=="ContentReview").Sum(r=>r.Minutes),active.Select(r=>r.Date).Distinct().Count(),active.Length,active,all.Where(r=>included.Contains(r.Id)).ToArray());
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/students/{id:guid}/parent-burden",async(Guid id,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone)).DateTime);return await Summary(db,id,today.AddDays(-27),today);});
        api.MapPost("/students/{id:guid}/parent-burden",async(Guid id,ParentBurdenInput input,Database db,HttpContext ctx)=>{
            var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);Validate(input,s);
            var r=new ParentBurdenRecord {FamilyId=a.FamilyId,StudentId=id,RecordedBy=a.Id,Date=input.Date,Category=input.Category,Minutes=input.Minutes,Note=input.Note??""};db.Add(r);return TypedResults.Created($"/api/v1/students/{id}/parent-burden",r);
        });
        api.MapPost("/parent-burden/{id:guid}:correct",async(Guid id,ParentBurdenInput input,Database db,HttpContext ctx)=>{
            var a=ctx.Actor();a.Require("Parent");var old=await db.Set<ParentBurdenRecord>().SingleOrDefaultAsync(r=>r.Id==id && r.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","记录不存在。");
            var s=await a.Student(db,old.StudentId);Validate(input,s);
            if(string.IsNullOrWhiteSpace(input.Reason))throw new ApiError(422,"CORRECTION_REASON_REQUIRED","更正必须说明原因，原记录会保留。");
            if(await db.Set<ParentBurdenRecord>().AnyAsync(r=>r.SupersedesId==id))throw new ApiError(409,"BURDEN_RECORD_SUPERSEDED","该记录已更正，请刷新后修改最新记录。");
            var r=new ParentBurdenRecord {FamilyId=a.FamilyId,StudentId=old.StudentId,RecordedBy=a.Id,Date=input.Date,Category=input.Category,Minutes=input.Minutes,Note=input.Note??"",SupersedesId=id,CorrectionReason=input.Reason};db.Add(r);return TypedResults.Created($"/api/v1/students/{s.Id}/parent-burden",r);
        });
    }
}
