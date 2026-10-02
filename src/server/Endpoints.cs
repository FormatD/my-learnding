using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Learning;
public record Credentials(string UserName,string Password,string? FamilyName=null);
public record StudentInput(string Name,int Grade=3,int DailyMinutes=30,string TimeZone="Asia/Shanghai");
public record BudgetInput(int Minutes,int Reserved);
public record GoalInput(string Title,int Minutes,string PaperReference,bool Active=true,string? Subject=null,string? GoalType=null,Guid? KCId=null,string? Period=null,int? TargetValue=null,int[]? Days=null,int? Priority=null,DateOnly? StartDate=null,DateOnly? EndDate=null,string Reason="家长调整目标",Guid? CourseId=null,Guid? UnitId=null);
public record DraftInput(string Title,Catalog Catalog);
public record PublishInput(string PreviewHash,bool ConfirmWarnings=false);
public record TransitionInput(string Status,string Reason="",int? ActualMinutes=null);
public record AnswerInput(Guid ClientSubmissionId,string Answer);
public record HintInput(int Level);
public record GradeInput(string Result,string Reason,ObservedStep[]? Steps=null,string? PreviewHash=null);
public record ManualTaskInput(string Title,int Minutes,string ResourceRef,string Type="Resource",bool Mandatory=true,Guid? QuestionId=null);
public record AdjustInput(Guid[] TaskIds,Guid[] LockedIds,string Reason);
public record ReasonInput(string Reason);
public static class Endpoints
{
    static async Task<T> Owned<T>(Database db,Actor actor,Guid id) where T:Row => await db.Set<T>().SingleOrDefaultAsync(x => x.Id==id && x.FamilyId==actor.FamilyId) ?? throw new ApiError(404,"NOT_FOUND","找不到该记录。");
    static DateOnly Today(Student s) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone)).DateTime);
    static async Task<Question> QuestionFor(Database db,LearningSession s) => Json.Read<Catalog>((await db.Releases.SingleAsync(r => r.Id==s.ReleaseId)).Payload).Questions.Single(q => q.Id==s.QuestionId);
    static async Task<(Catalog Catalog,Question Question,Guid ReleaseId)> GradingContext(Database db,Attempt attempt)
    {
        var session=await db.Sessions.SingleAsync(x=>x.Id==attempt.SessionId && x.FamilyId==attempt.FamilyId);
        var correction=await db.Set<CorrectionItem>().Where(x=>x.AttemptId==attempt.Id && x.FamilyId==attempt.FamilyId).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync();
        var releaseId=correction?.MappingReleaseId??session.ReleaseId;
        var catalog=Json.Read<Catalog>((await db.Releases.SingleAsync(x=>x.Id==releaseId && x.FamilyId==attempt.FamilyId)).Payload);
        return(catalog,catalog.Questions.Single(q=>q.Id==session.QuestionId),releaseId);
    }
    internal static void ValidateGrade(GradeInput input,Question question)
    {
        if (!new[] { "Correct","Incorrect","Partial","Unscorable" }.Contains(input.Result) || string.IsNullOrWhiteSpace(input.Reason)) throw new ApiError(422,"INVALID_GRADE","请选择判分并填写依据。");
        var steps=input.Steps??[];
        if (steps.Any(x => !question.Mappings.Any(m => m.Step==x.Step && m.Mode=="StepObserved") || !new[] { "Correct","Incorrect","Unknown" }.Contains(x.Result) || x.HintLevel is <0 or >3) || steps.Select(x => x.Step).Distinct().Count()!=steps.Length) throw new ApiError(422,"INVALID_STEPS","观察步骤与当前有效映射不一致。");
        if (input.Result=="Partial" && steps.Length==0) throw new ApiError(422,"STEPS_REQUIRED","部分正确必须有观察步骤。");
    }
    public static void MapLearningEndpoints(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1");
        api.MapPost("/auth/register",async (Credentials input,Database db,HttpContext ctx) =>
        {
            if (input.UserName.Trim().Length<3 || input.Password.Length<12 || input.Password.Length>128) throw new ApiError(422,"INVALID_CREDENTIALS","用户名至少 3 字，密码至少 12 字。");
            if (await db.Accounts.AnyAsync(a => a.UserName==input.UserName.Trim())) throw new ApiError(409,"USERNAME_UNAVAILABLE","用户名不可用。");
            await using var tx=await db.Database.BeginTransactionAsync();
            var family=new Family { Name=input.FamilyName??"我的家庭" }; var account=new Account { FamilyId=family.Id,UserName=input.UserName.Trim(),PasswordHash=Security.Password(input.Password) };
            db.Families.Add(family); db.Accounts.Add(account);db.Add(new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles=account.Roles}); await db.SaveChangesAsync();family.OwnerAccountId=account.Id;
            await Security.CreateSession(db,ctx,family.Id,account.Id,null,"Parent"); await tx.CommitAsync();
            return Results.Created("/api/v1/me",new { familyId=family.Id,role="Parent" });
        }).RequireRateLimiting("auth");
        api.MapPost("/auth/login",async (Credentials input,Database db,HttpContext ctx) =>
        {
            var initial=await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserName==input.UserName.Trim());
            if(initial==null)throw new ApiError(401,"INVALID_CREDENTIALS","用户名或密码不正确。");
            await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(initial.FamilyId);
            var account=await db.Accounts.SingleOrDefaultAsync(a=>a.Id==initial.Id);
            if (account==null || !await db.Set<FamilyMembership>().AnyAsync(m=>m.FamilyId==account.FamilyId && m.AccountId==account.Id && m.Roles!="") || !Security.Check(input.Password,account.PasswordHash)) throw new ApiError(401,"INVALID_CREDENTIALS","用户名或密码不正确。");
            await Security.CreateSession(db,ctx,account.FamilyId,account.Id,null,"Parent");await tx.CommitAsync(); return Results.Ok(new { role="Parent" });
        }).RequireRateLimiting("auth");
        api.MapGet("/me",async (Database db,HttpContext ctx) => new { actor=ctx.Actor(),family=await db.Families.SingleAsync(f => f.Id==ctx.Actor().FamilyId) });
        api.MapPost("/logout",async (Database db,HttpContext ctx) => { (await db.AuthSessions.SingleAsync(s => s.Id==ctx.Actor().SessionId)).Revoked=true; ctx.Response.Cookies.Delete(Security.Cookie); return Results.Ok(new { done=true }); });
        api.MapGet("/students",async (Database db,HttpContext ctx) => ctx.Actor().Role!="Child" && !ctx.Actor().Roles.Split(',').Contains("Parent") ? [] : await db.Students.Where(s => s.FamilyId==ctx.Actor().FamilyId && (ctx.Actor().Role!="Child" || s.Id==ctx.Actor().StudentId)).OrderBy(s => s.CreatedAt).ToListAsync());
        api.MapPost("/students",async (StudentInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor(); a.Require("Parent"); ValidateStudent(input);
            var s=new Student { FamilyId=a.FamilyId,Name=input.Name,Grade=input.Grade,DailyMinutes=input.DailyMinutes,TimeZone=input.TimeZone }; db.Students.Add(s); return Results.Created($"/api/v1/students/{s.Id}",s);
        });
        api.MapPut("/students/{id:guid}",async (Guid id,StudentInput input,Database db,HttpContext ctx) => { ctx.Actor().Require("Parent"); ValidateStudent(input); var s=await ctx.Actor().Student(db,id); s.Name=input.Name; s.Grade=input.Grade;s.DailyMinutes=input.DailyMinutes;s.TimeZone=input.TimeZone; return Results.Ok(s); });
        api.MapPost("/students/{id:guid}/child-sessions",async (Guid id,Database db,HttpContext ctx) => { var a=ctx.Actor(); a.Require("Parent"); await a.Student(db,id); await Security.CreateSession(db,ctx,a.FamilyId,null,id,"Child"); return Results.Ok(new { role="Child",studentId=id }); });
        api.MapPut("/students/{id:guid}/availability/{date}",async (Guid id,DateOnly date,BudgetInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor(); a.Require("Parent"); await a.Student(db,id);
            if (input.Minutes<0 || input.Minutes>600 || input.Reserved<0 || input.Reserved>input.Minutes) throw new ApiError(422,"INVALID_BUDGET","可用时间为 0～600 分钟，预留不能超过总时间。");
            var value=await db.Availabilities.SingleOrDefaultAsync(v => v.StudentId==id && v.Date==date);
            if (value==null) { value=new() { FamilyId=a.FamilyId,StudentId=id,Date=date }; db.Availabilities.Add(value); }
            value.Minutes=input.Minutes;value.Reserved=input.Reserved;return Results.Ok(value);
        });
        api.MapGet("/students/{id:guid}/availability/{date}",async(Guid id,DateOnly date,Database db,HttpContext ctx)=>{ctx.Actor().Require("Parent");var s=await ctx.Actor().Student(db,id);var value=await db.Availabilities.SingleOrDefaultAsync(a=>a.StudentId==id && a.Date==date);return new {minutes=value?.Minutes??s.DailyMinutes,reserved=value?.Reserved??0};});
        api.MapGet("/students/{id:guid}/progress",async (Guid id,bool? includeHistory,Database db,HttpContext ctx) => { ctx.Actor().Require("Parent");await ctx.Actor().Student(db,id);return await db.Progresses.Where(p => p.StudentId==id && (includeHistory==true || p.Status=="Confirmed")).OrderByDescending(p => p.Date).ThenByDescending(p=>p.CreatedAt).Take(100).ToListAsync(); });
        api.MapPut("/students/{id:guid}/school-progress/{date}/{lessonId:guid}",async (Guid id,DateOnly date,Guid lessonId,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id); var release=await db.Releases.SingleOrDefaultAsync(r => r.Id==s.ActiveReleaseId && !r.Withdrawn);
            if (release==null || !Json.Read<Catalog>(release.Payload).Lessons.Any(l => l.Id==lessonId)) throw new ApiError(422,"LESSON_UNPUBLISHED","课时不在当前内容版本中。");
            var progress=await db.Progresses.SingleOrDefaultAsync(p => p.StudentId==id && p.Date==date && p.LessonId==lessonId);
            if (progress==null) { progress=new() { FamilyId=a.FamilyId,StudentId=id,Date=date,LessonId=lessonId,ReleaseId=release.Id }; db.Progresses.Add(progress); }
            else if(progress.Status!="Confirmed")
            {
                var before=Json.Write(progress);progress.Status="Confirmed";progress.ReleaseId=release.Id;progress.Source="ParentReconfirmed";
                db.Add(new ProgressChange {FamilyId=a.FamilyId,StudentId=id,OldProgressId=progress.Id,NewProgressId=progress.Id,Before=before,After=Json.Write(progress),Reason="家长重新确认学校进度",ConfirmedBy=a.Id});
            }
            return Results.Ok(progress);
        });
        Goals.Map(api);
        KnowledgeChanges.Map(api);
        Operations.Map(api);
        api.MapGet("/content/directory-sources",async(Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("ContentEditor");return await db.Sources.Where(s=>s.FamilyId==a.FamilyId).OrderBy(s=>s.Title).Select(s=>new{s.Id,s.Title}).ToArrayAsync();});
        api.MapGet("/content",async (Database db,HttpContext ctx) => { var actor=ctx.Actor();if(!actor.Can("Parent"))actor.Require("ContentEditor");return new { drafts=actor.Can("ContentEditor")?await db.Drafts.Where(d => d.FamilyId==actor.FamilyId).OrderByDescending(d => d.CreatedAt).ToListAsync():[],releases=await db.Releases.Where(r => r.FamilyId==ctx.Actor().FamilyId).OrderByDescending(r => r.Number).ToListAsync() }; });
        api.MapPost("/content/fixture",(Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("ContentEditor");var draft=new ContentDraft { FamilyId=a.FamilyId,Title="原创样例 · 三年级第一单元混合运算（20 题）",Payload=Json.Write(Content.Fixture()) };db.Drafts.Add(draft);return Results.Ok(draft); });
        api.MapPost("/content/drafts",(DraftInput input,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("ContentEditor");var d=new ContentDraft { FamilyId=a.FamilyId,Title=input.Title,Payload=Json.Write(input.Catalog) };db.Drafts.Add(d);return Results.Ok(d); });
        api.MapPut("/content/drafts/{id:guid}",async (Guid id,DraftInput input,Database db,HttpContext ctx) => { ctx.Actor().Require("ContentEditor");var d=await Owned<ContentDraft>(db,ctx.Actor(),id);if (d.Status=="Published") throw new ApiError(409,"IMMUTABLE","已发布版本不可修改，请创建新草稿。");d.Payload=Json.Write(input.Catalog);d.Title=input.Title;d.Status="Draft";d.ReviewedBy=null;d.Version++;return Results.Ok(d); });
        api.MapPost("/content/drafts/{id:guid}:review",async (Guid id,Database db,HttpContext ctx) => { ctx.Actor().Require("ContentEditor");var d=await Owned<ContentDraft>(db,ctx.Actor(),id);await CatalogDirectory.ValidateSources(db,ctx.Actor().FamilyId,Json.Read<Catalog>(d.Payload));var errors=Content.Validate(Json.Read<Catalog>(d.Payload));if (errors.Length>0) throw new ApiError(422,"CONTENT_INVALID",string.Join("；",errors));if (d.Status=="Published") throw new ApiError(409,"IMMUTABLE","版本已发布。");d.Status="Approved";d.ReviewedBy=ctx.Actor().Id;return Results.Ok(d); });
        api.MapGet("/content/drafts/{id:guid}/preview",async (Guid id,Database db,HttpContext ctx) => { ctx.Actor().Require("Publisher");var d=await Owned<ContentDraft>(db,ctx.Actor(),id);return new { hash=Content.Hash(d.Payload+":"+d.Version),errors=Content.Validate(Json.Read<Catalog>(d.Payload)),status=d.Status }; });
        api.MapPost("/content/drafts/{id:guid}:publish",async (Guid id,PublishInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Publisher");var d=await Owned<ContentDraft>(db,a,id);
            if (d.Status!="Approved" || d.ReviewedBy==null) throw new ApiError(422,"REVIEW_REQUIRED","内容必须先经过人工审核。");
            if (Content.Hash(d.Payload+":"+d.Version)!=input.PreviewHash) throw new ApiError(412,"PREVIEW_CHANGED","草稿已变化，请重新预览。");
            var errors=Content.Validate(Json.Read<Catalog>(d.Payload));if (errors.Length>0) throw new ApiError(422,"CONTENT_INVALID",string.Join("；",errors));
            var release=new Release { FamilyId=a.FamilyId,Payload=d.Payload,Hash=Content.Hash(d.Payload),Number=(await db.Releases.Where(r => r.FamilyId==a.FamilyId).MaxAsync(r => (int?)r.Number)??0)+1,PublishedBy=a.Id };db.Releases.Add(release);await Publishing.Register(db,release);d.Status="Published";return Results.Ok(release);
        });
        api.MapPost("/students/{id:guid}/content/{releaseId:guid}:bind",async (Guid id,Guid releaseId,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);var r=await Owned<Release>(db,a,releaseId);if (r.Withdrawn) throw new ApiError(422,"WITHDRAWN","该发布版本已撤回。");s.ActiveReleaseId=releaseId;return Results.Ok(s); });
        api.MapPost("/content/releases/{id:guid}:withdraw",async(Guid id,ReasonInput input,Database db,HttpContext ctx)=>{var a=ctx.Actor();a.Require("Publisher");var release=await Owned<Release>(db,a,id);if(string.IsNullOrWhiteSpace(input.Reason))throw new ApiError(422,"REASON_REQUIRED","撤回需要原因。");release.Withdrawn=true;db.Audits.Add(new(){FamilyId=a.FamilyId,ActorId=a.Id,Action="ReleaseWithdrawal",Details=Json.Write(new {releaseId=id,input.Reason})});return Results.Ok(new {release.Id,release.Withdrawn,notice="阻止新会话；已领取会话与历史证据保留。"});});
        api.MapGet("/students/{id:guid}/catalog",async (Guid id,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);return s.ActiveReleaseId==null ? Results.Text("null","application/json") : Results.Ok(Json.Read<Catalog>((await Owned<Release>(db,a,s.ActiveReleaseId.Value)).Payload)); });
        api.MapPost("/students/{id:guid}/plans/{date}:generate",async (Guid id,DateOnly date,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");return Results.Ok(await Planning.Generate(db,await a.Student(db,id),date)); });
        api.MapGet("/students/{id:guid}/plans/{date}",async (Guid id,DateOnly date,Database db,HttpContext ctx) => { ctx.Actor().Require("Parent");await ctx.Actor().Student(db,id);return await PlanView(db,id,date,false); });
        api.MapPost("/plans/{id:guid}:publish",async (Guid id,PublishInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var revision=await Owned<PlanRevision>(db,a,id);var plan=await Owned<Plan>(db,a,revision.PlanId);
            if (revision.InputHash!=input.PreviewHash) throw new ApiError(412,"PREVIEW_CHANGED","计划已变化，请重新确认。");
            if (revision.Warnings!="[]" && !input.ConfirmWarnings) throw new ApiError(422,"WARNINGS_REQUIRE_CONFIRMATION","请先确认超载或内容缺口。");
            if (revision.Status!="Draft") throw new ApiError(409,"ALREADY_PUBLISHED","计划已经发布。");
            plan.ActiveRevisionId=id;plan.Status="Published";revision.Status="Published";
            var ids=await db.Placements.Where(p => p.RevisionId==id).Select(p => p.TaskId).ToArrayAsync();
            foreach (var t in await db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync()) if (t.Status is "Planned" or "Deferred") t.Status="Ready";
            return Results.Ok(revision);
        });
        api.MapGet("/plans/{id:guid}/questions",async(Guid id,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("Parent");var revision=await Owned<PlanRevision>(db,actor,id);var release=await Owned<Release>(db,actor,revision.ReleaseId);
            return Json.Read<Catalog>(release.Payload).Questions.Select(q=>new {q.Id,q.Stem,q.Type,q.Policy});
        });
        api.MapPost("/plans/{id:guid}/tasks",async (Guid id,ManualTaskInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var rev=await Owned<PlanRevision>(db,a,id);var plan=await Owned<Plan>(db,a,rev.PlanId);
            if (rev.Status!="Draft") throw new ApiError(409,"IMMUTABLE","请重新生成草稿再调整。");
            if (input.Minutes<1 || input.Minutes>180 || string.IsNullOrWhiteSpace(input.ResourceRef)) throw new ApiError(422,"INVALID_TASK","任务需要时长和可执行说明。");
            if (input.Type=="Schoolwork" && rev.Reserved>0) throw new ApiError(422,"SCHOOLWORK_DOUBLE_COUNT","请先取消预留作业时间。");
            Question? question=null;
            if(input.QuestionId.HasValue)
            {
                var release=await Owned<Release>(db,a,rev.ReleaseId);
                if(release.Withdrawn)throw new ApiError(422,"CONTENT_WITHDRAWN","内容版本已撤回，不能新增题目任务。");
                question=Json.Read<Catalog>(release.Payload).Questions.SingleOrDefault(q=>q.Id==input.QuestionId)??throw new ApiError(422,"QUESTION_NOT_IN_PLAN_RELEASE","请选择本计划内容版本中的题目。");
            }
            if(!new[] {"Schoolwork","Resource","Practice"}.Contains(input.Type) || input.Type=="Practice" && question==null || string.IsNullOrWhiteSpace(input.Title))throw new ApiError(422,"INVALID_TASK","请选择任务类型并填写标题；练习任务需要正式题目。");
            var task=new StudyTask { FamilyId=a.FamilyId,StudentId=plan.StudentId,ReleaseId=rev.ReleaseId,QuestionId=question?.Id,KCId=question?.Mappings.FirstOrDefault(m=>m.Mode!="None")?.KCId,Title=input.Title,Type=question!=null?"Practice":input.Type,Minutes=input.Minutes,ResourceRef=input.ResourceRef,Mandatory=input.Mandatory,Locked=true,ReasonCode="PARENT_LOCKED",Reason="家长安排的必做任务" };db.Tasks.Add(task);
            db.Placements.Add(new() { FamilyId=a.FamilyId,RevisionId=id,TaskId=task.Id,Sequence=await db.Placements.CountAsync(p => p.RevisionId==id) });rev.InputHash=Content.Hash(rev.InputHash+Json.Write(input));await Planning.RefreshBudget(db,rev);return Results.Ok(task);
        });
        api.MapPost("/plans/{id:guid}:adjust",async (Guid id,AdjustInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var rev=await Owned<PlanRevision>(db,a,id);
            if (rev.Status!="Draft" || string.IsNullOrWhiteSpace(input.Reason)) throw new ApiError(422,"ADJUST_INVALID","只可调整草稿，并需填写原因。");
            var placements=await db.Placements.Where(p => p.RevisionId==id).ToListAsync();
            if (input.TaskIds.Distinct().Count()!=input.TaskIds.Length || input.TaskIds.Any(t => !placements.Any(p => p.TaskId==t))) throw new ApiError(422,"INVALID_TASK_IDS","任务顺序无效。");
            foreach (var p in placements)
            {
                var t=await Owned<StudyTask>(db,a,p.TaskId);
                if (!input.TaskIds.Contains(t.Id)) { if (t.Mandatory || t.Status is "Completed" or "InProgress") throw new ApiError(422,"FIXED_TASK","不能移除已执行或必做任务。");db.Placements.Remove(p); }
                else { p.Sequence=Array.IndexOf(input.TaskIds,t.Id);t.Locked=input.LockedIds.Contains(t.Id); }
            }
            rev.InputHash=Content.Hash(rev.InputHash+Json.Write(input));await Planning.RefreshBudget(db,rev);return Results.Ok(rev);
        });
        api.MapGet("/students/{id:guid}/today",async (Guid id,Database db,HttpContext ctx) => { var s=await ctx.Actor().Student(db,id);return await PlanView(db,id,Today(s),true); });
        api.MapPost("/tasks/{id:guid}:transition",async (Guid id,TransitionInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();var t=await Owned<StudyTask>(db,a,id);var student=await a.Student(db,t.StudentId);var today=Today(student);
            var active=await (from p in db.Plans join place in db.Placements on p.ActiveRevisionId equals (Guid?)place.RevisionId where place.TaskId==id && p.StudentId==t.StudentId && (a.Role!="Child" || p.Date==today) select p).AnyAsync();
            if (!active) throw new ApiError(409,"TASK_NOT_ACTIVE","任务尚未发布或已被调整。");
            var allowed=t.Status switch { "Ready" => new[] { "InProgress","Skipped","Deferred" }, "InProgress" => new[] { "Completed","Deferred","Abandoned" }, _ => Array.Empty<string>() };
            if (!allowed.Contains(input.Status)) throw new ApiError(422,"INVALID_TRANSITION","任务状态不允许此操作。");
            if (t.Mandatory && input.Status=="Deferred" && a.Role=="Child") throw new ApiError(403,"PARENT_REQUIRED","必做任务延期需要家长确认。");
            if (input.ActualMinutes is <0 or >600) throw new ApiError(422,"INVALID_DURATION","学习时长无效。");
            if (input.Status=="Completed" && t.QuestionId!=null)
            {
                var sessions=await db.Sessions.Where(s => s.TaskId==id).Select(s => s.Id).ToArrayAsync();var attempts=await db.Attempts.Where(x => sessions.Contains(x.SessionId)).ToListAsync();
                if (attempts.Count==0 || !await db.Gradings.AnyAsync(g => attempts.Select(x => x.Id).Contains(g.AttemptId) && g.Result!="Pending")) throw new ApiError(422,"SUBMISSION_REQUIRED","请先作答并等待判分。");
            }
            var now=DateTimeOffset.UtcNow;
            if(t.Status=="InProgress" && t.StartedAt!=null)t.TrackedSeconds+=(int)Math.Max(0,(now-t.StartedAt.Value).TotalSeconds);
            t.Status=input.Status;
            if(input.Status=="InProgress")t.StartedAt=now;
            else if(input.Status is "Completed" or "Deferred" or "Abandoned")
            {
                t.StartedAt=null;t.ActualMinutes=a.Role=="Parent" && input.ActualMinutes!=null?input.ActualMinutes:Math.Max(1,(int)Math.Ceiling(t.TrackedSeconds/60m));
                if(input.Status=="Completed")t.CompletedAt=now;
            }
            await db.SaveChangesAsync();
            var plans=await (from p in db.Plans join place in db.Placements on p.ActiveRevisionId equals (Guid?)place.RevisionId where place.TaskId==id select p).ToListAsync();
            foreach(var plan in plans)
            {
                var taskIds=await db.Placements.Where(p=>p.RevisionId==plan.ActiveRevisionId).Select(p=>p.TaskId).ToArrayAsync();var statuses=await db.Tasks.Where(x=>taskIds.Contains(x.Id)).Select(x=>x.Status).ToArrayAsync();
                if(statuses.Length>0 && statuses.All(x=>x=="Completed"))plan.Status="Completed";
                else if(statuses.Length>0 && statuses.All(x=>x is "Completed" or "Skipped" or "Abandoned"))plan.Status="Closed";
                else if(statuses.Any(x=>x=="InProgress"))plan.Status="InProgress";
            }
            return Results.Ok(t);
        });
        api.MapPost("/tasks/{id:guid}/sessions",async (Guid id,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();var task=await Owned<StudyTask>(db,a,id);var student=await a.Student(db,task.StudentId);var today=Today(student);
            if(a.Role=="Child" && !await (from p in db.Plans join place in db.Placements on p.ActiveRevisionId equals (Guid?)place.RevisionId where place.TaskId==id && p.Date==today select p).AnyAsync())throw new ApiError(422,"TASK_NOT_TODAY","任务不在今天的活动计划中。");
            if (task.Status!="InProgress" || task.QuestionId==null) throw new ApiError(422,"SESSION_NOT_ALLOWED","先开始已发布的练习任务。");
            var release=await Owned<Release>(db,a,task.ReleaseId);
            var q=Json.Read<Catalog>(release.Payload).Questions.SingleOrDefault(q => q.Id==task.QuestionId) ?? throw new ApiError(404,"QUESTION_UNPUBLISHED","题目未发布。");
            var existing=await db.Sessions.Where(s => s.TaskId==id).OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync();
            if(release.Withdrawn && existing==null)throw new ApiError(422,"WITHDRAWN","内容已撤回，请联系家长。");
            var session=existing??new LearningSession { FamilyId=a.FamilyId,StudentId=task.StudentId,TaskId=id,ReleaseId=release.Id,QuestionId=q.Id };
            if (existing==null) db.Sessions.Add(session);
            var lastAttempt=await db.Attempts.Where(x=>x.SessionId==session.Id).OrderByDescending(x=>x.Number).FirstOrDefaultAsync();
            var lastGrade=lastAttempt==null?null:await db.Gradings.Where(g=>g.AttemptId==lastAttempt.Id).OrderByDescending(g=>g.Number).FirstOrDefaultAsync();
            var canComplete=await (from attempt in db.Attempts join grade in db.Gradings on attempt.Id equals grade.AttemptId where attempt.SessionId==session.Id && grade.Result!="Pending" select grade.Id).AnyAsync();
            return Results.Ok(new { sessionId=session.Id,q.Stem,q.Type,hintLevel=session.HintLevel,answerShown=session.AnswerShown,releaseId=release.Id,canComplete,lastAttempt=lastAttempt==null?null:new { lastAttempt.Answer,result=lastGrade?.Result } });
        });
        api.MapPost("/sessions/{id:guid}/hints",async (Guid id,HintInput input,Database db,HttpContext ctx) => { var a=ctx.Actor();var s=await Owned<LearningSession>(db,a,id);await a.Student(db,s.StudentId);if (input.Level is <1 or >3) throw new ApiError(422,"INVALID_HINT","提示级别无效。");s.HintLevel=Math.Max(s.HintLevel,input.Level);s.AnswerShown|=input.Level==3;var q=await QuestionFor(db,s);return Results.Ok(new { text=input.Level==3 ? q.Answer+"。"+q.Explanation : q.Hint??"请先在纸上尝试。",level=s.HintLevel }); });
        api.MapPost("/sessions/{id:guid}/attempts",async (Guid id,AnswerInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();var s=await Owned<LearningSession>(db,a,id);await a.Student(db,s.StudentId);var task=await Owned<StudyTask>(db,a,s.TaskId);
            if (task.Status!="InProgress") throw new ApiError(422,"SESSION_CLOSED","该任务不再接受作答。");
            if (input.ClientSubmissionId==Guid.Empty || input.Answer.Length>5000 || string.IsNullOrWhiteSpace(input.Answer)) throw new ApiError(422,"INVALID_ANSWER","请填写答案。");
            var old=await db.Attempts.SingleOrDefaultAsync(x => x.StudentId==s.StudentId && x.ClientSubmissionId==input.ClientSubmissionId);
            if (old!=null) { if (old.SessionId!=id || old.Answer!=input.Answer) throw new ApiError(409,"SUBMISSION_CONFLICT","提交标识对应了其他答案。");return Results.Ok(new { attempt=old,grading=await db.Gradings.Where(g => g.AttemptId==old.Id).OrderByDescending(g => g.Number).FirstAsync(),assessmentStatus="Pending" }); }
            var q=await QuestionFor(db,s);var count=await db.Attempts.CountAsync(x => x.SessionId==id);
            var attempt=new Attempt { FamilyId=a.FamilyId,StudentId=s.StudentId,SessionId=id,ClientSubmissionId=input.ClientSubmissionId,Number=count+1,Answer=input.Answer,HintLevel=s.HintLevel,AnswerShown=s.AnswerShown,AnswerSource=a.Role=="Child"?"Child":"ParentEntered" };
            var result=q.Type is "ShortAnswer" or "MultiStep" ? "Pending" : q.Type=="Numeric" ? decimal.TryParse(input.Answer,NumberStyles.Number,CultureInfo.InvariantCulture,out var value) && value==decimal.Parse(q.Answer,CultureInfo.InvariantCulture) ? "Correct" : "Incorrect" : input.Answer.Trim().Normalize()==q.Answer.Trim().Normalize() ? "Correct" : "Incorrect";
            var grade=new Grading { FamilyId=a.FamilyId,AttemptId=attempt.Id,Number=1,Result=result,Method=result=="Pending"?"ManualRequired":"Rule" };
            db.Attempts.Add(attempt);db.Gradings.Add(grade);db.Outbox.Add(new() { FamilyId=a.FamilyId,StudentId=s.StudentId,AttemptId=attempt.Id });
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/attempts/{attempt.Id}",new { attempt,grading=grade,assessmentStatus="Pending",feedback=result=="Pending"?"已保存，等待家长确认":result=="Correct"?"这次做对了！":"已保存。先看看思路，再试一次。",explanation=q.Explanation });
        });
        api.MapGet("/students/{id:guid}/attempts",async (Guid id,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);var attempts=await db.Attempts.Where(x => x.StudentId==id).OrderByDescending(x => x.Sequence).Take(100).ToListAsync();var ids=attempts.Select(x => x.Id).ToArray();return new { attempts,gradings=await db.Gradings.Where(g => ids.Contains(g.AttemptId)).OrderBy(g => g.Number).ToListAsync() }; });
        api.MapGet("/attempts/{id:guid}/grading-context",async (Guid id,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("Parent");var attempt=await Owned<Attempt>(db,actor,id);var context=await GradingContext(db,attempt);
            var grade=await db.Gradings.Where(g=>g.AttemptId==id).OrderByDescending(g=>g.Number).FirstAsync();
            return Results.Ok(new {attempt,question=context.Question,mappingReleaseId=context.ReleaseId,grade,observations=context.Question.Mappings.Where(m=>m.Mode=="StepObserved").Select(m=>new {step=m.Step,kcId=m.KCId,kcName=context.Catalog.Kcs.Single(k=>k.Id==m.KCId).Name}),notice="未观察到的步骤保留为未知，不推断正确或错误。"});
        });
        api.MapPost("/attempts/{id:guid}/grading-preview",async (Guid id,GradeInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var attempt=await Owned<Attempt>(db,a,id);
            var context=await GradingContext(db,attempt);ValidateGrade(input,context.Question);var grade=await db.Gradings.Where(g => g.AttemptId==id).OrderByDescending(g => g.Number).FirstAsync();var s=await a.Student(db,attempt.StudentId);var count=await db.Attempts.CountAsync(x => x.StudentId==s.Id);
            return Results.Ok(new { before=grade.Result,after=input.Result,replayAttempts=count,previewHash=Content.Hash(Json.Write(new {attemptId=id,gradingId=grade.Id,generationId=s.ActiveGenerationId,count,mappingReleaseId=context.ReleaseId,input.Result,input.Reason,input.Steps})),notice="确认后追加判分修订，整学生证据与日程重放；旧世代保留审计。" });
        });
        api.MapPost("/attempts/{id:guid}/grading-revisions",async (Guid id,GradeInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var attempt=await Owned<Attempt>(db,a,id);
            var context=await GradingContext(db,attempt);ValidateGrade(input,context.Question);var steps=input.Steps??[];
            var oldGrade=await db.Gradings.Where(g => g.AttemptId==id).OrderByDescending(g => g.Number).FirstAsync();
            if (oldGrade.Result!="Pending" || input.PreviewHash!=null)
            {
                var student=await a.Student(db,attempt.StudentId);var count=await db.Attempts.CountAsync(x => x.StudentId==student.Id);
                var expected=Content.Hash(Json.Write(new {attemptId=id,gradingId=oldGrade.Id,generationId=student.ActiveGenerationId,count,mappingReleaseId=context.ReleaseId,input.Result,input.Reason,input.Steps}));
                if (input.PreviewHash!=expected) throw new ApiError(412,"GRADING_PREVIEW_CHANGED","请先预览更正影响；若新作答到达，请重新预览。");
            }
            var grade=new Grading { FamilyId=a.FamilyId,AttemptId=id,Number=await db.Gradings.CountAsync(g => g.AttemptId==id)+1,Result=input.Result,Method="ParentConfirmed",Reason=input.Reason,GradedBy=a.Id,Steps=Json.Write(steps) };db.Gradings.Add(grade);db.Outbox.Add(new() { FamilyId=a.FamilyId,StudentId=attempt.StudentId,AttemptId=id });return Results.Accepted($"/api/v1/students/{attempt.StudentId}/mastery",grade);
        });
        api.MapGet("/students/{id:guid}/mastery",async (Guid id,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);return new { generation=s.ActiveGenerationId,masteries=await db.Masteries.Where(m => m.StudentId==id && m.GenerationId==s.ActiveGenerationId).ToListAsync(),pending=await db.Outbox.CountAsync(o => o.StudentId==id && o.ProcessedAt==null) }; });
        api.MapGet("/students/{id:guid}/mastery/{kcId:guid}",async (Guid id,Guid kcId,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);return new { mastery=await db.Masteries.SingleOrDefaultAsync(m => m.StudentId==id && m.GenerationId==s.ActiveGenerationId && m.KCId==kcId),evidence=await db.Evidence.Where(e => e.StudentId==id && e.GenerationId==s.ActiveGenerationId && e.KCId==kcId).OrderBy(e => e.OccurredAt).ToListAsync() }; });
        api.MapGet("/students/{id:guid}/reviews",async (Guid id,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);return await db.Reviews.Where(r => r.StudentId==id && r.GenerationId==s.ActiveGenerationId).OrderBy(r => r.DueDate).ToListAsync(); });
        api.MapPost("/students/{id:guid}:rebuild",async (Guid id,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);await Assessment.Rebuild(db,s);return Results.Ok(new { generation=s.ActiveGenerationId }); });
        api.MapGet("/students/{id:guid}/weekly-summary",async (Guid id,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);var today=Today(s);var start=today.AddDays(-6);
            var plans=await db.Plans.Where(p => p.StudentId==id && p.Date>=start && p.Date<=today).ToListAsync();var ids=plans.Select(p => p.Id).ToArray();
            var revisions=await db.PlanRevisions.Where(r => ids.Contains(r.PlanId) && r.Status=="Published").OrderBy(r => r.Number).ToListAsync();
            var originalIds=revisions.GroupBy(r => r.PlanId).Select(g => g.First().Id).ToArray();var currentIds=plans.Select(p => p.ActiveRevisionId).ToArray();
            var placements=await db.Placements.Where(p => originalIds.Contains(p.RevisionId) || currentIds.Contains(p.RevisionId)).ToListAsync();var tids=placements.Select(p => p.TaskId).ToArray();var tasks=await db.Tasks.Where(t => tids.Contains(t.Id)).ToListAsync();
            var original=placements.Where(p => originalIds.Contains(p.RevisionId)).Select(p => p.TaskId).Distinct().ToArray();var current=placements.Where(p => currentIds.Contains(p.RevisionId)).Select(p => p.TaskId).Distinct().ToArray();
            return new { start,end=today,original=new { total=original.Length,completed=tasks.Count(t => original.Contains(t.Id) && t.Status=="Completed") },adjusted=new { total=current.Length,completed=tasks.Count(t => current.Contains(t.Id) && t.Status=="Completed") },actualMinutes=tasks.Sum(t => t.ActualMinutes??0),dueReviews=await db.Reviews.CountAsync(r => r.StudentId==id && r.GenerationId==s.ActiveGenerationId && r.Status=="Pending" && r.DueDate<=today),pending=await db.Outbox.CountAsync(o => o.StudentId==id && o.ProcessedAt==null) };
        });
        api.MapGet("/audit",async (Database db,HttpContext ctx) => { ctx.Actor().Require("Parent");return await db.Audits.Where(a => a.FamilyId==ctx.Actor().FamilyId).OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync(); });
        api.MapGet("/jobs",async (Database db,HttpContext ctx)=>{ctx.Actor().Require("Parent");return await db.Outbox.Where(j=>j.FamilyId==ctx.Actor().FamilyId && j.ProcessedAt==null).OrderBy(j=>j.CreatedAt).Take(100).ToListAsync();});
        api.MapPost("/jobs/{id:guid}:retry",async (Guid id,Database db,HttpContext ctx)=>{ctx.Actor().Require("Parent");var job=await Owned<Outbox>(db,ctx.Actor(),id);if(job.ProcessedAt!=null)throw new ApiError(409,"JOB_COMPLETED","结果已经处理，不需要再次重试。");job.Retries=0;job.NextAttemptAt=null;job.Error=null;return Results.Accepted("/api/v1/jobs",job);});
        ProgressCorrections.Map(api);
        Builder.Map(api);
        Privacy.Map(api);
        Files.Map(api);
        Corrections.Map(api);
        Deferral.Map(api);
    }
    static void ValidateStudent(StudentInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length>50 || input.Grade is <1 or >12 || input.DailyMinutes is <0 or >600) throw new ApiError(422,"INVALID_STUDENT","学生姓名、年级或学习预算无效。");
        try { TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone); } catch (TimeZoneNotFoundException) { throw new ApiError(422,"INVALID_TIMEZONE","时区无效。"); }
    }
    static async Task<object> PlanView(Database db,Guid student,DateOnly date,bool child)
    {
        var plan=await db.Plans.SingleOrDefaultAsync(p => p.StudentId==student && p.Date==date);
        if (plan==null) return new { plan=(object?)null,tasks=Array.Empty<object>() };
        var revision=child ? await db.PlanRevisions.SingleOrDefaultAsync(r => r.Id==plan.ActiveRevisionId) : await db.PlanRevisions.Where(r => r.PlanId==plan.Id).OrderByDescending(r => r.Number).FirstOrDefaultAsync();
        if (revision==null) return new { plan=(object?)null,tasks=Array.Empty<object>() };
        var placements=await db.Placements.Where(p => p.RevisionId==revision.Id).OrderBy(p => p.Sequence).ToListAsync();var ids=placements.Select(p => p.TaskId).ToArray();var tasks=await db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync();
        return new { plan,revision,tasks=placements.Select(p => tasks.Single(t => t.Id==p.TaskId)).ToArray() };
    }
}
