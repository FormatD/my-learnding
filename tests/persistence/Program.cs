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
if(args[0]=="parent-burden-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261002050245_BuilderRetryAttempts");var f=new Family();var s=new Student{FamilyId=f.Id,Name="原始学生"};db.AddRange(f,s);await db.SaveChangesAsync();
    await db.Database.MigrateAsync();db.ChangeTracker.Clear();Assert((await db.Students.SingleAsync()).Name=="原始学生" && !await db.Set<ParentBurdenRecord>().AnyAsync(),"migration inferred parent effort or changed student");
    var a=new ParentBurdenRecord{FamilyId=f.Id,StudentId=s.Id,RecordedBy=Guid.NewGuid(),Date=new DateOnly(2026,10,1),Minutes=5};var b=new ParentBurdenRecord{FamilyId=f.Id,StudentId=s.Id,RecordedBy=Guid.NewGuid(),Date=new DateOnly(2026,10,2),Minutes=3,SupersedesId=a.Id,CorrectionReason="原始记录多算"};db.AddRange(a,b);await db.SaveChangesAsync();
    var another=new Family();db.Add(another);await db.SaveChangesAsync();db.Add(new ParentBurdenRecord{FamilyId=another.Id,StudentId=s.Id,RecordedBy=Guid.NewGuid(),Minutes=3});try{await db.SaveChangesAsync();throw new Exception("cross-family student accepted");}catch(DbUpdateException){}db.ChangeTracker.Clear();
    Assert(await db.Set<ParentBurdenRecord>().CountAsync()==2,"invalid record persisted");await db.Students.Where(x=>x.Id==s.Id).ExecuteDeleteAsync();Assert(!await db.Set<ParentBurdenRecord>().AnyAsync(),"student deletion retained burden records");
    Console.WriteLine("PASS 旧库升级不推算家长投入；数据库拒绝跨家庭学生；学生删除清除原记录与更正链");return;
}
if(args[0]=="mastery-snapshot")
{
    await db.Database.MigrateAsync();var family=new Family();var catalog=Content.Fixture();var q=catalog.Questions[0];var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};db.AddRange(family,release);await db.SaveChangesAsync();
    async Task<(Student Student,Outbox Job)> Seed()
    {
        var s=new Student{FamilyId=family.Id,ActiveReleaseId=release.Id};var task=new StudyTask{FamilyId=family.Id,StudentId=s.Id,ReleaseId=release.Id,QuestionId=q.Id,Status="InProgress"};var session=new LearningSession{FamilyId=family.Id,StudentId=s.Id,TaskId=task.Id,ReleaseId=release.Id,QuestionId=q.Id};var attempt=new Attempt{FamilyId=family.Id,StudentId=s.Id,SessionId=session.Id,ClientSubmissionId=Guid.NewGuid(),Number=1,Answer="999"};var grade=new Grading{FamilyId=family.Id,AttemptId=attempt.Id,Number=1,Result="Incorrect"};var job=new Outbox{FamilyId=family.Id,StudentId=s.Id,AttemptId=attempt.Id};db.AddRange(s,task,session,attempt,grade,job);await db.SaveChangesAsync();return(s,job);
    }
    var actor=new Actor(Guid.NewGuid(),family.Id,Guid.NewGuid(),null,"Parent","Parent");var old=await Seed();
    await using(var reader=Open())
    {
        var s=await actor.Student(reader,old.Student.Id);var mastery=await reader.Masteries.Where(m=>m.StudentId==s.Id && m.GenerationId==s.ActiveGenerationId).ToListAsync();
        await using(var worker=Open())await ProjectionWorker.Consume(worker,await worker.Outbox.SingleAsync(o=>o.Id==old.Job.Id));
        var pending=await reader.Outbox.CountAsync(o=>o.StudentId==s.Id && o.ProcessedAt==null);
        Assert(s.ActiveGenerationId==null && mastery.Count==0 && pending==0,"did not reproduce read-committed mixed snapshot");
    }
    Console.WriteLine("PASS 精确复现旧读取次序：后台提交夹在查询之间，旧能力与零积压混在同一响应");
    var fresh=await Seed();var pause=new PauseOutboxCount();var options=new DbContextOptionsBuilder<Database>().UseNpgsql(connection).AddInterceptors(pause).Options;
    await using(var reader=new Database(options))
    {
        var reading=Assessment.ReadStatus(reader,actor,fresh.Student.Id);await pause.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try{await using var worker=Open();await ProjectionWorker.Consume(worker,await worker.Outbox.SingleAsync(o=>o.Id==fresh.Job.Id));}finally{pause.Resume.TrySetResult();}
        var snapshot=await reading;Assert(snapshot.Generation==null && snapshot.Masteries.Count==0 && snapshot.Pending==1,"read snapshot mixed old mastery with new receipt");
    }
    await using(var reader=Open())
    {
        var result=await Assessment.ReadStatus(reader,actor,fresh.Student.Id);Assert(result.Generation!=null && result.Masteries.Count==1 && result.Pending==0,"fresh status did not expose committed projection");
    }
    Console.WriteLine("PASS 同一数据库快照保持旧能力与待处理一起返回；下一次读取显示新证据与零积压");return;
}
if(args[0]=="goal-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261001203019_ScheduledLearningGoals");
    var family=new Family();var catalog=Content.Fixture();var release=new Release {FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var legacyStudent=new Student {FamilyId=family.Id,Name="Legacy goal acceptance",ActiveReleaseId=release.Id};
    var goal=new Goal {FamilyId=family.Id,StudentId=legacyStudent.Id,Title="历史阅读目标",Minutes=9,PaperReference="原始纸质引用",Subject="",GoalType="",Period="",ScheduleRule="",TargetValue=0,Priority=0,Version=0};
    var task=new StudyTask {FamilyId=family.Id,StudentId=legacyStudent.Id,ReleaseId=release.Id,GoalSnapshots="",Status="Completed",CompletedAt=DateTimeOffset.UtcNow};
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Families\" (\"Id\",\"Name\",\"Version\") VALUES ({family.Id},{family.Name},{family.Version})");
    db.AddRange(release,legacyStudent,task);await db.SaveChangesAsync();
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Goals\" (\"Id\",\"FamilyId\",\"CreatedAt\",\"StudentId\",\"Title\",\"Minutes\",\"PaperReference\",\"Active\",\"Subject\",\"GoalType\",\"KCId\",\"Period\",\"TargetValue\",\"ScheduleRule\",\"Priority\",\"StartDate\",\"EndDate\",\"Version\") VALUES ({goal.Id},{goal.FamilyId},{goal.CreatedAt},{goal.StudentId},{goal.Title},{goal.Minutes},{goal.PaperReference},{goal.Active},{goal.Subject},{goal.GoalType},NULL,{goal.Period},{goal.TargetValue},{goal.ScheduleRule},{goal.Priority},NULL,NULL,{goal.Version})");
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
if(args[0]=="catalog-goal-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261002022950_FamilyMemberships");var f=new Family();var c=Content.MultiplicationFixture();var r=new Release{FamilyId=f.Id,Number=1,Payload=Json.Write(c),Hash=Content.Hash(Json.Write(c))};var s=new Student{FamilyId=f.Id,ActiveReleaseId=r.Id};
    var g=new Goal{FamilyId=f.Id,StudentId=s.Id,Subject="Reading",GoalType="Reading",Period="Weekly",TargetValue=1};
    var oldScope=Content.Hash(Json.Write(new{g.Subject,g.GoalType,g.KCId}));var snapshot=Json.Write(new[]{new{g.Id,g.Version,scope=oldScope,g.Title,g.Subject,g.GoalType,g.KCId,g.Minutes,g.Period,g.TargetValue,g.ScheduleRule,g.Priority,g.StartDate,g.EndDate}});
    var t=new StudyTask{FamilyId=f.Id,StudentId=s.Id,ReleaseId=r.Id,Status="Completed",CompletedAt=DateTimeOffset.UtcNow,GoalSnapshots=snapshot};
    db.AddRange(f,r,s,t);await db.SaveChangesAsync();
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Goals\" (\"Id\",\"FamilyId\",\"CreatedAt\",\"StudentId\",\"Title\",\"Minutes\",\"PaperReference\",\"Active\",\"Subject\",\"GoalType\",\"KCId\",\"Period\",\"TargetValue\",\"ScheduleRule\",\"Priority\",\"StartDate\",\"EndDate\",\"Version\") VALUES ({g.Id},{g.FamilyId},{g.CreatedAt},{g.StudentId},{g.Title},{g.Minutes},{g.PaperReference},{g.Active},{g.Subject},{g.GoalType},NULL,{g.Period},{g.TargetValue},{g.ScheduleRule},{g.Priority},NULL,NULL,{g.Version})");
    await db.Database.MigrateAsync();db.ChangeTracker.Clear();g=await db.Goals.SingleAsync();t=await db.Tasks.SingleAsync();r=await db.Releases.SingleAsync();s=await db.Students.SingleAsync();
    Assert(g.CourseId==null && g.UnitId==null && t.GoalSnapshots==snapshot && (Json.Read<Catalog>(r.Payload).Textbooks??[]).Length==0,"old scopes or missing textbook metadata inferred");
    var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone)).DateTime);Assert(Goals.Completed(g,day,s.TimeZone,[t],[])==1,"old scope hash completion broken by new nullable scope columns");
    await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(f.Id);var plan=await Planning.Generate(db,s,day);await db.SaveChangesAsync();await tx.CommitAsync();Assert(!await db.Placements.AnyAsync(p=>p.RevisionId==plan.Id),"already fulfilled old goal repeated after migration");
    Console.WriteLine("PASS 旧目录保持未记录，旧任务快照原文不改；新增空范围不破坏已完成配额和计划");return;
}
if(args[0]=="plan-boundary-seed")
{
    await db.Database.MigrateAsync();var f=new Family();var account=new Account{FamilyId=f.Id,UserName=Environment.GetEnvironmentVariable("PLAN_TEST_USERNAME")!,PasswordHash=Security.Password(Environment.GetEnvironmentVariable("PLAN_TEST_PASSWORD")!)};var member=new FamilyMembership{FamilyId=f.Id,AccountId=account.Id,Roles=account.Roles};db.AddRange(f,account,member);await db.SaveChangesAsync();f.OwnerAccountId=account.Id;
    var c=Content.Fixture();var q=c.Questions[0];var r=new Release{FamilyId=f.Id,Number=1,Payload=Json.Write(c),Hash=Content.Hash(Json.Write(c)),PublishedBy=account.Id};var s=new Student{FamilyId=f.Id,ActiveReleaseId=r.Id,Name="Due review fixture"};
    var past=DateTimeOffset.UtcNow.AddDays(-2);var oldPlan=new Plan{FamilyId=f.Id,StudentId=s.Id,Date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(past,TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone)).DateTime),Status="Completed"};var revision=new PlanRevision{FamilyId=f.Id,PlanId=oldPlan.Id,ReleaseId=r.Id,Number=1,Status="Published",Budget=30,InputHash="past-fixture"};
    var task=new StudyTask{FamilyId=f.Id,StudentId=s.Id,ReleaseId=r.Id,QuestionId=q.Id,KCId=q.Mappings[0].KCId,Title="原始错题",Status="Completed",CreatedAt=past,CompletedAt=past.AddMinutes(3),TrackedSeconds=180,ActualMinutes=3};var session=new LearningSession{FamilyId=f.Id,StudentId=s.Id,TaskId=task.Id,ReleaseId=r.Id,QuestionId=q.Id,StartedAt=past,CreatedAt=past};var attempt=new Attempt{FamilyId=f.Id,StudentId=s.Id,SessionId=session.Id,ClientSubmissionId=Guid.NewGuid(),Number=1,Answer="999",CreatedAt=past.AddMinutes(2)};var grade=new Grading{FamilyId=f.Id,AttemptId=attempt.Id,Number=1,Result="Incorrect",CreatedAt=attempt.CreatedAt};var job=new Outbox{FamilyId=f.Id,StudentId=s.Id,AttemptId=attempt.Id};
    db.AddRange(r,s,oldPlan,revision,task,session,attempt,grade,job,new Placement{FamilyId=f.Id,RevisionId=revision.Id,TaskId=task.Id,Sequence=0});await Publishing.Register(db,r);await db.SaveChangesAsync();oldPlan.ActiveRevisionId=revision.Id;await db.SaveChangesAsync();await ProjectionWorker.Consume(db,job);
    Console.WriteLine(Json.Write(new{familyId=f.Id,studentId=s.Id,releaseId=r.Id,questionId=q.Id,kcId=task.KCId}));return;
}
if(args[0]=="plan-rule-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261002025728_CatalogGoalScopes");var f=new Family();var c=Content.MultiplicationFixture();var r=new Release{FamilyId=f.Id,Number=1,Payload=Json.Write(c),Hash=Content.Hash(Json.Write(c))};var s=new Student{FamilyId=f.Id,ActiveReleaseId=r.Id};var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone)).DateTime);var plan=new Plan{FamilyId=f.Id,StudentId=s.Id,Date=day};db.AddRange(f,r,s,plan);await db.SaveChangesAsync();
    var id=Guid.NewGuid();var stamp=DateTimeOffset.UtcNow;
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PlanRevisions\" (\"Id\",\"FamilyId\",\"CreatedAt\",\"PlanId\",\"ReleaseId\",\"Number\",\"Budget\",\"Reserved\",\"Overflow\",\"Status\",\"InputHash\",\"Candidates\",\"Warnings\") VALUES ({id},{f.Id},{stamp},{plan.Id},{r.Id},1,13,2,0,'Published','legacy-rule-fixture','[]','[]')");
    await db.Database.MigrateAsync();db.ChangeTracker.Clear();var old=await db.PlanRevisions.SingleAsync();Assert(old.RuleVersion=="legacy/unknown" && old.InputHash=="legacy-rule-fixture" && old.Budget==13 && old.Reserved==2 && old.Status=="Published","old plan rule inferred or original snapshot overwritten");
    await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(f.Id);var current=await Planning.Generate(db,await db.Students.SingleAsync(),day);await db.SaveChangesAsync();await tx.CommitAsync();Assert(current.RuleVersion==Planning.RuleVersion && current.RuleVersion=="plan/2" && current.Number==2,"new plan rule version not persisted");Console.WriteLine("PASS 历史计划规则保持未记录且原快照不改；新计划持久化 plan/2");return;
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
if(args[0]=="builder-retry-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261002041538_KnowledgeChangeProposals");var family=new Family();var source=new Source{FamilyId=family.Id,Title="旧建库来源",Text="旧运行记录输入",Hash="legacy-builder-source"};db.AddRange(family,source);await db.SaveChangesAsync();var id=Guid.NewGuid();var at=DateTimeOffset.UtcNow.AddDays(-1);
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"BuilderRuns\" (\"Id\",\"FamilyId\",\"CreatedAt\",\"LibraryReleaseId\",\"InputVersion\",\"Type\",\"SourceId\",\"Status\",\"Provider\",\"Model\",\"PromptVersion\",\"InputHash\",\"Error\",\"Retries\",\"CompletedAt\") VALUES ({id},{family.Id},{at},NULL,'builder-input/1','Candidates',{source.Id},'Failed','Mock','fixture/1','kc-candidate/1','legacy-input-hash','INPUT_SNAPSHOT_UNKNOWN',2,{at})");
    await db.Database.MigrateAsync();db.ChangeTracker.Clear();var old=await db.BuilderRuns.SingleAsync();Assert(old.Status=="Failed" && old.Retries==2 && old.InputHash=="legacy-input-hash" && old.InputVersion=="builder-input/1" && old.NextAttemptAt==null && old.RetryRound==0 && !await db.Set<BuilderAttempt>().AnyAsync(),"legacy attempts fabricated or source state changed");Console.WriteLine("PASS 旧建库状态、次数与未知输入原样保留，不生成虚构尝试历史或自动重试");return;
}
if(args[0]=="builder-retry")
{
    await db.Database.MigrateAsync();var f=new Family();var catalog=Content.Fixture();var text=Json.Write(catalog);var release=new Release{FamilyId=f.Id,Number=1,Payload=text,Hash=Content.Hash(text)};var source=new Source{FamilyId=f.Id,Title="Retry rollback fixture",Text="先乘除后加减，独立判断运算顺序。",Hash="retry-fixture"};var run=new BuilderRun{FamilyId=f.Id,SourceId=source.Id,LibraryReleaseId=release.Id,InputHash="fixed-retry-input"};var healthySource=new Source{FamilyId=f.Id,Title="Other queued work",Text="确认独立步骤和边界。",Hash="healthy-fixture"};var healthy=new BuilderRun{FamilyId=f.Id,SourceId=healthySource.Id,InputHash="healthy-input",CreatedAt=DateTimeOffset.UtcNow.AddSeconds(1)};
    db.AddRange(f,release,source,run,healthySource,healthy,new Chunk{FamilyId=f.Id,SourceId=source.Id,Text=source.Text,Locator="段落1"},new Chunk{FamilyId=f.Id,SourceId=healthySource.Id,Text=healthySource.Text,Locator="段落1"});await Publishing.Register(db,release);await db.SaveChangesAsync();
    var failOptions=new DbContextOptionsBuilder<Database>().UseNpgsql(connection).AddInterceptors(new BuilderFailure(run.Id)).Options;
    for(var failure=1;failure<=4;failure++)
    {
        await using(var worker=new Database(failOptions))await Builder.ProcessOne(worker,CancellationToken.None);
        db.ChangeTracker.Clear();run=await db.BuilderRuns.SingleAsync(r=>r.Id==run.Id);var attempts=await db.Set<BuilderAttempt>().Where(a=>a.RunId==run.Id).OrderBy(a=>a.Number).ToArrayAsync();
        Assert(run.Retries==Math.Min(failure,3) && run.Status==(failure<4?"Queued":"Failed") && attempts.Length==failure,"builder retry bound or attempts invalid");Assert(!await db.Candidates.AnyAsync(c=>c.RunId==run.Id),"partial candidates persisted after save failure");
        if(failure==1)
        {
            Assert(!await db.Set<Embedding>().AnyAsync(),"partial embeddings persisted after save failure");await Builder.ProcessOne(db,CancellationToken.None);Assert((await db.BuilderRuns.SingleAsync(r=>r.Id==healthy.Id)).Status=="Completed","backoff job blocked unrelated queued work");
        }
        if(failure<4)
        {
            Assert(run.NextAttemptAt!=null && attempts.Last().Status=="RetryScheduled" && (run.NextAttemptAt.Value-attempts.Last().FinishedAt).TotalSeconds>=Math.Pow(2,failure)-.1,"retry backoff not persisted");
            await Builder.ProcessOne(db,CancellationToken.None);Assert(await db.Set<BuilderAttempt>().CountAsync(a=>a.RunId==run.Id)==failure,"retry ran before its scheduled time");await db.BuilderRuns.Where(r=>r.Id==run.Id).ExecuteUpdateAsync(update=>update.SetProperty(r=>r.NextAttemptAt,DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        else Assert(run.NextAttemptAt==null && run.CompletedAt!=null && attempts.Last().Status=="Failed","exhausted job not stopped");
    }
    Console.WriteLine("PASS 初次失败加3次退避重试，候选与索引事务回滚，等待不阻塞其他任务，最后进入人工失败队列");
    var history=await db.Set<BuilderAttempt>().Where(a=>a.RunId==run.Id).ToArrayAsync();Assert(history.Select(a=>a.InputSnapshot).Distinct().Count()==1 && history.All(a=>a.ErrorCode=="BUILDER_PROCESSING_FAILED"),"retry input drift or unsafe error code");
    var next=catalog with{Kcs=catalog.Kcs.Select(k=>k with{Name=k.Name+"新版本",RevisionId=Guid.NewGuid()}).ToArray()};var nextText=Json.Write(next);var r2=new Release{FamilyId=f.Id,Number=2,Payload=nextText,Hash=Content.Hash(nextText)};db.Add(r2);await Publishing.Register(db,r2);await db.SaveChangesAsync();await db.BuilderRuns.Where(r=>r.Id==run.Id).ExecuteUpdateAsync(u=>u.SetProperty(r=>r.Status,"Queued").SetProperty(r=>r.Retries,0).SetProperty(r=>r.RetryRound,1).SetProperty(r=>r.NextAttemptAt,(DateTimeOffset?)null));
    await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();var output=await db.Candidates.SingleAsync(c=>c.RunId==run.Id);Assert(Json.Read<Match[]>(output.Matches).All(m=>catalog.Kcs.Any(k=>k.Id==m.KCId && k.Name==m.Name)),"manual retry used latest library instead of frozen input");Assert(await db.Set<BuilderAttempt>().CountAsync(a=>a.RunId==run.Id)==5 && (await db.BuilderRuns.SingleAsync(r=>r.Id==run.Id)).Error==null,"retry round lost history or retained stale success error");await Builder.ProcessOne(db,CancellationToken.None);Assert(await db.Candidates.CountAsync(c=>c.RunId==run.Id)==1,"completed job regenerated output");Console.WriteLine("PASS 原输入跨内容版本和重试轮次固定，旧失败历史保留，成功后不重复生成");return;
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
    var legacy=new BuilderRun {FamilyId=f.Id,SourceId=source.Id,InputVersion="builder-input/1",InputHash="legacy-unrecorded-input"};db.Add(legacy);await db.SaveChangesAsync();await Builder.ProcessOne(db,CancellationToken.None);legacy=await db.BuilderRuns.SingleAsync(r=>r.Id==legacy.Id);
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

sealed class BuilderFailure(Guid runId):SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken cancellationToken=default)
    {
        if(data.Context!.ChangeTracker.Entries<Candidate>().Any(e=>e.State==EntityState.Added && e.Entity.RunId==runId))throw new IOException("Injected save failure in disposable database");return ValueTask.FromResult(result);
    }
}

sealed class PauseOutboxCount:DbCommandInterceptor
{
    public TaskCompletionSource Ready {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Resume {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int paused;
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
    {
        if(command.CommandText.Contains("count(*)",StringComparison.Ordinal) && command.CommandText.Contains("\"Outbox\"",StringComparison.Ordinal) && Interlocked.Exchange(ref paused,1)==0)
        {Ready.TrySetResult();await Resume.Task.WaitAsync(TimeSpan.FromSeconds(10),cancellationToken);}
        return result;
    }
}
