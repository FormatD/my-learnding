using Learning;
using Microsoft.EntityFrameworkCore;
public static class ReviewRuleUpgradeCases
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task Run(Database db,string mode)
    {
        var proof=Environment.GetEnvironmentVariable("REVIEW_UPGRADE_PROOF")??throw new Exception("Isolated proof path required");
        if(mode=="review-rule-seed")
        {
            Check(Assessment.ReviewRuleVersion=="review/1","Seed must execute the actual historical implementation");
            await db.Database.MigrateAsync();var family=new Family();var original=Content.Fixture();var skill=original.Kcs[0];var other=original.Kcs.First(k=>k.Id!=skill.Id);
            var questions=Enumerable.Range(0,11).Select(n=>original.Questions[0] with{Id=Guid.NewGuid(),RevisionId=Guid.NewGuid(),VariantGroupId=null,Policy="SingleKC",Difficulty="Medium",Mappings=[new(n==10?other.Id:skill.Id)]}).ToArray();
            var catalog=original with{Questions=questions};var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var student=new Student{FamilyId=family.Id,ActiveReleaseId=release.Id,TimeZone="UTC"};db.AddRange(family,release,student);await db.SaveChangesAsync();await Publishing.Register(db,release);await db.SaveChangesAsync();
            var time=new DateTimeOffset(2026,1,1,8,0,0,TimeSpan.Zero);
            for(var n=0;n<questions.Length;n++)
            {
                var q=questions[n];var task=new StudyTask{FamilyId=family.Id,StudentId=student.Id,ReleaseId=release.Id,QuestionId=q.Id,Type=n==10?"Review":"Practice",ReviewTargetId=n==10?skill.Id:null};var session=new LearningSession{FamilyId=family.Id,StudentId=student.Id,ReleaseId=release.Id,QuestionId=q.Id,TaskId=task.Id};var attempt=new Attempt{FamilyId=family.Id,StudentId=student.Id,SessionId=session.Id,Number=1,ClientSubmissionId=Guid.NewGuid(),CreatedAt=time.AddDays(n==10?8:0).AddMinutes(n)};db.AddRange(task,session,attempt,new Grading{FamilyId=family.Id,AttemptId=attempt.Id,Number=1,Result="Correct"});await db.SaveChangesAsync();
            }
            await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);await Assessment.Rebuild(db,student);await tx.CommitAsync();}
            Check((await db.Reviews.SingleAsync(r=>r.TargetType=="KC" && r.TargetId==skill.Id)).Stage=="LowFrequency","Historical implementation did not reproduce the target mismatch defect");
            await File.WriteAllTextAsync(proof,Json.Write(new UpgradeProof(student.Id,student.ActiveGenerationId!.Value,skill.Id,await Bytes(db,student.ActiveGenerationId.Value))));
            Console.WriteLine("PASS 历史提交真实执行 review/1：不测量原目标的复习被错误推进，保存原始状态/证据/复习字节作为升级对照");return;
        }
        Check(Assessment.ReviewRuleVersion=="review/3","Upgrade must execute the new implementation");var saved=Json.Read<UpgradeProof>(await File.ReadAllTextAsync(proof));await db.Database.MigrateAsync();var learner=await db.Students.SingleAsync(s=>s.Id==saved.Student);
        await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(learner.FamilyId);await Assessment.Rebuild(db,learner,online:true);await tx.CommitAsync();}db.ChangeTracker.Clear();learner=await db.Students.SingleAsync(s=>s.Id==saved.Student);var generation=await db.Generations.SingleAsync(g=>g.Id==learner.ActiveGenerationId);
        Check(generation.Id!=saved.Generation && generation.CalculationMode=="FullStreamChangedPrefix" && generation.ProcessedInputCount==11 && generation.IncrementalBaseGenerationId==null,"Rule upgrade resumed a historical rule snapshot");
        var review=await db.Reviews.SingleAsync(r=>r.GenerationId==generation.Id && r.TargetType=="KC" && r.TargetId==saved.Target);Check(review.Stage=="R1" && review.DueDate==new DateOnly(2026,1,8),"Actual upgrade retained the unrelated target promotion");
        var report=await ReviewReporting.Read(db,learner.Id,learner.TimeZone,new(2026,1,1),new(2026,1,10),CancellationToken.None);Check(report.GradedEncounters==1 && report.IndependentPasses==0 && report.Items.Single().Reason=="TARGET_CHANGED","Actual report failed to expose the target mismatch to the parent");
        Check(await Bytes(db,saved.Generation)==saved.Bytes,"Upgrade rewrote historical checkpoint, evidence or reviews");Check(Json.Read<IncrementalAssessmentSnapshot>((await db.Set<AssessmentCheckpoint>().SingleAsync(c=>c.GenerationId==generation.Id)).Payload).ReviewRule=="review/3","New checkpoint failed to record the actual rule");
        await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(learner.FamilyId);await Assessment.Rebuild(db,learner,online:true);await tx.CommitAsync();}Check(await db.Generations.CountAsync()==2 && await db.Set<AssessmentCheckpoint>().CountAsync()==2,"Same upgraded input fabricated another generation");
        Console.WriteLine("PASS 新规则从原始11条输入完整重算，原目标保持到期；旧状态/证据/复习逐字节不变，新状态记录 review/3，同输入复用");
        await db.Families.Where(f=>f.Id==learner.FamilyId).ExecuteDeleteAsync();Check(!await db.Set<AssessmentCheckpoint>().AnyAsync() && !await db.Reviews.AnyAsync(),"Family deletion retained review history");Console.WriteLine("PASS 家庭删除清理升级前后全部复习与检查点");
    }
    static async Task<string> Bytes(Database db,Guid generation)
    {
        var data=System.Text.Json.Nodes.JsonNode.Parse(Json.Write(new{checkpoints=await db.Set<AssessmentCheckpoint>().AsNoTracking().Where(c=>c.GenerationId==generation).OrderBy(c=>c.Id).ToArrayAsync(),evidence=await db.Evidence.AsNoTracking().Where(c=>c.GenerationId==generation).OrderBy(c=>c.Id).ToArrayAsync(),reviews=await db.Reviews.AsNoTracking().Where(c=>c.GenerationId==generation).OrderBy(c=>c.Id).ToArrayAsync()}))!;
        // Old assemblies have no storage columns. Ignore only newly added NULLs, never recorded facts.
        foreach(var checkpoint in data["checkpoints"]!.AsArray())foreach(var field in new[]{"storageVersion","baseCheckpointId","deltaDepth","statePayloadHash"})
            if(checkpoint!.AsObject().TryGetPropertyValue(field,out var value) && value==null)checkpoint.AsObject().Remove(field);
        return Json.Write(data);
    }
    record UpgradeProof(Guid Student,Guid Generation,Guid Target,string Bytes);
}
