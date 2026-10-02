using Microsoft.EntityFrameworkCore;
namespace Learning;
public class GoalChange : Row
{
    public Guid StudentId { get; set; }
    public Guid GoalId { get; set; }
    public long Version { get; set; }
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid ConfirmedBy { get; set; }
}
public record GoalSnapshot(Guid Id,long Version,string Scope,string Title,string Subject,string GoalType,Guid? KCId,int Minutes,string Period,int TargetValue,string ScheduleRule,int Priority,DateOnly? StartDate,DateOnly? EndDate);
public static class Goals
{
    public static bool Scheduled(Goal goal,DateOnly date)=>goal.Active && (!goal.StartDate.HasValue || date>=goal.StartDate) && (!goal.EndDate.HasValue || date<=goal.EndDate) && Json.Read<int[]>(goal.ScheduleRule).Contains((int)date.DayOfWeek);
    public static DateOnly PeriodStart(Goal g,DateOnly date)=>g.Period=="Daily"?date:date.AddDays(-((int)date.DayOfWeek+6)%7);
    public static GoalSnapshot Snapshot(Goal g)=>new(g.Id,g.Version,Content.Hash(Json.Write(new{g.Subject,g.GoalType,g.KCId})),g.Title,g.Subject,g.GoalType,g.KCId,g.Minutes,g.Period,g.TargetValue,g.ScheduleRule,g.Priority,g.StartDate,g.EndDate);
    public static int Completed(Goal goal,DateOnly date,string zone,IEnumerable<StudyTask> tasks,Guid[] answeredTaskIds,bool dailyOnly=false)
    {
        var start=dailyOnly?date:PeriodStart(goal,date);if(goal.StartDate>start)start=goal.StartDate.Value;
        var end=goal.EndDate<date?goal.EndDate.Value:date;var scope=Snapshot(goal).Scope;
        return tasks.Count(task=>
        {
            var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(task.CompletedAt!.Value,TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
            return day>=start && day<=end && Json.Read<GoalSnapshot[]>(task.GoalSnapshots).Any(snapshot=>snapshot.Id==goal.Id && snapshot.Scope==scope) && (goal.GoalType!="Practice" || answeredTaskIds.Contains(task.Id));
        });
    }
    static async Task Apply(Database db,Actor actor,Goal goal,GoalInput input,bool updating=false)
    {
        var legacy=updating && input.Subject==null && input.GoalType==null && input.Period==null && input.TargetValue==null && input.Days==null && input.Priority==null && input.StartDate==null && input.EndDate==null;
        input=input with {Subject=input.Subject??goal.Subject,GoalType=input.GoalType??goal.GoalType,Period=input.Period??goal.Period,TargetValue=input.TargetValue??goal.TargetValue,Days=input.Days??Json.Read<int[]>(goal.ScheduleRule),Priority=input.Priority??goal.Priority,KCId=legacy?goal.KCId:input.KCId,StartDate=legacy?goal.StartDate:input.StartDate,EndDate=legacy?goal.EndDate:input.EndDate};
        var days=(input.Days??[1,2,3,4,5,6,0]).Distinct().OrderBy(x=>x).ToArray();
        if(string.IsNullOrWhiteSpace(input.Title) || input.Title.Length>200 || input.Minutes is <3 or >60 || string.IsNullOrWhiteSpace(input.PaperReference) || !new[]{"Math","Reading","English","Other","Unspecified"}.Contains(input.Subject) || !new[]{"Reading","Listening","Practice","Activity"}.Contains(input.GoalType) || !new[]{"Daily","Weekly"}.Contains(input.Period) || days.Length==0 || days.Any(d=>d is <0 or >6) || input.Priority is <1 or >5 || input.StartDate>input.EndDate || input.TargetValue<1 || input.TargetValue>(input.Period=="Daily"?1:days.Length) || string.IsNullOrWhiteSpace(input.Reason))throw new ApiError(422,"INVALID_GOAL","请填写目标与资源，选择有效日期、星期、优先级和周期次数；每天一次，每周次数不超过可安排天数。");
        if(input.GoalType=="Practice")
        {
            if(!(input.KCId!=null && goal.KCId==input.KCId))
            {
            var student=await actor.Student(db,goal.StudentId);
            var release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==student.ActiveReleaseId && r.FamilyId==actor.FamilyId && !r.Withdrawn);
            if(input.KCId==null || release==null || !Json.Read<Catalog>(release.Payload).Kcs.Any(k=>k.Id==input.KCId))throw new ApiError(422,"GOAL_KC_UNAVAILABLE","能力练习需要选择学生当前正式内容中的能力。");
            }
        }
        else if(input.KCId!=null)throw new ApiError(422,"GOAL_SCOPE_INVALID","阅读、听力与活动只记录行为；如需能力测量，请使用能力练习目标。");
        goal.Title=input.Title;goal.Minutes=input.Minutes;goal.PaperReference=input.PaperReference;goal.Active=input.Active;goal.Subject=input.Subject!;goal.GoalType=input.GoalType!;goal.KCId=input.KCId;goal.Period=input.Period!;goal.TargetValue=input.TargetValue!.Value;goal.ScheduleRule=Json.Write(days);goal.Priority=input.Priority!.Value;goal.StartDate=input.StartDate;goal.EndDate=input.EndDate;
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/students/{id:guid}/goals",async(Guid id,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);return await db.Goals.Where(g=>g.StudentId==id).OrderBy(g=>g.CreatedAt).ToListAsync();});
        api.MapGet("/students/{id:guid}/goal-changes",async(Guid id,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);return await db.Set<GoalChange>().Where(g=>g.StudentId==id).OrderByDescending(g=>g.CreatedAt).ToListAsync();});
        api.MapPost("/students/{id:guid}/goals",async(Guid id,GoalInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);var goal=new Goal{FamilyId=a.FamilyId,StudentId=id};await Apply(db,a,goal,input);db.Goals.Add(goal);db.Add(new GoalChange{FamilyId=a.FamilyId,StudentId=id,GoalId=goal.Id,Version=goal.Version,Before="null",After=Json.Write(goal),Reason=input.Reason,ConfirmedBy=a.Id});return Results.Ok(goal);
        });
        api.MapPut("/goals/{id:guid}",async(Guid id,GoalInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Parent");var goal=await db.Goals.SingleOrDefaultAsync(g=>g.Id==id && g.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","目标不存在。");var before=Json.Write(goal);await Apply(db,a,goal,input,true);goal.Version++;db.Add(new GoalChange{FamilyId=a.FamilyId,StudentId=goal.StudentId,GoalId=id,Version=goal.Version,Before=before,After=Json.Write(goal),Reason=input.Reason,ConfirmedBy=a.Id});return Results.Ok(goal);
        });
    }
}
