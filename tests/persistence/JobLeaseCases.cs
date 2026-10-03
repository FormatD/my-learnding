using Learning;
using Microsoft.EntityFrameworkCore;
public static class JobLeaseCases
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task Run(Database db)
    {
        await db.Database.MigrateAsync();var family=new Family();db.Add(family);await db.SaveChangesAsync();
        BackgroundJob Job()=>new(){FamilyId=family.Id,Type="LeaseFixture",InputRef=Guid.NewGuid(),IdempotencyKey=Guid.NewGuid().ToString(),InputPayload="{}",InputHash=Content.Hash("{}"),LeaseSeconds=3,HeartbeatSeconds=1};
        var race=Job();db.Add(race);await db.SaveChangesAsync();
        var claimed=await Task.WhenAll(BackgroundJobs.Claim(db,types:["LeaseFixture"]),BackgroundJobs.Claim(db,types:["LeaseFixture"]));
        Check(claimed.Count(l=>l!=null)==1,"two connections claimed the same job");await using(var lease=claimed.Single(l=>l!=null)!)
        {
            var original=lease.Job.HeartbeatAt;await Task.Delay(4200);await using var competing=await BackgroundJobs.Claim(db,types:["LeaseFixture"]);Check(competing==null,"active heartbeat did not protect lease");
            var row=await db.Set<BackgroundJob>().AsNoTracking().SingleAsync(j=>j.Id==race.Id);Check(row.HeartbeatAt>original && row.LeaseExpiresAt>DateTimeOffset.UtcNow,"heartbeat not persisted");
            await using var tx=await db.Database.BeginTransactionAsync();db.Add(new Audit{FamilyId=family.Id,Action="JobLeaseAtomicResult"});await lease.Finish(db,"Succeeded",null,null,CancellationToken.None);await tx.CommitAsync();
        }
        db.ChangeTracker.Clear();Check((await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==race.Id)).Status=="Succeeded" && await db.Set<JobLeaseAttempt>().CountAsync(a=>a.JobId==race.Id && a.Status=="Succeeded")==1,"lease receipt and result did not commit together");Console.WriteLine("PASS 两真实连接仅一个领取；心跳跨完整租约周期续期，完成回执与结果事务提交");
        var abandoned=Job();abandoned.Status="Running";abandoned.AttemptCount=1;abandoned.MaxAttempts=2;abandoned.LeaseOwner=Guid.NewGuid().ToString();abandoned.HeartbeatAt=DateTimeOffset.UtcNow.AddSeconds(-10);abandoned.LeaseExpiresAt=DateTimeOffset.UtcNow.AddSeconds(-5);db.AddRange(abandoned,new JobLeaseAttempt{FamilyId=family.Id,JobId=abandoned.Id,LeaseOwner=abandoned.LeaseOwner,Number=1,StartedAt=abandoned.HeartbeatAt.Value});await db.SaveChangesAsync();
        await using(var old=new JobLease(db,abandoned,true,CancellationToken.None))
        await using(var current=await BackgroundJobs.Claim(db,types:["LeaseFixture"]))
        {
            Check(current!=null && current.Job.LeaseOwner!=abandoned.LeaseOwner && current.Job.AttemptCount==2,"expired owner not replaced");
            await using(var staleTx=await db.Database.BeginTransactionAsync())
            {
                db.Add(new Audit{FamilyId=family.Id,Action="StaleJobMustNotCommit"});
                await db.SaveChangesAsync();
                try{await old.Finish(db,"Succeeded",null,null,CancellationToken.None);throw new Exception("stale owner committed");}catch(OperationCanceledException){await staleTx.RollbackAsync();}
            }
            db.ChangeTracker.Clear();Check(!await db.Audits.AnyAsync(a=>a.Action=="StaleJobMustNotCommit"),"stale result survived");
            await using var tx=await db.Database.BeginTransactionAsync();await current!.Finish(db,"Succeeded",null,null,CancellationToken.None);await tx.CommitAsync();
        }
        Check(await db.Set<JobLeaseAttempt>().CountAsync(a=>a.JobId==abandoned.Id && a.Status=="LeaseExpired")==1,"expired owner history missing");Console.WriteLine("PASS 到期领取使用新标记，旧领取人提交被围栏拒绝且结果回滚，旧租约历史保留");
        var exhausted=Job();exhausted.MaxAttempts=1;exhausted.AttemptCount=1;exhausted.Status="Running";exhausted.LeaseOwner=Guid.NewGuid().ToString();exhausted.HeartbeatAt=DateTimeOffset.UtcNow.AddSeconds(-10);exhausted.LeaseExpiresAt=DateTimeOffset.UtcNow.AddSeconds(-5);db.AddRange(exhausted,new JobLeaseAttempt{FamilyId=family.Id,JobId=exhausted.Id,LeaseOwner=exhausted.LeaseOwner,Number=1,StartedAt=exhausted.HeartbeatAt.Value});await db.SaveChangesAsync();
        await using(var lease=await BackgroundJobs.Claim(db,types:["LeaseFixture"]))
        {
            Check(lease!=null && !lease.CanExecute && lease.Job.AttemptCount==1,"exhausted crash job allowed another execution");await using var tx=await db.Database.BeginTransactionAsync();await lease!.Finish(db,"Failed","JOB_ATTEMPTS_EXHAUSTED",null,CancellationToken.None);await tx.CommitAsync();
        }
        Check(await BackgroundJobs.Claim(db,types:["LeaseFixture"])==null,"terminal failed job reclaimed automatically");Console.WriteLine("PASS 崩溃耗尽领取上限后只完成失败清理，不再执行，终态不自动重领");
        var pending=Job();pending.Status="Retrying";pending.NextRunAt=DateTimeOffset.UtcNow.AddMinutes(1);var cancelled=Job();cancelled.Status="Cancelled";db.AddRange(pending,cancelled);await db.SaveChangesAsync();Check(await BackgroundJobs.Claim(db,types:["LeaseFixture"])==null,"future retry or cancelled job executed");Console.WriteLine("PASS 未到退避时间和Cancelled均不能领取");
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BackgroundJob\" SET \"Status\"='Running' WHERE \"Id\"={cancelled.Id}");throw new Exception("database accepted ownerless running job");}
            catch(Npgsql.PostgresException e){Check(e.SqlState=="23514","wrong constraint failure");await tx.RollbackAsync();}
        }
        var duplicate=Job();duplicate.InputRef=race.InputRef;db.Add(duplicate);
        try{await db.SaveChangesAsync();throw new Exception("duplicate physical job accepted");}catch(DbUpdateException e){Check(e.InnerException is Npgsql.PostgresException p && p.SqlState=="23505","job uniqueness not enforced in database");db.ChangeTracker.Clear();}
        Console.WriteLine("PASS 数据库约束拒绝缺领取人的Running与重复任务，不只依赖应用先查");
    }
}
