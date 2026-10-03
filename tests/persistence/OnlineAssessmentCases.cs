using Learning;
using Microsoft.EntityFrameworkCore;
public static class OnlineAssessmentCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static async Task Run(Database db,string mode)
    {
        if(mode=="online-crash")
        {
            await using var crash=new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(new CrashBeforeCommit()).Options);
            await ProjectionWorker.ProcessOne(crash);return;
        }
        if(mode=="online-recover")
        {
            await using var a=BackgroundJobs.Open(db);await using var b=BackgroundJobs.Open(db);await Task.WhenAll(ProjectionWorker.ProcessOne(a),ProjectionWorker.ProcessOne(b));db.ChangeTracker.Clear();
            var g=await db.Generations.SingleAsync();Check(g.Status=="Active" && g.CalculationMode=="OnlineAppend" && g.ProcessedInputCount==1 && g.IncrementalBaseGenerationId==null,"online recovery did not append original generation");
            Check(await db.Set<AssessmentCheckpoint>().CountAsync()==2 && await db.Evidence.CountAsync()==3 && await db.Set<AssessmentContext>().CountAsync()==3 && await db.Set<ConsumerReceipt>().CountAsync()==3,"online recovery lost or duplicated state/rows/receipts");
            Check(await db.Set<JobLeaseAttempt>().CountAsync(x=>x.Status=="LeaseExpired")==1 && await db.Set<JobLeaseAttempt>().CountAsync(x=>x.Status=="Succeeded")==2,"online recovery lost real attempt history");
            Console.WriteLine("PASS 真正中断后双消费者只追加一次，同一活动世代/3条唯一上下文证据/2份不可修改状态/3张历史回执，原到期领取保留");return;
        }
        await db.Database.MigrateAsync();var family=new Family();var account=new Account{FamilyId=family.Id,UserName="online-"+Guid.NewGuid()};var catalog=Content.Fixture();var skill=catalog.Kcs[0];var group=Guid.NewGuid();catalog=catalog with{Questions=catalog.Questions.Take(3).Select(q=>q with{Mappings=[new(skill.Id)],Policy="SingleKC",Difficulty="Medium",VariantGroupId=group}).ToArray()};
        var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var student=new Student{FamilyId=family.Id,ActiveReleaseId=release.Id};db.AddRange(family,account,new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles="Parent"},release,student);await db.SaveChangesAsync();await Publishing.Register(db,release);await db.SaveChangesAsync();
        var attempts=new List<Attempt>();var events=new List<Outbox>();var stamp=new DateTimeOffset(2026,1,1,8,0,0,TimeSpan.Zero);
        async Task Append(int index)
        {
            var question=catalog.Questions[index];var task=new StudyTask{FamilyId=family.Id,StudentId=student.Id,ReleaseId=release.Id,QuestionId=question.Id};var session=new LearningSession{FamilyId=family.Id,StudentId=student.Id,TaskId=task.Id,ReleaseId=release.Id,QuestionId=question.Id};var attempt=new Attempt{FamilyId=family.Id,StudentId=student.Id,SessionId=session.Id,Number=1,ClientSubmissionId=Guid.NewGuid(),CreatedAt=stamp.AddMinutes(index)};var ev=new Outbox{FamilyId=family.Id,StudentId=student.Id,AttemptId=attempt.Id};db.AddRange(task,session,attempt,new Grading{FamilyId=family.Id,AttemptId=attempt.Id,Number=1,Result="Correct"},ev);await db.SaveChangesAsync();attempts.Add(attempt);events.Add(ev);
        }
        await Append(0);await Append(1);Check(await ProjectionWorker.Consume(db,events[0]),"initial consumption failed");db.ChangeTracker.Clear();var original=await db.Generations.SingleAsync();var originalHash=original.InputHash;var first=(await db.Set<AssessmentCheckpoint>().SingleAsync());var frozen=Json.Write(first);var oldEvidence=Json.Write(await db.Evidence.OrderBy(e=>e.Id).ToArrayAsync());var oldContexts=Json.Write(await db.Set<AssessmentContext>().OrderBy(c=>c.Id).ToArrayAsync());var oldReceipts=Json.Write(await db.Set<ConsumerReceipt>().OrderBy(r=>r.Id).ToArrayAsync());
        AssessmentRebuildResult? parentResult=null;string? parentBytes=null;
        if(mode=="online-checkpoints")
        {
            await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);await AssessmentRebuildJobs.Enqueue(db,new Actor(Guid.NewGuid(),family.Id,account.Id,null,"Parent","Parent"),student.Id,"核对追加前原结果");await db.SaveChangesAsync();await tx.CommitAsync();}
            await AssessmentRebuildJobs.ProcessOne(db);db.ChangeTracker.Clear();parentResult=await db.Set<AssessmentRebuildResult>().SingleAsync();parentBytes=Json.Write(parentResult);Check(parentResult.CheckpointId==first.Id && parentResult.ReusedGeneration,"parent same-input result not bound to actual checkpoint");
        }
        // Normal completion changes workflow metadata, not the mathematical prefix or fixed references.
        await db.Tasks.Where(t=>t.StudentId==student.Id).ExecuteUpdateAsync(u=>u.SetProperty(t=>t.Status,"Completed"));
        await Append(2);await ProjectionJobs.Ensure(db);
        if(mode=="online-missing-prefix")
        {
            var missing=await db.Evidence.OrderBy(e=>e.OccurredAt).FirstAsync();await db.Evidence.Where(e=>e.Id==missing.Id).ExecuteDeleteAsync();db.ChangeTracker.Clear();
            try{await ProjectionWorker.Consume(db,events[2]);throw new Exception("missing immutable prefix evidence silently recreated");}catch(ApiError error){Check(error.Code=="INCREMENTAL_STATE_INVALID","wrong missing-prefix error");}
            db.ChangeTracker.Clear();Check(await db.Generations.CountAsync()==1 && (await db.Generations.SingleAsync()).InputHash==originalHash && await db.Set<AssessmentCheckpoint>().CountAsync()==1 && Json.Write(await db.Set<AssessmentCheckpoint>().SingleAsync())==frozen && await db.Evidence.CountAsync()==1 && await db.Set<AssessmentContext>().CountAsync()==2 && await db.Set<ConsumerReceipt>().CountAsync()==2,"missing-prefix rejection committed or recreated history");
            Check((await db.Outbox.SingleAsync(o=>o.Id==events[2].Id)).Error=="INCREMENTAL_STATE_INVALID","real queue error missing");await db.Students.Where(s=>s.Id==student.Id).ExecuteDeleteAsync();Console.WriteLine("PASS 受控删除一条历史Evidence后明确拒绝追加，保留原状态/世代且不重新插入历史证据或提交新回执");return;
        }
        if(mode=="online-seed")
        {
            await db.Set<BackgroundJob>().Where(j=>j.Status=="Queued").ExecuteUpdateAsync(u=>u.SetProperty(j=>j.LeaseSeconds,3).SetProperty(j=>j.HeartbeatSeconds,1));
            // The second coalesced event already has its real receipt; no new work may be invented.
            Check(!await ProjectionWorker.Consume(db,events[1]),"coalesced event unexpectedly executed");return;
        }
        Check(await ProjectionWorker.Consume(db,events[2]),"suffix consumption failed");db.ChangeTracker.Clear();var current=await db.Generations.SingleAsync();var checkpoints=await db.Set<AssessmentCheckpoint>().OrderBy(c=>c.InputCount).ToArrayAsync();
        Check(current.Id==original.Id && current.CalculationMode=="OnlineAppend" && current.ProcessedInputCount==1 && current.InputHash!=originalHash && checkpoints.Length==2,"suffix replaced generation or recomputed prefix");
        Check(Json.Write(checkpoints[0])==frozen && checkpoints[1].InputHash==current.InputHash && checkpoints[1].Cursor==current.Cursor && checkpoints[1].ProcessedInputCount==1,"historical state overwritten or current state metadata missing");
        Check(Json.Write(await db.Evidence.Where(e=>e.AttemptId!=attempts[2].Id).OrderBy(e=>e.Id).ToArrayAsync())==oldEvidence && Json.Write(await db.Set<AssessmentContext>().Where(c=>c.AttemptId!=attempts[2].Id).OrderBy(c=>c.Id).ToArrayAsync())==oldContexts,"prefix evidence/context bytes changed");
        Check(Json.Write(await db.Set<ConsumerReceipt>().Where(r=>r.EventId!=events[2].Id).OrderBy(r=>r.Id).ToArrayAsync())==oldReceipts,"historical receipts changed");
        var evidence=await db.Evidence.OrderBy(e=>e.OccurredAt).ToArrayAsync();Check(evidence.Select(e=>e.Weight).SequenceEqual(new[]{1m,.8m,.2m}) && evidence.Select(e=>e.ContextId).Distinct().Count()==3,"online variant cap or context identity incorrect");var mastery=await db.Masteries.SingleAsync();Check(mastery.Alpha==4m && mastery.Beta==2m,"online mastery double counted prefix");
        var receipt=await db.Set<ConsumerReceipt>().SingleAsync(r=>r.EventId==events[2].Id);Check(receipt.CheckpointId==checkpoints[1].Id && receipt.GenerationId==original.Id,"suffix receipt not bound to actual historical checkpoint");
        Check(!await ProjectionWorker.Consume(db,events[2]),"repeat executed");db.ChangeTracker.Clear();Check(await db.Generations.CountAsync()==1 && await db.Set<AssessmentCheckpoint>().CountAsync()==2 && await db.Evidence.CountAsync()==3,"duplicate fabricated rows");
        // Repair an old marker only; generation hash now differs from its immutable receipt hash.
        var old=await db.Outbox.SingleAsync(o=>o.Id==events[0].Id);old.ProcessedAt=null;old.RetryRound=1;await db.SaveChangesAsync();Check(!await ProjectionWorker.Consume(db,old),"old receipt invalidated by generation advancement");db.ChangeTracker.Clear();Check(await db.Set<ConsumerReceipt>().CountAsync()==3 && await db.Set<AssessmentCheckpoint>().CountAsync()==2,"repair rewrote past calculation");
        Console.WriteLine("PASS 实际在线消费只在原活动世代追加1条后缀，前缀证据/上下文/状态/回执原样；限额/掌握准确，重复与旧标记修复均不重算");
        if(parentResult!=null)
        {
            Check(await AssessmentCheckpoints.ValidateResult(db,parentResult,default),"actual parent result invalidated after online advancement");
            await db.Set<BackgroundJob>().Where(j=>j.Id==parentResult.JobId).ExecuteUpdateAsync(u=>u.SetProperty(j=>j.Status,"Queued"));
            await AssessmentRebuildJobs.ProcessOne(db);db.ChangeTracker.Clear();Check(Json.Write(await db.Set<AssessmentRebuildResult>().SingleAsync())==parentBytes && (await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==parentResult.JobId)).Status=="Succeeded" && await db.Set<AssessmentCheckpoint>().CountAsync()==2,"completed-result recovery changed history or used current hash");
        }
        // A parent result remains verifiable after later online advancement, via its actual immutable state.
        var historical=new AssessmentRebuildResult{FamilyId=family.Id,StudentId=student.Id,GenerationId=original.Id,CheckpointId=first.Id,InputHash=first.InputHash!,Cursor=first.Cursor!.Value};Check(await AssessmentCheckpoints.ValidateResult(db,historical,default),"historical result validation incorrectly uses mutable generation hash");historical.InputHash=current.InputHash;Check(!await AssessmentCheckpoints.ValidateResult(db,historical,default),"result bound to wrong checkpoint accepted");
        var grade=await db.Gradings.SingleAsync(g=>g.AttemptId==attempts[0].Id);var correction=new CorrectionBatch{FamilyId=family.Id,StudentId=student.Id,ReleaseId=release.Id,Cause="Grading",AffectedAttemptIds=Json.Write(new[]{attempts[0].Id}),SourceGradingRevisionId=grade.Id,Reason="更正在线限额",ConfirmedBy=account.Id};var corrected=new Outbox{FamilyId=family.Id,StudentId=student.Id,AttemptId=attempts[0].Id};db.AddRange(correction,new Grading{FamilyId=family.Id,AttemptId=attempts[0].Id,Number=2,Result="Incorrect",CorrectionBatchId=correction.Id,Method="ParentConfirmed",GradedBy=account.Id},corrected);await db.SaveChangesAsync();Check(await ProjectionWorker.Consume(db,corrected),"correction failed");db.ChangeTracker.Clear();var active=await db.Generations.SingleAsync(g=>g.Status=="Active");Check(active.Id!=original.Id && active.CalculationMode=="FullStreamChangedPrefix" && active.ProcessedInputCount==3 && await db.Generations.CountAsync()==2,"correction must rebase to fixed new generation");Check((await db.Masteries.SingleAsync(m=>m.GenerationId==active.Id)).Beta==3m && (await db.Evidence.Where(e=>e.GenerationId==active.Id).OrderBy(e=>e.OccurredAt).ToArrayAsync()).Select(e=>e.Weight).SequenceEqual(new[]{1m,.8m,.8m}),"corrected downstream cap stale");Check(await db.Set<EvidenceRevocation>().AnyAsync(r=>r.ReplacementGenerationId==active.Id && r.CorrectionBatchId==correction.Id),"actual correction revocation missing");Console.WriteLine("PASS 更正仍沿固定新目标完整重算、释放下游限额和撤销；历史结果指向原状态而不是变化中的当前摘要");
        var wrongEvent=new Outbox{FamilyId=family.Id,StudentId=student.Id,AttemptId=attempts[0].Id};db.AddRange(wrongEvent,new ConsumerReceipt{FamilyId=family.Id,StudentId=student.Id,GenerationId=active.Id,CheckpointId=first.Id,EventId=wrongEvent.Id,JobId=receipt.JobId,InputHash=active.InputHash});
        try{await db.SaveChangesAsync();throw new Exception("cross-generation checkpoint reference accepted");}catch(DbUpdateException ex){Check(ex.InnerException is Npgsql.PostgresException error && error.SqlState=="23503","wrong state ownership rejection");db.ChangeTracker.Clear();}
        await db.Students.Where(s=>s.Id==student.Id).ExecuteDeleteAsync();Check(!await db.Set<AssessmentCheckpoint>().AnyAsync() && !await db.Set<ConsumerReceipt>().AnyAsync() && !await db.Generations.AnyAsync(),"private append state chain not deleted");Console.WriteLine("PASS 数据库拒绝跨世代状态引用，学生删除清理追加状态与历史回执/世代");
    }
}
