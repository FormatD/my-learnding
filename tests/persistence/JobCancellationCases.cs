using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
public static class JobCancellationCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static async Task Run(Database db,string mode)
    {
        if(mode=="cancel-worker-seed")
        {
            await db.Database.MigrateAsync();var f=new Family();var account=new Account{FamilyId=f.Id,UserName="cancel-"+Guid.NewGuid()};var source=new Source{FamilyId=f.Id,Title="受控取消任务",Text="先乘除后加减。",Hash="cancel-source"};var run=new BuilderRun{FamilyId=f.Id,SourceId=source.Id,InputHash="cancel-worker-input"};db.AddRange(f,account,new FamilyMembership{FamilyId=f.Id,AccountId=account.Id,Roles="Parent,ContentEditor"},source,run,new Chunk{FamilyId=f.Id,SourceId=source.Id,Text=source.Text,Locator="段落1"});await db.SaveChangesAsync();await BackgroundJobs.EnsureBuilderJobs(db);await db.Set<BackgroundJob>().ExecuteUpdateAsync(u=>u.SetProperty(j=>j.LeaseSeconds,3).SetProperty(j=>j.HeartbeatSeconds,1));return;
        }
        if(mode=="cancel-worker")
        {
            await using var worker=new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(new PauseCandidateResult()).Options);await Builder.ProcessOne(worker,CancellationToken.None);await BackgroundJobs.EnsureBuilderJobs(db);Check(!await db.Candidates.AnyAsync(),"cancelled builder committed candidates");return;
        }
        var job=await db.Set<BackgroundJob>().AsNoTracking().SingleAsync();var accountRow=await db.Accounts.SingleAsync();var actor=new Actor(Guid.NewGuid(),job.FamilyId,accountRow.Id,null,"Parent","Parent,ContentEditor");
        if(mode=="cancel-worker-request")
        {
            await using var tx=await db.Database.BeginTransactionAsync();var watch=System.Diagnostics.Stopwatch.StartNew();var result=await JobCancellation.Cancel(db,actor,job.Id,"受控真实执行器处理期间取消");await db.SaveChangesAsync();await tx.CommitAsync();Check(watch.Elapsed<TimeSpan.FromSeconds(2) && result.Status=="Cancelled" && !result.ExecutionStopConfirmed,"cancel blocked on worker family lock or invented stop");return;
        }
        Check(job.Status=="Cancelled" && !await db.Candidates.AnyAsync() && (await db.BuilderRuns.SingleAsync()).Status=="Cancelled" && (await db.BuilderRuns.SingleAsync()).CompletedAt==null,"cancelled output/run state inconsistent");Check(await db.Set<JobLeaseAttempt>().CountAsync(a=>a.Status=="Cancelled" && a.FinishedAt!=null)==1 && await db.Audits.CountAsync(a=>a.Action=="BackgroundJobCancellationObserved")==1,"actual stop not acknowledged");Check(await BackgroundJobs.Claim(db)==null,"cancelled job reclaimed");var calls=await db.Set<BuilderCall>().AsNoTracking().ToArrayAsync();Check(calls.Length==1 && calls[0].Status=="Returned" && calls[0].BillingStatus=="LocalNoCharge","durable returned call erased");
        await using(var tx=await db.Database.BeginTransactionAsync()){var again=await JobCancellation.Cancel(db,actor,job.Id,"重复核对取消");Check(again.AlreadyCancelled && again.ExecutionStopConfirmed,"repeat cancellation changed state");await db.SaveChangesAsync();await tx.CommitAsync();}Check(await db.Audits.CountAsync(a=>a.Action=="BackgroundJobCancellationRequested")==1,"repeated cancellation added fact");
        var priorOwner=(await db.Set<JobLeaseAttempt>().SingleAsync()).LeaseOwner;var staleJob=await db.Set<BackgroundJob>().AsNoTracking().SingleAsync();staleJob.LeaseOwner=priorOwner;
        await using(var stale=new JobLease(db,staleJob,true,CancellationToken.None))
        {
            try{await BuilderBudget.Reserve(db,new BuilderCall{FamilyId=job.FamilyId,RunId=job.InputRef,ExecutionId=Guid.NewGuid(),AttemptNumber=2,CallNumber=1},BuilderQuote.Local,CancellationToken.None,stale);throw new Exception("cancelled lease started another call");}catch(OperationCanceledException){Check(stale.Lost && await db.Set<BuilderCall>().CountAsync()==1,"revoked lease preserved no-call fence incorrectly");}
        }
        // Controlled outstanding usage fixture, no external request or paid service.
        var persistedRun=await db.BuilderRuns.SingleAsync();var policy=await db.Set<BuilderBudgetPolicy>().SingleAsync();policy.DailyCostLimit=1;policy.PerCallCostLimit=.1m;policy.DailyTokenLimit=100;policy.PerCallTokenLimit=50;policy.MaxConcurrentCalls=2;await db.SaveChangesAsync();var unknown=new BuilderCall{FamilyId=job.FamilyId,RunId=persistedRun.Id,ExecutionId=Guid.NewGuid(),AttemptNumber=2,CallNumber=1};Check(await BuilderBudget.Reserve(db,unknown,new(.05m,20,"USD"),CancellationToken.None)==null,"unknown fixture not reserved");var original=Json.Write(await db.Set<BuilderCall>().AsNoTracking().SingleAsync(c=>c.Id==unknown.Id));await using(var tx=await db.Database.BeginTransactionAsync()){await JobCancellation.Cancel(db,actor,job.Id,"受控未知费用保留核对");await tx.CommitAsync();}var budget=await BuilderBudget.State(db,job.FamilyId,DateOnly.FromDateTime(DateTime.UtcNow));Check(original==Json.Write(await db.Set<BuilderCall>().AsNoTracking().SingleAsync(c=>c.Id==unknown.Id)) && budget.ActiveCalls==1 && budget.CostCommitted==.05m && budget.TokensCommitted==20,"cancellation released unknown physical call");
        // Controlled new human round mirrors the persisted run state; the HTTP retry is covered separately.
        var previousCallCount=await db.Set<BuilderCall>().CountAsync();persistedRun.Status="Queued";persistedRun.RetryRound++;persistedRun.Error=null;await db.SaveChangesAsync();await BackgroundJobs.EnsureBuilderJobs(db);db.ChangeTracker.Clear();var resumed=await db.Set<BackgroundJob>().SingleAsync();Check(resumed.Status=="Queued" && resumed.RetryRound==1 && resumed.Id==job.Id && resumed.InputHash==job.InputHash && (await db.BuilderRuns.SingleAsync()).Status=="Queued" && await db.Set<BuilderCall>().CountAsync()==previousCallCount,"old cancellation prevented an explicit new retry round or rewrote call facts");
        Console.WriteLine("PASS 真实建库处理中取消不等家庭锁，心跳停止执行且输出回滚，实际停止另确认；重复取消不增记录、未确认用量保留额度");
        await db.Families.Where(f=>f.Id==job.FamilyId).ExecuteDeleteAsync();Check(!await db.Set<BackgroundJob>().AnyAsync() && !await db.Audits.AnyAsync() && !await db.Set<BuilderCall>().AnyAsync(),"family deletion left cancellation private records");Console.WriteLine("PASS 家庭删除清理取消依据、领取与调用记录");
    }
    sealed class PauseCandidateResult:SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken ct=default)
        {
            if(data.Context!.ChangeTracker.Entries<Candidate>().Any()){Console.WriteLine("CANCEL_READY");Console.Out.Flush();await Task.Delay(Timeout.Infinite,ct);}return result;
        }
    }
}
