using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

public static class AssessmentSourceLoadingCases
{
    sealed class LoadedRows:IMaterializationInterceptor
    {
        public readonly Dictionary<Type,HashSet<Guid>> Ids=[];
        public object InitializedInstance(MaterializationInterceptionData data,object entity)
        {
            if(entity is Row row){if(!Ids.TryGetValue(entity.GetType(),out var ids))Ids[entity.GetType()]=ids=[];ids.Add(row.Id);}return entity;
        }
        public void Require<T>(params Guid[] expected) where T:Row
        {
            var actual=Ids.GetValueOrDefault(typeof(T))??[];
            if(!actual.SetEquals(expected))throw new Exception($"Unexpected materialized {typeof(T).Name}: {actual.Count}, expected {expected.Length}");
        }
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static async Task Run(Database db)
    {
        await db.Database.MigrateAsync();var f=new Family();var otherFamily=new Family();var actor=new Account{FamilyId=f.Id,UserName="source-scope-"+Guid.NewGuid()};var catalog=Content.Fixture();var question=catalog.Questions[0];
        var original=new Release{FamilyId=f.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};
        var changedQuestion=question with{RevisionId=Guid.NewGuid(),Mappings=[question.Mappings[0] with{Share=.5m}]};
        var changedCatalog=catalog with{Questions=catalog.Questions.Select(q=>q.Id==question.Id?changedQuestion:q).ToArray()};
        var changed=new Release{FamilyId=f.Id,Number=2,Payload=Json.Write(changedCatalog),Hash=Content.Hash(Json.Write(changedCatalog))};
        var student=new Student{FamilyId=f.Id,ActiveReleaseId=original.Id};var empty=new Student{FamilyId=f.Id,ActiveReleaseId=original.Id};
        db.AddRange(f,otherFamily,actor,original,changed,student,empty);await db.SaveChangesAsync();await Publishing.Register(db,original);await db.SaveChangesAsync();await Publishing.Register(db,changed);await db.SaveChangesAsync();
        var tasks=new List<StudyTask>();var sessions=new List<LearningSession>();var attempts=new List<Attempt>();var grades=new List<Grading>();var time=new DateTimeOffset(2026,10,1,4,0,0,TimeSpan.Zero);
        for(var i=0;i<2;i++)
        {
            var task=new StudyTask{FamilyId=f.Id,StudentId=student.Id,ReleaseId=original.Id,QuestionId=question.Id};var session=new LearningSession{FamilyId=f.Id,StudentId=student.Id,TaskId=task.Id,ReleaseId=original.Id,QuestionId=question.Id,QuestionRevisionId=question.RevisionId};
            var attempt=new Attempt{FamilyId=f.Id,StudentId=student.Id,SessionId=session.Id,QuestionRevisionId=question.RevisionId,Number=1,ClientSubmissionId=Guid.NewGuid(),Answer=question.Answer,CreatedAt=time.AddMinutes(i)};
            var grade=new Grading{FamilyId=f.Id,AttemptId=attempt.Id,Number=1,Result="Correct"};db.AddRange(task,session,attempt,grade);await db.SaveChangesAsync();tasks.Add(task);sessions.Add(session);attempts.Add(attempt);grades.Add(grade);
        }
        var mappingBatch=new CorrectionBatch{FamilyId=f.Id,StudentId=student.Id,ReleaseId=changed.Id,Cause="Mapping",AffectedAttemptIds=Json.Write(new[]{attempts[0].Id}),ConfirmedBy=actor.Id,CreatedAt=time.AddHours(1)};
        var mapping=new CorrectionItem{FamilyId=f.Id,BatchId=mappingBatch.Id,AttemptId=attempts[0].Id,MappingReleaseId=changed.Id,QuestionRevisionId=changedQuestion.RevisionId};
        var gradingBatch=new CorrectionBatch{FamilyId=f.Id,StudentId=student.Id,ReleaseId=original.Id,Cause="Grading",AffectedAttemptIds=Json.Write(new[]{attempts[0].Id}),ConfirmedBy=actor.Id,CreatedAt=time.AddHours(2)};
        var latest=new Grading{FamilyId=f.Id,AttemptId=attempts[0].Id,Number=2,Result="Incorrect",CorrectionBatchId=gradingBatch.Id,GradedBy=actor.Id};
        var resource=new StudyTask{FamilyId=f.Id,StudentId=student.Id,ReleaseId=original.Id,Type="Resource",KCId=question.Mappings[0].KCId,CompletedAt=time.AddMinutes(-1)};
        db.AddRange(mappingBatch,mapping,gradingBatch,latest,resource);await db.SaveChangesAsync();
        // Unused sessions/tasks and batches of this same learner must not be materialized either.
        var unusedTask=new StudyTask{FamilyId=f.Id,StudentId=student.Id,ReleaseId=original.Id,QuestionId=question.Id};var unusedSession=new LearningSession{FamilyId=f.Id,StudentId=student.Id,TaskId=unusedTask.Id,ReleaseId=original.Id,QuestionId=question.Id};
        db.AddRange(unusedTask,unusedSession,new StudyTask{FamilyId=f.Id,StudentId=student.Id,ReleaseId=original.Id,Type="Resource",KCId=question.Mappings[0].KCId},new StudyTask{FamilyId=f.Id,StudentId=student.Id,ReleaseId=original.Id,Type="Resource",CompletedAt=time},new CorrectionBatch{FamilyId=f.Id,StudentId=student.Id,ReleaseId=original.Id,ConfirmedBy=actor.Id});
        // Real unrelated rows cover another child in this family and a separate family.
        for(var i=0;i<12;i++)
        {
            var familyId=i<6?f.Id:otherFamily.Id;var release=new Release{FamilyId=familyId,Number=i+3,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var child=new Student{FamilyId=familyId,ActiveReleaseId=release.Id};
            var task=new StudyTask{FamilyId=familyId,StudentId=child.Id,ReleaseId=release.Id,QuestionId=question.Id};var session=new LearningSession{FamilyId=familyId,StudentId=child.Id,TaskId=task.Id,ReleaseId=release.Id,QuestionId=question.Id};var attempt=new Attempt{FamilyId=familyId,StudentId=child.Id,SessionId=session.Id,ClientSubmissionId=Guid.NewGuid(),Number=1};
            db.AddRange(release,child,task,session,attempt,new Grading{FamilyId=familyId,AttemptId=attempt.Id,Number=1,Result="Correct"});
        }
        await db.SaveChangesAsync();
        var teaching=new[]{new TeachingAnchor(resource.KCId!.Value,resource.CompletedAt!.Value)};
        var expected=new[]{new AssessmentInput(attempts[0],sessions[0],changedQuestion,latest,changedCatalog.Kcs,tasks[0],changed.Id,gradingBatch.Id,null,gradingBatch.Id,mappingBatch.Id),new AssessmentInput(attempts[1],sessions[1],question,grades[1],catalog.Kcs,tasks[1],original.Id)};
        async Task<(AssessmentInput[] Inputs,TeachingAnchor[] Teaching,LoadedRows Rows)> Read(Student target,ISet<Guid>? excluded=null)
        {
            var rows=new LoadedRows();await using var reader=new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(rows).Options);
            await using var tx=await reader.Database.BeginTransactionAsync();await reader.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");var result=await Assessment.LoadInputs(reader,target,excludedCorrectionBatches:excluded);
            Check(!reader.ChangeTracker.HasChanges(),"source read altered entities");await tx.CommitAsync();return(result.Inputs,result.Teaching,rows);
        }
        var actual=await Read(student);Check(Json.Write(actual.Inputs)==Json.Write(expected) && Json.Write(actual.Teaching)==Json.Write(teaching),"restricted source load changed fixed input bytes/latest grading/mapping cause/teaching");
        actual.Rows.Require<Attempt>(attempts.Select(a=>a.Id).ToArray());actual.Rows.Require<LearningSession>(sessions.Select(s=>s.Id).ToArray());actual.Rows.Require<StudyTask>(tasks.Select(t=>t.Id).Append(resource.Id).ToArray());actual.Rows.Require<Grading>(grades.Select(g=>g.Id).Append(latest.Id).ToArray());actual.Rows.Require<Release>(original.Id,changed.Id);actual.Rows.Require<CorrectionBatch>(mappingBatch.Id,gradingBatch.Id);actual.Rows.Require<CorrectionItem>(mapping.Id);
        Console.WriteLine("PASS 实际只读数据库加载仅物化所需作答/会话/任务/评分/发布/更正，排除同家庭其他孩子、另一家庭及本学生未引用行；完整固定输入与教学字节符合独立预期");
        var excluded=await Read(student,new HashSet<Guid>{mappingBatch.Id,gradingBatch.Id});var originalInputs=new[]{new AssessmentInput(attempts[0],sessions[0],question,grades[0],catalog.Kcs,tasks[0],original.Id),expected[1]};
        Check(Json.Write(excluded.Inputs)==Json.Write(originalInputs) && Json.Write(excluded.Teaching)==Json.Write(teaching),"excluded correction comparison changed original input");excluded.Rows.Require<Release>(original.Id);excluded.Rows.Require<CorrectionBatch>();
        Console.WriteLine("PASS 排除指定更正时恢复实际原评分/题目及原来源，不加载被排除映射版本/更正批次，教学锚点保持");
        var absent=await Read(empty);Check(absent.Inputs.Length==0 && absent.Teaching.Length==0,"empty learner inherited other sources");absent.Rows.Require<Attempt>();absent.Rows.Require<LearningSession>();absent.Rows.Require<StudyTask>();absent.Rows.Require<Grading>();absent.Rows.Require<Release>();absent.Rows.Require<CorrectionBatch>();
        Console.WriteLine("PASS 空学生读取不物化家庭其他学生的来源，保持空输入/教学且没有业务写入");
        var teachingOnly=new StudyTask{FamilyId=f.Id,StudentId=empty.Id,ReleaseId=original.Id,Type="Resource",KCId=question.Mappings[0].KCId,CompletedAt=time};db.Add(teachingOnly);await db.SaveChangesAsync();
        var anchored=await Read(empty);Check(anchored.Inputs.Length==0 && Json.Write(anchored.Teaching)==Json.Write(new[]{new TeachingAnchor(teachingOnly.KCId!.Value,time)}),"learner without attempts lost completed resource anchor");anchored.Rows.Require<StudyTask>(teachingOnly.Id);anchored.Rows.Require<Release>();anchored.Rows.Require<Grading>();
        Console.WriteLine("PASS 无作答学生仍保留已完成资源的教学锚点，无需读取未引用的内容版本或评分");
    }
}
