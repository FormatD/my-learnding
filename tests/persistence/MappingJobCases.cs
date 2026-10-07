using Learning;
using Microsoft.EntityFrameworkCore;
public static class MappingJobCases
{
    static void Check(bool value,string error){if(!value)throw new Exception(error);}
    static Database Open(Database db,bool crash=false){var b=new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString());if(crash)b.AddInterceptors(new CrashBeforeCommit());return new(b.Options);}
    static async Task<MappingPreparation> Seed(Database db,string provider)
    {
        await db.Database.MigrateAsync();var f=new Family();var account=new Account{FamilyId=f.Id,UserName="mapping-jobs-"+Guid.NewGuid()};var catalog=Content.Fixture();catalog=catalog with{Lessons=catalog.Lessons.Select(l=>l with{RevisionId=Guid.NewGuid()}).ToArray(),Resources=catalog.Resources.Select(r=>r with{RevisionId=Guid.NewGuid()}).ToArray()};var draft=new ContentDraft{FamilyId=f.Id,Title="后台映射冻结验收",Payload=Json.Write(catalog)};var release=new Release{FamilyId=f.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};db.AddRange(f,account,draft,release,new FamilyMembership{FamilyId=f.Id,AccountId=account.Id,Roles="Parent,ContentEditor"});await db.SaveChangesAsync();await Publishing.Register(db,release);await db.SaveChangesAsync();
        var selection=new MappingOwnerSelection[]{new("Question",catalog.Questions[0].Id,catalog.Questions[0].RevisionId),new("Lesson",catalog.Lessons[0].Id,catalog.Lessons[0].RevisionId!.Value),new("Resource",catalog.Resources[0].Id,catalog.Resources[0].RevisionId!.Value)};
        var actor=new Actor(Guid.NewGuid(),f.Id,account.Id,null,"Parent","Parent,ContentEditor");var input=new MappingRunInput(draft.Id,release.Id,selection,provider);
        MappingPreparation p;
        await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(f.Id);p=await MappingJobs.Enqueue(db,actor,input);await db.SaveChangesAsync();var same=await MappingJobs.Enqueue(db,actor,input);Check(same.Id==p.Id,"duplicate selection queued twice");await tx.CommitAsync();}
        Check(!await db.Set<MappingRun>().AnyAsync(r=>r.FamilyId==f.Id) && !await db.Set<MappingSuggestion>().AnyAsync(r=>r.FamilyId==f.Id),"queue produced pretend completed output");
        draft.Version++;draft.Title="排队后另存标题";var changed=catalog with{Questions=catalog.Questions.Select(q=>q with{Stem="排队后修改，不应换入"}).ToArray()};draft.Payload=Json.Write(changed);await db.SaveChangesAsync();
        var job=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId);job.LeaseSeconds=3;job.HeartbeatSeconds=1;await db.SaveChangesAsync();return p;
    }
    public static async Task Run(Database db,string mode)
    {
        if(mode=="mapping-job-seed"){await Seed(db,"Mock");Console.WriteLine("SEEDED");return;}
        if(mode=="mapping-job-crash"){await using var crash=Open(db,true);await MappingJobs.ProcessOne(crash);return;}
        if(mode=="mapping-job-recover")
        {
            var p=await db.Set<MappingPreparation>().SingleAsync();await using var a=Open(db);await using var b=Open(db);await Task.WhenAll(MappingJobs.ProcessOne(a),MappingJobs.ProcessOne(b));await Verify(db,p);Check(await db.Set<JobLeaseAttempt>().CountAsync(j=>j.Status=="LeaseExpired")==1,"old crash claim not retained");Console.WriteLine("PASS 真正终止后等待数据库租约到期，双消费者只有一组固定输出、旧领取保留");return;
        }
        await Revalidation(db);
        foreach(var provider in new[]{"Mock","Manual"}){var p=await Seed(db,provider);await MappingJobs.ProcessOne(db);db.ChangeTracker.Clear();await Verify(db,p);var run=await db.Set<MappingRun>().SingleAsync(r=>r.Id==p.RunId);var bytes=Json.Write(await db.Set<MappingSuggestion>().Where(s=>s.RunId==p.RunId).OrderBy(s=>s.Id).ToArrayAsync());await MappingJobs.ProcessOne(db);Check(Json.Write(await db.Set<MappingSuggestion>().Where(s=>s.RunId==p.RunId).OrderBy(s=>s.Id).ToArrayAsync())==bytes,"duplicate worker changed suggestions");
            try{await db.Set<MappingPreparation>().Where(x=>x.Id==p.Id).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.Snapshot,"{}"));throw new Exception("fixed snapshot changed");}catch(Npgsql.PostgresException ex){Check(ex.SqlState=="23514","snapshot immutable guard wrong");}
            Check(provider!="Manual" || run.Model=="None","manual called model");await db.Families.Where(f=>f.Id==p.FamilyId).ExecuteDeleteAsync();Check(!await db.Set<MappingPreparation>().AnyAsync(x=>x.FamilyId==p.FamilyId) && !await db.Set<BackgroundJob>().AnyAsync(x=>x.FamilyId==p.FamilyId),"family deletion retained snapshot/job");}
        var forbidden=await Seed(db,"Mock");await db.Set<FamilyMembership>().Where(m=>m.FamilyId==forbidden.FamilyId).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,"Parent"));await MappingJobs.ProcessOne(db);db.ChangeTracker.Clear();var denied=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==forbidden.JobId);Check(denied.Status=="Failed" && denied.LastErrorCode=="JOB_REQUESTER_FORBIDDEN" && !await db.Set<MappingRun>().AnyAsync(r=>r.FamilyId==forbidden.FamilyId),"revoked role still generated suggestions");var authorized=new Account{FamilyId=forbidden.FamilyId,UserName="mapping-recover-"+Guid.NewGuid()};db.AddRange(authorized,new FamilyMembership{FamilyId=forbidden.FamilyId,AccountId=authorized.Id,Roles="ContentEditor"});denied.Status="Queued";denied.RetryRound=1;denied.AttemptCount=0;denied.LastErrorCode=null;db.Add(new Audit{FamilyId=forbidden.FamilyId,ActorId=authorized.Id,Action="MappingPreparationManualRetry",Details=Json.Write(new{preparationId=forbidden.Id,jobId=denied.Id,retryRound=1,runId=forbidden.RunId,snapshotHash=forbidden.SnapshotHash,reason="受控有权限成员确认原输入恢复夹具"})});await db.SaveChangesAsync();await MappingJobs.ProcessOne(db);await Verify(db,forbidden);Check((await db.Set<MappingPreparation>().SingleAsync(p=>p.Id==forbidden.Id)).RequestedBy!=authorized.Id,"recovery overwrote original requester");Console.WriteLine("PASS 受控授权恢复沿原输入与固定输出，原请求人不改，执行授权另留实际审计");await db.Families.Where(f=>f.Id==forbidden.FamilyId).ExecuteDeleteAsync();Console.WriteLine("PASS 排队后撤销维护权限明确停止，零建议输出、不自动绕过权限重试");
        Console.WriteLine("PASS 模拟与手工后台排队零假完成、重复提交复用、后改草稿不换输入、一次提交固定结果与领取、重复消费不改输出");Console.WriteLine("PASS 固定输入数据库拒绝修改，家庭删除清理快照/任务/领取与输出");
    }
    // Two real short transactions with an intervening independent write; no model completion is fabricated.
    static async Task Revalidation(Database db)
    {
        foreach(var change in new[]{"Role","Withdrawal"})
        {
            var p=await Seed(db,"Mock");
            await using(var lease=await BackgroundJobs.Claim(db,types:[MappingJobs.Type],inputRef:p.Id)??throw new Exception("revalidation lease missing"))
            {
                await using(var tx=await db.Database.BeginTransactionAsync(lease.Token))
                {
                    await db.Lock(p.FamilyId,lease.Token);var captured=await MappingJobs.ValidateFrozen(db,lease,lease.Token);
                    Check(captured.Preparation.Id==p.Id && captured.Input.SourcePayload==Json.Read<MappingFrozenInput>(p.Snapshot).SourcePayload,"capture changed frozen input");
                    await tx.CommitAsync(lease.Token);
                }
                // Keep the original tracker populated to detect accidental reuse of a stale tracked Release.
                await using(var writer=Open(db))
                {
                    if(change=="Role")await writer.Set<FamilyMembership>().Where(m=>m.FamilyId==p.FamilyId).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,"Parent"));
                    else await writer.Releases.Where(r=>r.Id==p.LibraryReleaseId).ExecuteUpdateAsync(u=>u.SetProperty(r=>r.Withdrawn,true));
                }
                await using(var tx=await db.Database.BeginTransactionAsync(lease.Token))
                {
                    await db.Lock(p.FamilyId,lease.Token);var expected=change=="Role"?"JOB_REQUESTER_FORBIDDEN":"LIBRARY_WITHDRAWN";
                    try{await MappingJobs.ValidateFrozen(db,lease,lease.Token);throw new Exception("second capture accepted changed authorization/library");}
                    catch(ApiError error){Check(error.Code==expected,"second capture returned wrong rejection");}
                    await lease.Finish(db,"Failed",expected,null,lease.Token);await tx.CommitAsync(lease.Token);
                }
            }
            Check(!await db.Set<MappingRun>().AnyAsync(r=>r.FamilyId==p.FamilyId) && !await db.Set<MappingModelCall>().AnyAsync(r=>r.FamilyId==p.FamilyId),"revalidation fixture fabricated model/output");
            await db.Families.Where(f=>f.Id==p.FamilyId).ExecuteDeleteAsync();db.ChangeTracker.Clear();
        }
        Console.WriteLine("PASS 两次短事务间真实撤权/撤库重新拒绝，旧跟踪缓存不覆盖当前状态，零模型调用或建议输出");
    }
    static async Task Verify(Database db,MappingPreparation p)
    {
        db.ChangeTracker.Clear();var job=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId);var run=await db.Set<MappingRun>().SingleOrDefaultAsync(r=>r.Id==p.RunId);var f=Json.Read<MappingFrozenInput>(p.Snapshot);Check(job.Status=="Succeeded" && run!=null && run.SourcePayload==f.SourcePayload && run.SourceDraftVersion==f.SourceDraftVersion && run.InputHash==p.InputHash && run.SourceTitle==p.SourceTitle,"fixed snapshot/result not preserved");Check(await db.Set<MappingSuggestion>().CountAsync(s=>s.RunId==p.RunId)==3 && await db.Set<JobLeaseAttempt>().CountAsync(a=>a.JobId==p.JobId && a.Status=="Succeeded")==1,"partial/duplicate result or missing committed claim");Check(!await db.Set<MappingReviewDecision>().AnyAsync() && !await db.Set<MappingSetRevision>().AnyAsync(s=>s.ReviewDecisionId!=null),"suggestion published itself");
    }
}
