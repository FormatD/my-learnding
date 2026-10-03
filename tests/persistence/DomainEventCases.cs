using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
public static class DomainEventCases
{
    static void Check(bool yes,string reason){if(!yes)throw new Exception(reason);}
    public static async Task Run(Database db)
    {
        await db.GetService<IMigrator>().MigrateAsync("20261003003523_ProjectionConsumerReceipts");
        var f=new Family();var catalog=Content.Fixture();var release=new Release{FamilyId=f.Id,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog)),Number=1};var s=new Student{FamilyId=f.Id,ActiveReleaseId=release.Id};var q=catalog.Questions[0];var t=new StudyTask{FamilyId=f.Id,StudentId=s.Id,ReleaseId=release.Id,QuestionId=q.Id};var session=new LearningSession{FamilyId=f.Id,StudentId=s.Id,TaskId=t.Id,ReleaseId=release.Id,QuestionId=q.Id};var attempt=new Attempt{FamilyId=f.Id,StudentId=s.Id,SessionId=session.Id,Number=1,ClientSubmissionId=Guid.NewGuid(),Answer="999"};var grade=new Grading{FamilyId=f.Id,AttemptId=attempt.Id,Number=1,Result="Incorrect"};
        // Insert with the old schema, before nullable event links exist.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Families\" (\"Id\",\"Name\",\"Version\") VALUES ({f.Id},{f.Name},{f.Version})");
        // Remaining old rows use raw snapshots via the prior model's still-compatible common columns.
        await db.Database.MigrateAsync();db.AddRange(release,s,t,session,attempt,grade);var legacy=new Outbox{FamilyId=f.Id,StudentId=s.Id,AttemptId=attempt.Id};db.Add(legacy);await db.SaveChangesAsync();await Publishing.Register(db,release);await db.SaveChangesAsync();
        var old=ProjectionJobs.Snapshot(legacy);Check(!old.Contains("domainEventId") && legacy.DomainEventId==null,"legacy descriptor changed");
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            var rolled=await DomainEvents.Append(db,f.Id,s.Id,attempt.Id,"Attempt","AttemptSubmitted",new{attemptId=attempt.Id},attempt.Id);await db.SaveChangesAsync();await tx.RollbackAsync();db.ChangeTracker.Clear();Check(!await db.Set<DomainEvent>().AnyAsync() && await db.Outbox.CountAsync()==1,"rollback left journal or dispatch");
        }
        DomainEvent submitted;
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            submitted=await DomainEvents.Append(db,f.Id,s.Id,attempt.Id,"Attempt","AttemptSubmitted",new{attemptId=attempt.Id,gradingId=grade.Id},attempt.Id);
            var confirmed=await DomainEvents.Append(db,f.Id,s.Id,grade.Id,"Grading","GradingConfirmed",new{attemptId=attempt.Id,gradingId=grade.Id,grade.Result});
            Check(submitted.EventSequence<confirmed.EventSequence,"append sequence unordered");await db.SaveChangesAsync();await tx.CommitAsync();
        }
        db.ChangeTracker.Clear();var row=await db.Outbox.SingleAsync(o=>o.Id==submitted.Id);await DomainEvents.Validate(db,row,default);
        row.DomainEventId=null;try{await DomainEvents.Validate(db,row,default);throw new Exception("modern event downgraded to legacy");}catch(ApiError e){Check(e.Code=="DOMAIN_EVENT_INVALID","missing modern link did not reject");}row.DomainEventId=submitted.Id;
        var bytes=Json.Write(await db.Set<DomainEvent>().OrderBy(e=>e.EventSequence).ToArrayAsync());
        Check(await ProjectionWorker.Consume(db,row),"typed projection not consumed");db.ChangeTracker.Clear();var receipts=await db.Set<ConsumerReceipt>().ToArrayAsync();Check(receipts.Length==2 && receipts.Single(r=>r.EventId==submitted.Id).DomainEventId==submitted.Id && receipts.Single(r=>r.EventId==legacy.Id).DomainEventId==null,"typed/legacy receipt provenance lost");
        Check(await db.Set<DomainEvent>().CountAsync(e=>e.EventType=="AssessmentApplied")==1,"actual assessment event missing");Check(!await ProjectionWorker.Consume(db,await db.Outbox.SingleAsync(o=>o.Id==submitted.Id)) && await db.Set<DomainEvent>().CountAsync()==3,"duplicate consumption generated journal facts");
        Console.WriteLine("PASS 服务端事件顺序、事务回滚无残留、旧输入格式不变、新旧事件独立回执及幂等评估事实");
        DomainEvent invalid;
        await using(var tx=await db.Database.BeginTransactionAsync()){invalid=await DomainEvents.Append(db,f.Id,s.Id,attempt.Id,"Attempt","AttemptSubmitted",new{attemptId=Guid.NewGuid(),gradingId=grade.Id},attempt.Id);await db.SaveChangesAsync();await tx.CommitAsync();}
        var beforeGenerations=await db.Generations.CountAsync();var beforeReceipts=await db.Set<ConsumerReceipt>().CountAsync();
        try{await ProjectionWorker.Consume(db,await db.Outbox.SingleAsync(o=>o.Id==invalid.Id));throw new Exception("invalid typed anchor accepted");}catch(ApiError e){Check(e.Code=="DOMAIN_EVENT_INVALID","incorrect typed validation failure");}
        db.ChangeTracker.Clear();Check(await db.Generations.CountAsync()==beforeGenerations && await db.Set<ConsumerReceipt>().CountAsync()==beforeReceipts && await db.Set<DomainEvent>().CountAsync(e=>e.EventType=="AssessmentApplied")==1,"invalid event left a partial projection");Console.WriteLine("PASS 新事件负载锚点错误明确拒绝，不降级旧事件、不产生半个世代或回执");
        try{await db.Set<DomainEvent>().Where(e=>e.Id==submitted.Id).ExecuteUpdateAsync(u=>u.SetProperty(e=>e.EventType,"ProgressChanged"));throw new Exception("journal update accepted");}catch(Npgsql.PostgresException ex){Check(ex.SqlState=="23514","wrong immutable constraint");}
        Console.WriteLine("PASS 数据库拒绝事件记录修改，保留删除隐私权");
        await db.Students.Where(x=>x.Id==s.Id).ExecuteDeleteAsync();Check(!await db.Set<DomainEvent>().AnyAsync() && !await db.Set<ConsumerReceipt>().AnyAsync(),"student deletion retained event payload");Console.WriteLine("PASS 学生删除清除事件负载和关联消费记录");
    }
}
