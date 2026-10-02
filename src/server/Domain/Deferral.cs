using Microsoft.EntityFrameworkCore;
namespace Learning;
public record DeferInput(DateOnly Date,string Reason);
public static class Deferral
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/tasks/{id:guid}:defer",async (Guid id,DeferInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Parent");var task=await db.Tasks.SingleOrDefaultAsync(t=>t.Id==id && t.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","任务不存在。");
            if(task.Status is not "Ready" and not "InProgress" and not "Deferred" || string.IsNullOrWhiteSpace(input.Reason))throw new ApiError(422,"DEFER_INVALID","只能顺延尚未完成的任务，需填写原因。");
            var s=await a.Student(db,task.StudentId);
            var active=await (from place in db.Placements join plan in db.Plans on (Guid?)place.RevisionId equals plan.ActiveRevisionId where place.TaskId==id select plan).ToListAsync();
            if(active.Any(p=>p.Date==input.Date))throw new ApiError(422,"SAME_DATE","请选择另一个日期。");
            if(task.Status=="InProgress" && task.StartedAt!=null){task.TrackedSeconds+=(int)Math.Max(0,(DateTimeOffset.UtcNow-task.StartedAt.Value).TotalSeconds);task.StartedAt=null;task.ActualMinutes=Math.Max(1,(int)Math.Ceiling(task.TrackedSeconds/60m));}
            task.Status="Deferred";
            foreach(var plan in active)
            {
                var old=await db.PlanRevisions.SingleAsync(r=>r.Id==plan.ActiveRevisionId);
                var revision=new PlanRevision {FamilyId=a.FamilyId,PlanId=plan.Id,ReleaseId=old.ReleaseId,Number=await db.PlanRevisions.Where(r=>r.PlanId==plan.Id).MaxAsync(r=>r.Number)+1,Budget=old.Budget,Reserved=old.Reserved,Status="Published",InputHash=Content.Hash(old.InputHash+Json.Write(input)+id),Warnings=old.Warnings,Candidates=old.Candidates};db.Add(revision);
                var oldPlaces=await db.Placements.Where(p=>p.RevisionId==old.Id).OrderBy(p=>p.Sequence).ToListAsync();
                foreach(var place in oldPlaces.Where(p=>p.TaskId!=id))db.Placements.Add(new(){FamilyId=a.FamilyId,RevisionId=revision.Id,TaskId=place.TaskId,Sequence=place.Sequence});
                var beforeIds=oldPlaces.Select(p=>p.TaskId).ToArray();var afterIds=beforeIds.Where(t=>t!=id).ToArray();
                db.Audits.Add(new(){FamilyId=a.FamilyId,StudentId=task.StudentId,ActorId=a.Id,Action="PlanAdjusted",Details=Json.Write(new PlanAdjustmentDetails(plan.Id,revision.Id,input.Reason,beforeIds,afterIds,[id],[],afterIds,"TaskDeferral",input.Date))});
                plan.ActiveRevisionId=revision.Id;
            }
            var target=await Planning.Generate(db,s,input.Date);
            if(target.Status!="Draft")throw new ApiError(409,"TARGET_PLAN_PUBLISHED","目标日期已有相同已发布计划，请先调整输入重新生成。");
            if(!await db.Placements.AnyAsync(p=>p.RevisionId==target.Id && p.TaskId==id))db.Placements.Add(new(){FamilyId=a.FamilyId,RevisionId=target.Id,TaskId=id,Sequence=await db.Placements.CountAsync(p=>p.RevisionId==target.Id)});
            task.Locked=true;task.ReasonCode="DEFERRED";task.Reason=$"家长顺延：{input.Reason}";
            target.InputHash=Content.Hash(target.InputHash+id+Json.Write(input));await Planning.RefreshBudget(db,target);
            return Results.Ok(new {task,revision=target,notice="旧日任务入口已停用，请发布目标日草稿。"});
        });
    }
}
