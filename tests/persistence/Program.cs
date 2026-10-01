using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

var connection=Environment.GetEnvironmentVariable("PERSISTENCE_TEST_CONNECTION")??throw new Exception("Supply isolated test database connection");
if(!connection.Contains("Database=learning_fault_",StringComparison.Ordinal))throw new Exception("Only disposable learning_fault_ database allowed");
Database Open(bool crash=false){var options=new DbContextOptionsBuilder<Database>().UseNpgsql(connection);if(crash)options.AddInterceptors(new CrashBeforeCommit());return new(options.Options);}
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
await using var db=Open();
if(args[0]=="seed")
{
    await db.Database.MigrateAsync();var f=new Family();var c=Content.Fixture();var q=c.Questions[0];var r=new Release {FamilyId=f.Id,Number=1,Payload=Json.Write(c),Hash=Content.Hash(Json.Write(c))};var s=new Student {FamilyId=f.Id,ActiveReleaseId=r.Id,Name="Fault acceptance"};
    var t=new StudyTask {FamilyId=f.Id,StudentId=s.Id,ReleaseId=r.Id,QuestionId=q.Id,Status="InProgress"};var session=new LearningSession {FamilyId=f.Id,StudentId=s.Id,TaskId=t.Id,ReleaseId=r.Id,QuestionId=q.Id};var a=new Attempt {FamilyId=f.Id,StudentId=s.Id,SessionId=session.Id,ClientSubmissionId=Guid.NewGuid(),Number=1,Answer="999"};var g=new Grading {FamilyId=f.Id,AttemptId=a.Id,Number=1,Result="Incorrect"};var job=new Outbox {FamilyId=f.Id,StudentId=s.Id,AttemptId=a.Id};
    db.AddRange(f,r,s,t,session,a,g,job);await db.SaveChangesAsync();Console.WriteLine("SEEDED");return;
}
if(args[0]=="crash")
{
    await using var crashing=Open(true);var pending=await crashing.Outbox.SingleAsync();await ProjectionWorker.Consume(crashing,pending);throw new Exception("Should have been terminated before commit");
}
var student=await db.Students.SingleAsync();
Assert(student.ActiveGenerationId==null && await db.Generations.CountAsync()==0 && await db.Evidence.CountAsync()==0 && (await db.Outbox.SingleAsync()).ProcessedAt==null,"crashed transaction leaked projection or receipt");
Console.WriteLine("PASS AT12 证据写入后进程终止：活动绑定、证据和处理回执全部回滚");
await using var first=Open();await using var second=Open();
// Both consumers have actually selected the same unprocessed row before either takes the family lock.
var p1=await first.Outbox.SingleAsync();var p2=await second.Outbox.SingleAsync();
var results=await Task.WhenAll(ProjectionWorker.Consume(first,p1),ProjectionWorker.Consume(second,p2));
Assert(results.Count(r=>r)==1,"two consumers both processed the selected event");
db.ChangeTracker.Clear();student=await db.Students.SingleAsync();
Assert(student.ActiveGenerationId!=null && await db.Generations.CountAsync()==1 && await db.Evidence.CountAsync()==1 && await db.Reviews.CountAsync()==1 && (await db.Masteries.SingleAsync()).Beta==3 && (await db.Outbox.SingleAsync()).ProcessedAt!=null,"retry did not converge exactly once");
Console.WriteLine("PASS AT23 双消费者重复领取：一个有效处理，一组证据和一个R1日程");
await using var again=Open();Assert(!await ProjectionWorker.Consume(again,await again.Outbox.SingleAsync()),"processed event handled again");Assert(await again.Generations.CountAsync()==1,"duplicate replay created generation");
Console.WriteLine("PASS 崩溃后重试及重复消费不增加评估世代或错误次数");

class CrashBeforeCommit : DbTransactionInterceptor
{
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData eventData,InterceptionResult result,CancellationToken cancellationToken=default)
    {
        Console.WriteLine("BEFORE_COMMIT");Console.Out.Flush();await Task.Delay(Timeout.Infinite,cancellationToken);return result;
    }
}
