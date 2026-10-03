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
if(args[0]=="builder-budget"){await BuilderBudgetPersistenceCases.Run(db);return;}
if(args[0]=="builder-config")
{
    await db.Database.MigrateAsync();var family=new Family();var source=new Source{FamilyId=family.Id,Title="固定配置数据库验收",Text="先乘除。\n后加减。",Hash=Content.Hash("先乘除。\n后加减。")};db.AddRange(family,source);db.AddRange(new Chunk{FamilyId=family.Id,SourceId=source.Id,Locator="段落1",Text="先乘除。"},new Chunk{FamilyId=family.Id,SourceId=source.Id,Locator="段落2",Text="后加减。"});await db.SaveChangesAsync();
    async Task<BuilderRun> Create(int limit,string input="builder-input/3")
    {
        var payload=Json.Write(new BuilderModelConfiguration("builder-config/1","Mock","fixture/1","kc-candidate/1",Content.Hash(BuilderProtocol.Schema),new BuilderLimits(MaxFragments:limit)));var hash=Content.Hash(payload);var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,InputVersion=input,ModelConfigPayload=payload,ModelConfigHash=hash,InputHash=Content.Hash(source.Hash+":Mock:fixture/1:kc-candidate/1:"+input+"::"+hash)};db.Add(run);await db.SaveChangesAsync();return run;
    }
    var frozen=await Create(1);var original=frozen.ModelConfigPayload;await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();frozen=await db.BuilderRuns.SingleAsync(r=>r.Id==frozen.Id);Assert(frozen.Status=="Failed" && frozen.Error=="BUILDER_INPUT_LIMIT" && !await db.Candidates.AnyAsync(),"saved lower limit was ignored or produced partial candidates");
    frozen.Status="Queued";frozen.Error=null;await db.SaveChangesAsync();await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();frozen=await db.BuilderRuns.SingleAsync(r=>r.Id==frozen.Id);Assert(frozen.Error=="BUILDER_INPUT_LIMIT" && frozen.ModelConfigPayload==original && await db.Set<BuilderAttempt>().CountAsync(a=>a.RunId==frozen.Id)==2,"repeat execution changed frozen limits or lost attempts");Console.WriteLine("PASS 固定低限额两次真实处理仍明确失败，原配置保持且无部分候选");
    var higher=await Create(2);await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();higher=await db.BuilderRuns.SingleAsync(r=>r.Id==higher.Id);Assert(higher.Status=="Completed" && higher.InputHash!=frozen.InputHash && await db.Candidates.CountAsync(c=>c.RunId==higher.Id)==2,"different fixed config did not receive an independent result");Console.WriteLine("PASS 新配置有独立摘要，真实完整生成两候选且不影响旧任务");
    var damaged=await Create(3);damaged.ModelConfigPayload=null;await db.SaveChangesAsync();await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();damaged=await db.BuilderRuns.SingleAsync(r=>r.Id==damaged.Id);Assert(damaged.Error=="RUN_CONFIGURATION_UNKNOWN" && !await db.Candidates.AnyAsync(c=>c.RunId==damaged.Id),"missing modern config silently defaulted");
    var inconsistent=await Create(4);inconsistent.InputHash="mismatched-input";await db.SaveChangesAsync();await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();inconsistent=await db.BuilderRuns.SingleAsync(r=>r.Id==inconsistent.Id);Assert(inconsistent.Error=="INPUT_SNAPSHOT_UNKNOWN" && !await db.Candidates.AnyAsync(c=>c.RunId==inconsistent.Id),"changed config bypassed frozen input hash");Console.WriteLine("PASS 现代缺失配置与配置/输入摘要矛盾分别拒绝，零部分输出");
    var legacy=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,InputHash="legacy-unrecorded-config"};db.Add(legacy);await db.SaveChangesAsync();await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();legacy=await db.BuilderRuns.SingleAsync(r=>r.Id==legacy.Id);Assert(legacy.Status=="Completed" && legacy.ModelConfigHash==null && legacy.ModelConfigPayload==null,"legacy compatibility invented configuration");Console.WriteLine("PASS 旧未记录配置可按明确兼容流程处理，原NULL不回填");return;
}
if(args[0]=="revocation-multiple")
{
    await db.Database.MigrateAsync();var family=new Family();var actor=new Account{FamilyId=family.Id,UserName="multi-correction-"+Guid.NewGuid()};var catalog=Content.Fixture();var kc=catalog.Questions[0].Mappings[0].KCId;var group=Guid.NewGuid();var questions=catalog.Questions.Take(3).Select(q=>q with{Difficulty="Hard",VariantGroupId=group,Mappings=[new(kc,"Primary",1,"WholeItem",null)]}).Append(catalog.Questions.Skip(3).First(q=>q.Policy=="SingleKC" && q.Mappings[0].KCId!=kc) with{Difficulty="Hard"}).ToArray();catalog=catalog with{Questions=questions};var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var learner=new Student{FamilyId=family.Id,ActiveReleaseId=release.Id};db.AddRange(family,actor,release,learner);await Publishing.Register(db,release);await db.SaveChangesAsync();
    var attempts=new List<Guid>();var time=new DateTimeOffset(DateTime.UtcNow.Date.AddHours(4),TimeSpan.Zero);
    foreach(var q in questions){var task=new StudyTask{FamilyId=family.Id,StudentId=learner.Id,ReleaseId=release.Id,QuestionId=q.Id};var session=new LearningSession{FamilyId=family.Id,StudentId=learner.Id,TaskId=task.Id,ReleaseId=release.Id,QuestionId=q.Id};var a=new Attempt{FamilyId=family.Id,StudentId=learner.Id,SessionId=session.Id,ClientSubmissionId=Guid.NewGuid(),Number=1,Answer=q.Answer,CreatedAt=time.AddMinutes(attempts.Count)};db.AddRange(task,session,a,new Grading{FamilyId=family.Id,AttemptId=a.Id,Number=1,Result="Correct"});attempts.Add(a.Id);await db.SaveChangesAsync();}db.ChangeTracker.Clear();learner=await db.Students.SingleAsync();
    async Task Rebuild(){await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(family.Id);await Assessment.Rebuild(db,learner);await tx.CommitAsync();}
    await Rebuild();var original=Json.Write(await db.Attempts.OrderBy(a=>a.Sequence).ToArrayAsync());var old=await db.Evidence.Where(e=>e.GenerationId==learner.ActiveGenerationId).OrderBy(e=>e.OccurredAt).ToArrayAsync();Assert(old.Take(3).Select(e=>e.Weight).SequenceEqual(new[]{1.2m,.8m,0m}),"fixture did not exercise downstream cap");
    var clock=DateTimeOffset.UtcNow;
    async Task<CorrectionBatch> Grade(int index,string result,string reason){var a=attempts[index];var prior=await db.Gradings.Where(g=>g.AttemptId==a).OrderBy(g=>g.Number).LastAsync();clock=clock.AddSeconds(1);var batch=new CorrectionBatch{FamilyId=family.Id,StudentId=learner.Id,ReleaseId=release.Id,Cause="Grading",AffectedAttemptIds=Json.Write(new[]{a}),SourceGradingRevisionId=prior.Id,Reason=reason,ConfirmedBy=actor.Id,CreatedAt=clock};db.AddRange(batch,new Grading{FamilyId=family.Id,AttemptId=a,Number=prior.Number+1,Result=result,CorrectionBatchId=batch.Id,Reason=reason,Method="ParentConfirmed",GradedBy=actor.Id});await db.SaveChangesAsync();return batch;}
    var root=await Grade(0,"Incorrect","首题更正释放同构限额");var unrelated=await Grade(3,"Incorrect","另一能力的独立更正");await Rebuild();var revoked=await db.Set<EvidenceRevocation>().ToArrayAsync();Assert(revoked.Length==4,"coalesced correction lost old evidence");foreach(var e in old)Assert(revoked.Single(r=>r.EvidenceId==e.Id).CorrectionBatchId==(e.AttemptId==attempts[3]?unrelated.Id:root.Id),"unrelated latest correction captured another cause");
    var current=await db.Evidence.Where(e=>e.GenerationId==learner.ActiveGenerationId).OrderBy(e=>e.OccurredAt).ToArrayAsync();Assert(current.Take(3).Select(e=>e.Weight).SequenceEqual(new[]{.8m,.96m,.96m}) && current.Take(3).All(e=>e.CorrectionBatchId==root.Id),"current parts have wrong source");Console.WriteLine("PASS 两个更正一次重放：直接/后续限额证据归给实际根更正，无关的较晚更正不会冒领原因");
    var restore=await Grade(0,"Correct","恢复首题，重算后续限额");var noOp=await Grade(2,"Correct","后续第三题同结果复核");await Rebuild();var latest=await db.Set<EvidenceRevocation>().Where(r=>r.ReplacementGenerationId==learner.ActiveGenerationId).ToArrayAsync();Assert(latest.Length==3 && latest.All(r=>r.CorrectionBatchId==restore.Id),"no-op direct review captured dependency revocation");var third=await db.Set<AssessmentContext>().SingleAsync(c=>c.GenerationId==learner.ActiveGenerationId && c.AttemptId==attempts[2]);Assert(third.GradingCorrectionBatchId==noOp.Id && third.CorrectionBatchId==restore.Id,"own grading source must differ from real downstream cause");Assert(latest.Single(r=>r.EvidenceId==current[2].Id).Effect=="ReplayDependency" && (await db.Evidence.SingleAsync(e=>e.GenerationId==learner.ActiveGenerationId && e.AttemptId==attempts[2])).CorrectionBatchId==restore.Id,"no-op grading must not replace actual part cause");Console.WriteLine("PASS 同结果判分不冒领后续变化；上下文保存真实判分来源与实际重放原因");
    var count=await db.Set<EvidenceRevocation>().CountAsync();var superseded=await Grade(0,"Incorrect","被后续更正替代");var adopted=await Grade(0,"Correct","最终采用相同结果");await Rebuild();Assert(superseded.Status=="Superseded" && adopted.Status=="Applied" && await db.Set<EvidenceRevocation>().CountAsync()==count,"superseded intermediate input fabricated a revocation");
    // Mapping correction and an unrelated no-op grading are deliberately queued before one rebuild.
    var target=catalog.Kcs.First(k=>k.Id!=kc && k.Id!=questions[3].Mappings[0].KCId).Id;var mapped=catalog with{Questions=catalog.Questions.Select(q=>q.Id==questions[0].Id?q with{RevisionId=Guid.NewGuid(),Mappings=[new(target,"Primary",.5m,"WholeItem",null)]}:q).ToArray()};var r2=new Release{FamilyId=family.Id,Number=2,Payload=Json.Write(mapped),Hash=Content.Hash(Json.Write(mapped))};db.Add(r2);await Publishing.Register(db,r2);clock=clock.AddSeconds(1);var mapping=new CorrectionBatch{FamilyId=family.Id,StudentId=learner.Id,ReleaseId=r2.Id,Cause="Mapping",AffectedAttemptIds=Json.Write(new[]{attempts[0]}),Reason="首题迁移能力并核对半份测量预算",ConfirmedBy=actor.Id,CreatedAt=clock};db.AddRange(mapping,new CorrectionItem{FamilyId=family.Id,BatchId=mapping.Id,AttemptId=attempts[0],MappingReleaseId=r2.Id,QuestionRevisionId=mapped.Questions[0].RevisionId});await db.SaveChangesAsync();var unrelatedNoOp=await Grade(3,"Incorrect","无关能力同结果核对");await Rebuild();latest=await db.Set<EvidenceRevocation>().Where(r=>r.ReplacementGenerationId==learner.ActiveGenerationId).ToArrayAsync();Assert(latest.Length==6 && latest.All(r=>r.CorrectionBatchId==mapping.Id),"mapping changes were blamed on unrelated grading");Assert(await db.Evidence.CountAsync(e=>e.GenerationId==learner.ActiveGenerationId && e.KCId==target)==1,"actual mapping replay not applied");var actualWeights=await db.Evidence.Where(e=>e.GenerationId==learner.ActiveGenerationId).OrderBy(e=>e.OccurredAt).Select(e=>e.Weight).ToArrayAsync();Assert(actualWeights.Take(3).SequenceEqual(new[]{.6m,.96m,.44m}),"mapping budget did not produce independently expected downstream caps");Assert(Json.Write(await db.Attempts.OrderBy(a=>a.Sequence).ToArrayAsync())==original,"raw attempts changed");await Rebuild();Assert(await db.Set<EvidenceRevocation>().CountAsync()==count+6,"identical replay double revoked");var partBefore=(await db.Evidence.Where(e=>e.GenerationId==learner.ActiveGenerationId).ToArrayAsync()).ToDictionary(e=>(e.AttemptId,e.KCId,e.Part,e.Positive),e=>e.CorrectionBatchId);var causeBefore=(await db.Set<AssessmentContext>().Where(c=>c.GenerationId==learner.ActiveGenerationId).ToArrayAsync()).ToDictionary(c=>c.AttemptId,c=>c.CorrectionBatchId);
    var originalFirstAttempt=await db.Attempts.SingleAsync(a=>a.Id==attempts[0]);var retry=new Attempt{FamilyId=family.Id,StudentId=learner.Id,SessionId=originalFirstAttempt.SessionId,ClientSubmissionId=Guid.NewGuid(),Number=2,Answer="复做",CreatedAt=time.AddMinutes(5)};db.AddRange(retry,new Grading{FamilyId=family.Id,AttemptId=retry.Id,Number=1,Result="Correct"});await db.SaveChangesAsync();await Rebuild();var nextParts=await db.Evidence.Where(e=>e.GenerationId==learner.ActiveGenerationId).ToArrayAsync();Assert(nextParts.All(e=>e.CorrectionBatchId==partBefore[(e.AttemptId,e.KCId,e.Part,e.Positive)]),"ordinary retry erased equivalent evidence provenance");var nextContexts=await db.Set<AssessmentContext>().Where(c=>c.GenerationId==learner.ActiveGenerationId && c.AttemptId!=retry.Id).ToArrayAsync();Assert(nextContexts.All(c=>c.CorrectionBatchId==causeBefore[c.AttemptId]),"ordinary generation erased mathematical context cause");Assert(await db.Set<EvidenceRevocation>().CountAsync()==count+6,"ordinary arrival fabricated revocation");Console.WriteLine("PASS 被替代中间结果不制造撤销；映射与无关判分合并原因正确，原作答稳定；普通重放继承等价证据真实原因");return;
}
if(args[0]=="independent-mapping-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261002204255_EvidenceRevocations");
    var family=new Family();var account=new Account{FamilyId=family.Id,UserName="legacy-independent-"+Guid.NewGuid()};var catalog=Content.Fixture();var draft=new ContentDraft{FamilyId=family.Id,Payload=Json.Write(catalog),Title="真实旧审核快照"};var release=new Release{FamilyId=family.Id,Number=1,Payload=draft.Payload,Hash=Content.Hash(draft.Payload)};db.AddRange(family,account,draft,release);await Publishing.Register(db,release);await db.SaveChangesAsync();
    var review=new ContentReviewRecord{FamilyId=family.Id,DraftId=draft.Id,DraftVersion=draft.Version,ReviewerId=account.Id,SourcePayload=draft.Payload,SourceTitle=draft.Title,PayloadHash=release.Hash};var q=catalog.Questions[0];var set=new MappingSetRevision{FamilyId=family.Id,DraftId=draft.Id,ContentReviewRecordId=review.Id,OwnerType="Question",OwnerId=q.Id,OwnerRevisionId=q.RevisionId,OriginalOwnerRevisionId=q.RevisionId,OwnerDefinitionHash=Content.Hash(Json.Write(q)),RevisionNo=1,EvidencePolicy=q.Policy,ReviewStatus="ReviewedCatalog",CoverageOrigin="CatalogDefault"};var m=q.Mappings[0];var item=new MappingSetItem{FamilyId=family.Id,SetRevisionId=set.Id,KCId=m.KCId,KCRevisionId=catalog.Kcs.Single(k=>k.Id==m.KCId).RevisionId,Role=m.Role,EvidenceShare=m.Share,EvidenceMode=m.Mode,Step=m.Step,Sequence=1};db.AddRange(review,set,item);await db.SaveChangesAsync();
    async Task<string> Bytes(){await db.Database.OpenConnectionAsync();using var cmd=db.Database.GetDbConnection().CreateCommand();cmd.CommandText="SELECT jsonb_build_object('sets',(SELECT jsonb_agg(to_jsonb(s) ORDER BY s.\"Id\") FROM \"MappingSetRevision\" s),'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY i.\"Id\") FROM \"MappingSetItem\" i),'reviews',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r.\"Id\") FROM \"ContentReviewRecord\" r))::text";return (string)(await cmd.ExecuteScalarAsync())!;}
    var before=await Bytes();await db.Database.MigrateAsync();db.ChangeTracker.Clear();Assert(await Bytes()==before && !await db.Set<IndependentMappingDraft>().AnyAsync(),"migration changed old mappings or fabricated independent provenance");
    try{await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "MappingSetRevision" SET "ReviewStatus"='Draft' WHERE "Id"={set.Id}""");throw new Exception("unreviewed draft accepted a review source");}catch(Npgsql.PostgresException ex) when(ex.SqlState=="23514"){}
    try{await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "MappingSetRevision" SET "ContentReviewRecordId"=NULL WHERE "Id"={set.Id}""");throw new Exception("reviewed set accepted missing review source");}catch(Npgsql.PostgresException ex) when(ex.SqlState=="23514"){}
    var pending=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "MappingSetRevision" SELECT (jsonb_populate_record(NULL::"MappingSetRevision",to_jsonb(s)||jsonb_build_object('Id',{pending},'ReviewStatus','Draft','ContentReviewRecordId',NULL,'ReviewDecisionId',NULL,'CoverageOrigin','UnreviewedDraft','RevisionNo',2))).* FROM "MappingSetRevision" s WHERE "Id"={set.Id}""");Assert((await db.Set<MappingSetRevision>().SingleAsync(x=>x.Id==pending)).ContentReviewRecordId==null,"draft gained invented approval");
    Console.WriteLine("PASS 旧映射与审核所有字段不变、零补造来源；数据库允许无审核草稿，拒绝待审附审核或已审缺审核");await db.Families.Where(f=>f.Id==family.Id).ExecuteDeleteAsync();Assert(!await db.Set<MappingSetRevision>().AnyAsync() && !await db.Set<ContentReviewRecord>().AnyAsync(),"family deletion retained private mappings");Console.WriteLine("PASS 家庭删除清除映射、条目与审核，旧版本迁移路径可用");return;
}
if(args[0]=="assessment-context-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261002191928_LearningMappingReferences");
    var f=new Family();var catalog=Content.Fixture();var q=catalog.Questions[0];var r=new Release{FamilyId=f.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var s=new Student{FamilyId=f.Id,ActiveReleaseId=r.Id};var t=new StudyTask{FamilyId=f.Id,StudentId=s.Id,ReleaseId=r.Id,QuestionId=q.Id};var session=new LearningSession{FamilyId=f.Id,StudentId=s.Id,TaskId=t.Id,ReleaseId=r.Id,QuestionId=q.Id};var a=new Attempt{FamilyId=f.Id,StudentId=s.Id,SessionId=session.Id,ClientSubmissionId=Guid.NewGuid(),Number=1,Answer="999"};var g=new Grading{FamilyId=f.Id,AttemptId=a.Id,Number=1,Result="Incorrect"};var gen=new Generation{FamilyId=f.Id,StudentId=s.Id,Status="Active",InputHash="legacy-evaluation"};
    db.AddRange(f,r,s,t,session,a,gen);await db.SaveChangesAsync();
    await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "Gradings" ("Id","FamilyId","AttemptId","Number","Result","Method","Reason","Steps","CreatedAt") VALUES ({g.Id},{f.Id},{a.Id},1,'Incorrect','Rule','','[]',{g.CreatedAt})""");
    var legacyBatch=Guid.NewGuid();var actor=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "CorrectionBatch" ("Id","FamilyId","StudentId","ReleaseId","PreviewHash","Reason","ConfirmedBy","Status","CreatedAt") VALUES ({legacyBatch},{f.Id},{s.Id},{r.Id},'legacy-preview','旧映射确认记录',{actor},'Confirmed',{g.CreatedAt})""");
    db.Add(new CorrectionItem{FamilyId=f.Id,BatchId=legacyBatch,AttemptId=a.Id,MappingReleaseId=r.Id});await Publishing.Register(db,r);s.ActiveGenerationId=gen.Id;await db.SaveChangesAsync();
    var eid=Guid.NewGuid();var when=DateTimeOffset.UtcNow;var kc=q.Mappings[0].KCId;var revision=catalog.Kcs.Single(k=>k.Id==kc).RevisionId;
    await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "Evidence" ("Id","FamilyId","StudentId","GenerationId","AttemptId","GradingId","ReleaseId","KCId","KCRevisionId","Part","Positive","RawWeight","Weight","Factors","OccurredAt","CreatedAt") VALUES ({eid},{f.Id},{s.Id},{gen.Id},{a.Id},{g.Id},{r.Id},{kc},{revision},'WholeItem',false,1,1,'legacy factor',{when},{when})""");
    async Task<string> OldBytes(){await db.Database.OpenConnectionAsync();using var cmd=db.Database.GetDbConnection().CreateCommand();cmd.CommandText=$"SELECT (to_jsonb(e)-'ContextId')::text FROM \"Evidence\" e WHERE \"Id\"='{eid}'";return (string)(await cmd.ExecuteScalarAsync())!;}
    var old=await OldBytes();await db.Database.MigrateAsync();db.ChangeTracker.Clear();Assert(await OldBytes()==old && (await db.Evidence.SingleAsync()).ContextId==null && !await db.Set<AssessmentContext>().AnyAsync(),"upgrade changed old evidence or invented a context");Assert(!await db.Set<EvidenceRevocation>().AnyAsync() && (await db.Gradings.SingleAsync()).CorrectionBatchId==null && (await db.Set<CorrectionBatch>().SingleAsync()).Cause==null,"upgrade fabricated revocations or grading causes");
    await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(f.Id);await Assessment.Rebuild(db,await db.Students.SingleAsync());await tx.CommitAsync();}
    var context=await db.Set<AssessmentContext>().SingleAsync();Assert(context.MappingSource=="LegacySnapshot" && context.MappingSetRevisionId==null && context.ActivationStatus=="Active" && context.QuestionRevisionId==q.RevisionId,"new replay guessed a legacy mapping or missed its actual question");
    Assert(await OldBytes()==old && (await db.Evidence.SingleAsync(e=>e.Id==eid)).ContextId==null && (await db.Evidence.SingleAsync(e=>e.GenerationId==context.GenerationId)).ContextId==context.Id,"new replay rewrote old evidence or left new evidence without context");
    Console.WriteLine("PASS 旧库迁移不改原证据/不补上下文；新重放明确LegacySnapshot并固定实际题目和新上下文，旧证据保持NULL");
    try{await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Evidence" SET "ContextId"={context.Id},"Part"='ForeignContext' WHERE "Id"={eid}""");throw new Exception("context from another generation accepted");}catch(Npgsql.PostgresException ex) when(ex.SqlState=="23503"){}
    var duplicate=Guid.NewGuid();try{await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "AssessmentContext" SELECT (jsonb_populate_record(NULL::"AssessmentContext",to_jsonb(c)||jsonb_build_object('Id',{duplicate}))).* FROM "AssessmentContext" c WHERE "Id"={context.Id}""");throw new Exception("legacy NULL mapping allowed a duplicate context");}catch(Npgsql.PostgresException ex) when(ex.SqlState=="23505"){}
    await db.Students.Where(x=>x.Id==s.Id).ExecuteDeleteAsync();Assert(!await db.Set<AssessmentContext>().AnyAsync() && !await db.Evidence.AnyAsync(),"student deletion retained context or evidence");Console.WriteLine("PASS 数据库拒绝跨世代上下文；学生删除清除独立上下文与新旧证据");return;
}
if(args[0]=="student-audit-legacy")
{
    await db.GetService<IMigrator>().MigrateAsync("20261002095324_ParentBurdenRecords");var f=new Family();var s=new Student{FamilyId=f.Id,Name="有实际来源的学生"};var p=new Plan{FamilyId=f.Id,StudentId=s.Id,Date=new DateOnly(2026,10,2)};db.AddRange(f,s,p);await db.SaveChangesAsync();
    async Task<Guid> OldAudit(string action,string detail){var id=Guid.NewGuid();var actor=Guid.NewGuid();var time=DateTimeOffset.UtcNow;await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "Audits" ("Id","FamilyId","ActorId","Action","Details","CreatedAt") VALUES ({id},{f.Id},{actor},{action},{detail},{time})""");return id;}
    var transition=await OldAudit("TaskTransition",Json.Write(new TaskTransitionDetails(s.Id,Guid.NewGuid(),"Ready","Skipped","原跳过说明")));var adjustment=await OldAudit("PlanAdjusted",Json.Write(new PlanAdjustmentDetails(p.Id,Guid.NewGuid(),"原调整说明",[],[],[],[],[])));
    var generic=await OldAudit("POST:legacy",Content.Hash("原事件"));var orphan=await OldAudit("TaskTransition",Json.Write(new TaskTransitionDetails(Guid.NewGuid(),Guid.NewGuid(),"Ready","Skipped","此前已删除学生的私人说明")));
    await db.Database.MigrateAsync();db.ChangeTracker.Clear();var audits=await db.Audits.ToListAsync();Assert(audits.Count==3 && audits.Single(a=>a.Id==transition).StudentId==s.Id && audits.Single(a=>a.Id==adjustment).StudentId==s.Id && audits.Single(a=>a.Id==generic).StudentId==null && !audits.Any(a=>a.Id==orphan),"audit scope inferred incorrectly or deleted notes retained");
    Assert(Json.Read<TaskTransitionDetails>(audits.Single(a=>a.Id==transition).Details).Reason=="原跳过说明","reason rewritten");Console.WriteLine("PASS 旧具体说明按真实学生/计划链接回填；泛化摘要保持未知，已失去所属学生的具体私人说明清除");
    var another=new Family();var other=new Student{FamilyId=another.Id,Name="其他家庭"};db.AddRange(another,other);await db.SaveChangesAsync();try{await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Audits" SET "StudentId"={other.Id} WHERE "Id"={transition}""");throw new Exception("cross-family audit accepted");}catch(DbException){}
    await db.Students.Where(x=>x.Id==s.Id).ExecuteDeleteAsync();Assert(await db.Audits.CountAsync()==1 && (await db.Audits.SingleAsync()).Id==generic,"student deletion left private audit notes or erased unrelated generic audit");Console.WriteLine("PASS 数据库拒绝跨家庭审计；删除学生清除具体说明，普通家庭摘要保留");return;
}
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
    await db.Database.MigrateAsync();var family=new Family();var catalog=Content.Fixture();var q=catalog.Questions[0];var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};db.AddRange(family,release);await db.SaveChangesAsync();await Publishing.Register(db,release);await db.SaveChangesAsync();
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
    db.AddRange(f,r,s,t,session,a,g,job);await db.SaveChangesAsync();await Publishing.Register(db,r);await db.SaveChangesAsync();Console.WriteLine("SEEDED");return;
}
if(args[0]=="revocation-seed")
{
    var s=await db.Students.SingleAsync();var a=await db.Attempts.SingleAsync();var old=await db.Gradings.SingleAsync();var batch=new CorrectionBatch{FamilyId=s.FamilyId,StudentId=s.Id,ReleaseId=s.ActiveReleaseId!.Value,Cause="Grading",AffectedAttemptIds=Json.Write(new[]{a.Id}),SourceGradingRevisionId=old.Id,Reason="一次性故障验收的明确判分更正",PreviewHash="fault-confirmed"};var g=new Grading{FamilyId=s.FamilyId,AttemptId=a.Id,Number=2,Result="Correct",Method="ParentConfirmed",Reason=batch.Reason,CorrectionBatchId=batch.Id};db.AddRange(batch,g,new Outbox{FamilyId=s.FamilyId,StudentId=s.Id,AttemptId=a.Id});await db.SaveChangesAsync();return;
}
if(args[0]=="revocation-recover")
{
    var pending=await db.Outbox.SingleAsync(o=>o.ProcessedAt==null);Assert(await ProjectionWorker.Consume(db,pending),"correction not processed");var rev=await db.Set<EvidenceRevocation>().SingleAsync();var old=await db.Evidence.SingleAsync(e=>e.Id==rev.EvidenceId);Assert(!old.Positive && old.Weight==1 && (await db.Set<CorrectionBatch>().SingleAsync()).Status=="Applied" && await db.Generations.CountAsync()==2 && await db.Set<AssessmentContext>().CountAsync()==2 && (await db.Masteries.SingleAsync(m=>m.GenerationId==rev.ReplacementGenerationId)).Alpha==3,"replacement or revocation did not commit together");Assert(!await ProjectionWorker.Consume(db,pending) && await db.Set<EvidenceRevocation>().CountAsync()==1,"duplicate correction duplicated revocation");Console.WriteLine("PASS 更正崩溃后重试一次提交撤销、替代证据、上下文与活动指针；重复消费不重复撤销，旧证据不改写");return;
}
if(args[0]=="crash")
{
    await using var crashing=Open(true);var pending=await crashing.Outbox.SingleAsync(o=>o.ProcessedAt==null);await ProjectionWorker.Consume(crashing,pending);throw new Exception("Should have been terminated before commit");
}
if(args[0]=="builder-ledger-usage")
{
    await db.Database.MigrateAsync();var family=new Family();var source=new Source{FamilyId=family.Id,Title="受控计费响应夹具",Text="先乘除后加减。",Hash="usage-fixture"};var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,InputHash="usage-input"};var chunk=new Chunk{FamilyId=family.Id,SourceId=source.Id,Locator="段落1",Text=source.Text};db.AddRange(family,source,run,chunk);await db.SaveChangesAsync();db.Add(new BuilderBudgetPolicy{FamilyId=family.Id,DailyCostLimit=1,PerCallCostLimit=.001m,DailyTokenLimit=1000,PerCallTokenLimit=100});await db.SaveChangesAsync();var fragments=new[]{new BuilderFragment(chunk.Id,chunk.Text)};var valid=await new MockBuilderCandidateProvider().Generate(new(fragments,BuilderProtocol.Schema),CancellationToken.None);
    await using(var transaction=await db.Database.BeginTransactionAsync())
    {
        await db.Lock(family.Id);var tracker=new BuilderCallTracking(db,run,1,new UsageFixtureProvider(new("{invalid",new(11,22,.000123m,"USD","Confirmed")),new(valid.Output,new(13,24,.000456m,"USD","Confirmed"))));var result=await BuilderProtocol.Run(tracker,fragments,new BuilderLimits(),CancellationToken.None);Assert(result.Calls==2 && result.Repaired,"fixture did not repair once");await transaction.RollbackAsync();
    }
    db.ChangeTracker.Clear();var calls=await db.Set<BuilderCall>().OrderBy(c=>c.CallNumber).ToArrayAsync();Assert(calls.Length==2 && calls[0].ChargedCost==.000123m && calls[0].InputTokens==11 && calls[1].ChargedCost==.000456m && calls[1].OutputTokens==24 && calls[1].Repair && calls.All(c=>c.Status=="Returned" && c.Currency=="USD" && c.BillingStatus=="Confirmed"),"returned simulated usage lost after source validation/transaction rollback");Console.WriteLine("PASS 受控计费响应夹具：首次无效结构与一次修复分别保存实际响应用量、六位小数费用及身份，原事务回滚不删除");
    var unknown=new BuilderCallTracking(db,run,2,new UsageFixtureProvider(new BuilderProviderResponse(valid.Output)));await unknown.Generate(new(fragments,BuilderProtocol.Schema),CancellationToken.None);var missing=await db.Set<BuilderCall>().SingleAsync(c=>c.AttemptNumber==2);Assert(missing.BillingStatus=="Unknown" && missing.ChargedCost==null && missing.InputTokens==null && missing.OutputTokens==null,"missing usage invented zero");Console.WriteLine("PASS 未提供用量的返回保持费用/Token未知，不补零");return;
}
if(args[0]=="builder-call-crash-seed")
{
    await db.Database.MigrateAsync();var family=new Family();var source=new Source{FamilyId=family.Id,Title="逐调用中断验收",Text="先乘除后加减。",Hash="crash-source"};var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,InputHash="crash-call-input"};db.AddRange(family,source,run,new Chunk{FamilyId=family.Id,SourceId=source.Id,Text=source.Text,Locator="段落1"});await db.SaveChangesAsync();return;
}
if(args[0]=="builder-call-crash"){await using var worker=Open(true);await Builder.ProcessOne(worker,CancellationToken.None);return;}
if(args[0]=="builder-call-started-crash")
{
    var run=await db.BuilderRuns.SingleAsync();var chunk=await db.Chunks.SingleAsync();await new BuilderCallTracking(db,run,1,new PauseBuilderProvider()).Generate(new([new(chunk.Id,chunk.Text)],BuilderProtocol.Schema),CancellationToken.None);return;
}
if(args[0]=="builder-call-started-recover")
{
    var priorCall=await db.Set<BuilderCall>().SingleAsync();Assert(priorCall.Status=="Started" && priorCall.BillingStatus=="Unknown" && priorCall.ChargedCost==null && priorCall.FinishedAt==null && priorCall.ElapsedMilliseconds==null,"unfinished call invented result or zero fee");await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();Assert(await db.Set<BuilderCall>().CountAsync()==2 && (await db.Set<BuilderCall>().SingleAsync(c=>c.Id==priorCall.Id)).Status=="Started" && !await db.Candidates.AnyAsync() && (await db.BuilderRuns.SingleAsync()).Error=="BUILDER_CONCURRENCY_LIMIT","unknown physical call released its slot automatically");var actor=new Account{FamilyId=priorCall.FamilyId,UserName="budget-crash-fixture-"+Guid.NewGuid()};db.Add(actor);await db.SaveChangesAsync();db.Add(new BuilderBudgetReconciliation{FamilyId=priorCall.FamilyId,CallId=priorCall.Id,ActorId=actor.Id,ChargedCost=0,Reason="隔离夹具已实际终止原进程",ReceiptReference="observed test worker SIGKILL"});var pending=await db.BuilderRuns.SingleAsync();pending.Status="Queued";pending.Error=null;pending.RetryRound++;await db.SaveChangesAsync();await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();Assert(await db.Set<BuilderCall>().CountAsync()==3 && (await db.Set<BuilderCall>().SingleAsync(c=>c.Id==priorCall.Id)).Status=="Started" && await db.Candidates.CountAsync()==1,"manual local termination reconciliation rewrote history or failed to release slot");Console.WriteLine("PASS 调用开始保存后实际终止进程，结果和费用保持未知；恢复新调用不抹掉旧缺口");return;
}
if(args[0]=="builder-call-crash-recover")
{
    Assert((await db.BuilderRuns.SingleAsync()).Status=="Queued" && !await db.Candidates.AnyAsync() && !await db.Set<BuilderAttempt>().AnyAsync(),"crashed candidate transaction partially committed");var priorCall=await db.Set<BuilderCall>().SingleAsync();Assert(priorCall.Status=="Returned" && priorCall.ChargedCost==0 && priorCall.BillingStatus=="LocalNoCharge" && priorCall.InputTokens==null,"actual pre-crash call lost or invented tokens");await Builder.ProcessOne(db,CancellationToken.None);db.ChangeTracker.Clear();var calls=await db.Set<BuilderCall>().ToArrayAsync();Assert(calls.Length==2 && calls.Select(c=>c.ExecutionId).Distinct().Count()==2 && calls.All(c=>c.CallNumber==1 && c.AttemptNumber==1 && c.Status=="Returned") && await db.Candidates.CountAsync()==1 && await db.Set<BuilderAttempt>().CountAsync()==1,"retry reused call identity or doubled candidates");Console.WriteLine("PASS 提交前实际终止进程后调用事实仍在；恢复新执行身份保留两个实际调用、只有一份候选及成功尝试");await db.Families.ExecuteDeleteAsync();Assert(!await db.Set<BuilderCall>().AnyAsync(),"family delete retained calls");Console.WriteLine("PASS 调用账本同家庭外键及删除闭合");return;
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
        Assert(await db.Set<BuilderCall>().CountAsync(c=>c.RunId==run.Id && c.Status=="Returned" && c.BillingStatus=="LocalNoCharge")==failure,"candidate rollback deleted actual call facts");
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
Assert(await db.Set<AssessmentContext>().CountAsync()==1 && (await db.Set<AssessmentContext>().SingleAsync()).ActivationStatus=="Active" && (await db.Evidence.SingleAsync()).ContextId==(await db.Set<AssessmentContext>().SingleAsync()).Id,"contexts did not converge atomically");
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

sealed class PauseBuilderProvider:IBuilderCandidateProvider
{
    public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
    public async Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct){Console.WriteLine("CALL_STARTED");Console.Out.Flush();await Task.Delay(Timeout.Infinite,ct);return new("{}");}
}

sealed class UsageFixtureProvider(params BuilderProviderResponse[] responses):IBuilderCandidateProvider
{
    public BuilderQuote Quote(BuilderProviderRequest request)=>new(.001m,100,"USD");
    int number;public Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(responses[number++]);}
}
