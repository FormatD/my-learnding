using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Data.Common;

var connection=Environment.GetEnvironmentVariable("PERSISTENCE_TEST_CONNECTION")??throw new Exception("Supply isolated test database connection");
if(!connection.Contains("Database=learning_fault_",StringComparison.Ordinal))throw new Exception("Only disposable learning_fault_ database allowed");
Database Open(bool crash=false){var options=new DbContextOptionsBuilder<Database>().UseNpgsql(connection);if(crash)options.AddInterceptors(new CrashBeforeCommit());return new(options.Options);}
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
await using var db=Open();
if(args[0]=="goal-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261001203019_ScheduledLearningGoals");
    var family=new Family();var catalog=Content.Fixture();var release=new Release {FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var legacyStudent=new Student {FamilyId=family.Id,Name="Legacy goal acceptance",ActiveReleaseId=release.Id};
    var goal=new Goal {FamilyId=family.Id,StudentId=legacyStudent.Id,Title="历史阅读目标",Minutes=9,PaperReference="原始纸质引用",Subject="",GoalType="",Period="",ScheduleRule="",TargetValue=0,Priority=0,Version=0};
    var task=new StudyTask {FamilyId=family.Id,StudentId=legacyStudent.Id,ReleaseId=release.Id,GoalSnapshots="",Status="Completed",CompletedAt=DateTimeOffset.UtcNow};
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Families\" (\"Id\",\"Name\",\"Version\") VALUES ({family.Id},{family.Name},{family.Version})");
    db.AddRange(release,legacyStudent,goal,task);await db.SaveChangesAsync();
    await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();goal=await db.Goals.SingleAsync();task=await db.Tasks.SingleAsync();legacyStudent=await db.Students.SingleAsync();
    Assert(goal.Subject=="Unspecified" && goal.GoalType=="Activity" && goal.Period=="Daily" && goal.TargetValue==1 && goal.Priority==3 && goal.Version==1 && Json.Read<int[]>(goal.ScheduleRule).Length==7 && goal.Title=="历史阅读目标" && goal.Minutes==9 && goal.PaperReference=="原始纸质引用","legacy defaults invalid or original goal overwritten");
    Assert(task.GoalSnapshots=="[]" && task.Status=="Completed","unknown old goal relationship was inferred");Console.WriteLine("PASS 旧目标补齐结构默认值，原名称/资源/时长不改；旧任务关联保持未知");
    await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(family.Id);var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(legacyStudent.TimeZone)).DateTime);var revision=await Planning.Generate(db,legacyStudent,day);await db.SaveChangesAsync();await tx.CommitAsync();
    var placement=await db.Placements.SingleAsync(p=>p.RevisionId==revision.Id);var planned=await db.Tasks.SingleAsync(t=>t.Id==placement.TaskId);Assert(Json.Read<GoalSnapshot[]>(planned.GoalSnapshots).Single().Id==goal.Id,"migration did not preserve working plans or guessed old completion quota");Console.WriteLine("PASS 真实旧库升级后计划可生成，不用未记录的旧完成任务虚构目标次数");return;
}
if(args[0]=="family-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261002014148_GoalLegacyDefaults");
    var single=new Family();var multiple=new Family();var editorOnly=new Family();
    foreach(var f in new[]{single,multiple,editorOnly})await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Families\" (\"Id\",\"Name\",\"Version\") VALUES ({f.Id},{f.Name},{f.Version})");
    var accounts=new[]{new Account{FamilyId=single.Id,UserName="single",Roles="Parent,Publisher"},new Account{FamilyId=multiple.Id,UserName="multi1",Roles="Parent"},new Account{FamilyId=multiple.Id,UserName="multi2",Roles="Parent"},new Account{FamilyId=editorOnly.Id,UserName="editor",Roles="ContentEditor"}};
    db.AddRange(accounts);await db.SaveChangesAsync();await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();
    var memberships=await db.Set<FamilyMembership>().ToListAsync();Assert(memberships.Count==4 && accounts.All(a=>memberships.Any(m=>m.AccountId==a.Id && m.FamilyId==a.FamilyId && m.Roles==a.Roles)),"legacy membership permissions lost");
    Assert((await db.Families.SingleAsync(f=>f.Id==single.Id)).OwnerAccountId==accounts[0].Id,"single parent owner missing");
    Assert((await db.Families.SingleAsync(f=>f.Id==multiple.Id)).OwnerAccountId==null && (await db.Families.SingleAsync(f=>f.Id==editorOnly.Id)).OwnerAccountId==null,"ambiguous owner inferred");
    Console.WriteLine("PASS 旧账号角色准确迁移；单家长账号补齐负责人，多账号和纯编辑不推断");
    try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Families\" SET \"OwnerAccountId\"={accounts[0].Id} WHERE \"Id\"={multiple.Id}");throw new Exception("cross-family owner allowed");}catch(DbException){}
    Console.WriteLine("PASS 数据库外键拒绝跨家庭负责人关联");return;
}
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
if(args[0]=="builder")
{
    var f=await db.Families.SingleAsync();var original=await db.Releases.SingleAsync();var catalog=Json.Read<Catalog>(original.Payload);
    var next=catalog with {Kcs=catalog.Kcs.Select(k=>k with {RevisionId=Guid.NewGuid(),Name=k.Name+"（新版名称）"}).ToArray()};
    var source=new Source {FamilyId=f.Id,Title="Frozen retrieval acceptance",Text="先乘除后加减，独立确定运算顺序。",Hash=Guid.NewGuid().ToString()};var chunk=new Chunk {FamilyId=f.Id,SourceId=source.Id,Locator="段落 1",Text=source.Text};
    var run=new BuilderRun {FamilyId=f.Id,SourceId=source.Id,LibraryReleaseId=original.Id,InputHash="frozen-snapshot-test"};
    db.AddRange(source,chunk,run,new Release {FamilyId=f.Id,Number=2,Payload=Json.Write(next),Hash=Content.Hash(Json.Write(next))});await db.SaveChangesAsync();
    await Builder.ProcessOne(db,CancellationToken.None);
    var candidate=await db.Candidates.SingleAsync(c=>c.RunId==run.Id);var matches=Json.Read<Match[]>(candidate.Matches);
    Assert(matches.Length>0 && matches.All(m=>catalog.Kcs.Any(k=>k.Id==m.KCId && k.Name==m.Name)),"retrieval used newer library rather than frozen release");
    Console.WriteLine("PASS 建库任务排队后库版本变化：匹配仍使用冻结的原内容快照");
    var noLibrary=new BuilderRun {FamilyId=f.Id,SourceId=source.Id,InputHash="explicit-empty-library-test"};db.Add(noLibrary);await db.SaveChangesAsync();await Builder.ProcessOne(db,CancellationToken.None);
    var empty=await db.Candidates.SingleAsync(c=>c.RunId==noLibrary.Id);Assert(Json.Read<Match[]>(empty.Matches).Length==0,"empty library snapshot was replaced by current library");
    Console.WriteLine("PASS 明确空库快照不会临时检索后来发布的内容");
    var legacy=new BuilderRun {FamilyId=f.Id,SourceId=source.Id,InputVersion="builder-input/1",InputHash="legacy-unrecorded-input"};db.Add(legacy);await db.SaveChangesAsync();await Builder.ProcessOne(db,CancellationToken.None);
    Assert(legacy.Status=="Failed" && legacy.Error=="INPUT_SNAPSHOT_UNKNOWN" && !await db.Candidates.AnyAsync(c=>c.RunId==legacy.Id),"unknown legacy input was guessed");
    Console.WriteLine("PASS 历史输入不明的排队任务明确失败，要求重新运行");return;
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
