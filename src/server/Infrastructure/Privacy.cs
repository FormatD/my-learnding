using Microsoft.EntityFrameworkCore;

namespace Learning;
public record DeleteInput(string PreviewHash,string Password,string Confirm);
public static class Privacy
{
    public static void Map(RouteGroupBuilder api)
    {
        FamilyData.Map(api);
        api.MapGet("/students/{id:guid}/export",async (Guid id,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);
            var attempts=await db.Attempts.Where(x => x.StudentId==id).OrderBy(x => x.Sequence).ToListAsync();var aids=attempts.Select(x => x.Id).ToArray();
            var plans=await db.Plans.Where(x => x.StudentId==id).ToListAsync();var pids=plans.Select(x => x.Id).ToArray();var revisions=await db.PlanRevisions.Where(x => pids.Contains(x.PlanId)).ToListAsync();var rids=revisions.Select(x => x.Id).ToArray();
            var paperWrongs=await db.Set<PaperWrong>().Where(x=>x.StudentId==id).ToListAsync();
            var fileIds=paperWrongs.Where(x=>x.FileId!=null).Select(x=>x.FileId!.Value).Distinct().ToArray();
            var corrections=await db.Set<CorrectionBatch>().Where(x=>x.StudentId==id).ToListAsync();var correctionIds=corrections.Select(x=>x.Id).ToArray();
            return Results.File(System.Text.Encoding.UTF8.GetBytes(Json.Write(new { format="learning-export/2",exportedAt=DateTimeOffset.UtcNow,student=s,parentBurden=await db.Set<ParentBurdenRecord>().Where(x=>x.StudentId==id).ToListAsync(),knowledgeChanges=await KnowledgeChanges.PublishedHistory(db,a.FamilyId),contentProvenance=await Provenance.Citations(db,a.FamilyId,publishedOnly:true),progressChanges=await db.Set<ProgressChange>().Where(x=>x.StudentId==id).ToListAsync(),paperWrongs,files=await db.Set<PrivateFile>().Where(x=>x.FamilyId==a.FamilyId && fileIds.Contains(x.Id)).ToListAsync(),corrections,correctionItems=await db.Set<CorrectionItem>().Where(x=>correctionIds.Contains(x.BatchId)).ToListAsync(),availability=await db.Availabilities.Where(x => x.StudentId==id).ToListAsync(),progress=await db.Progresses.Where(x => x.StudentId==id).ToListAsync(),goalChanges=await db.Set<GoalChange>().Where(x=>x.StudentId==id).ToListAsync(),goals=await db.Goals.Where(x => x.StudentId==id).ToListAsync(),plans,revisions,placements=await db.Placements.Where(x => rids.Contains(x.RevisionId)).ToListAsync(),tasks=await db.Tasks.Where(x => x.StudentId==id).ToListAsync(),sessions=await db.Sessions.Where(x => x.StudentId==id).ToListAsync(),attempts,gradings=await db.Gradings.Where(x => aids.Contains(x.AttemptId)).ToListAsync(),generations=await db.Generations.Where(x => x.StudentId==id).ToListAsync(),evidence=await db.Evidence.Where(x => x.StudentId==id).ToListAsync(),mastery=await db.Masteries.Where(x => x.StudentId==id).ToListAsync(),reviews=await db.Reviews.Where(x => x.StudentId==id).ToListAsync(),releases=await db.Releases.Where(x => x.FamilyId==a.FamilyId).ToListAsync() })),"application/json",$"learning-{id}.json");
        });
        api.MapGet("/students/{id:guid}/delete-preview",async (Guid id,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);var attempts=await db.Attempts.CountAsync(x => x.StudentId==id);var family=await db.Families.SingleAsync(f => f.Id==a.FamilyId);
            return new { student=s.Name,attempts,previewHash=Content.Hash($"{id}:{family.Version}:{attempts}"),requiredConfirm="永久删除学生" };
        });
        api.MapPost("/students/{id:guid}:delete",async (Guid id,DeleteInput input,Database db,HttpContext ctx,IConfiguration config) =>
        {
            var a=ctx.Actor();a.Require("Parent");var s=await a.Student(db,id);var family=await db.Families.SingleAsync(f => f.Id==a.FamilyId);var count=await db.Attempts.CountAsync(x => x.StudentId==id);
            if (input.PreviewHash!=Content.Hash($"{id}:{family.Version}:{count}")) throw new ApiError(412,"PREVIEW_CHANGED","删除范围已变化，请重新预览。");
            var account=await db.Accounts.SingleAsync(x => x.Id==a.AccountId);
            if (input.Confirm!="永久删除学生" || !Security.Check(input.Password,account.PasswordHash)) throw new ApiError(422,"DELETE_CONFIRMATION_REQUIRED","需要家长密码和明确删除确认。");
            db.AuthSessions.RemoveRange(await db.AuthSessions.Where(x => x.StudentId==id).ToListAsync());
            // Cached command responses may contain answers or the student's name.
            db.Commands.RemoveRange(await db.Commands.Where(x => x.FamilyId==a.FamilyId).ToListAsync());
            var fileIds=await db.Set<PaperWrong>().Where(p => p.StudentId==id && p.FileId!=null).Select(p => p.FileId).ToArrayAsync();
            var files=await db.Set<PrivateFile>().Where(f => fileIds.Contains(f.Id)).ToListAsync();
            foreach (var f in files)
                if (!await db.Set<PaperWrong>().AnyAsync(p => p.FileId==f.Id && p.StudentId!=id) && !await db.Sources.AnyAsync(source => source.FamilyId==a.FamilyId && source.Hash==f.Hash)) db.Remove(f);
            // Retain only opaque identifiers outside database backups to prevent restoration resurrection.
            var path=config["DeletionLedger"]??Path.Combine(appData(),"deleted-students.txt");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.AppendAllTextAsync(path,$"{a.FamilyId},{id}\n");
            db.Students.Remove(s);return Results.Ok(new { deleted=true,studentId=id,backupPolicy="恢复备份必须重新应用独立删除清单" });
        });
    }
    static string appData()=>Path.Combine(Directory.GetCurrentDirectory(),"../../.local");
}
