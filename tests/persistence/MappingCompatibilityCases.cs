using Learning;
using Microsoft.EntityFrameworkCore;
public static class MappingCompatibilityCases
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static async Task Run(Database db,string mode)
    {
        await db.Database.MigrateAsync();var fixtures=new List<object>();
        foreach(var provider in new[]{"Mock","Manual"})
        {
            var family=new Family();var user=(Environment.GetEnvironmentVariable("MAPPING_COMPAT_USER")??"mapping-compat-"+Guid.NewGuid())+provider.ToLowerInvariant();var account=new Account{FamilyId=family.Id,UserName=user,PasswordHash=Security.Password(Environment.GetEnvironmentVariable("MAPPING_COMPAT_PASSWORD")??"isolated-compat-fixture-password")};var membership=new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles="Parent,ContentEditor"};var catalog=Content.Fixture();var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var draft=new ContentDraft{FamilyId=family.Id,Title="映射旧入口围栏验收",Payload=Json.Write(catalog)};db.AddRange(family,account,membership,release,draft);await db.SaveChangesAsync();await Publishing.Register(db,release);await db.SaveChangesAsync();
            var actor=new Actor(account.Id,family.Id,account.Id,null,"Parent",membership.Roles);var input=new MappingRunInput(draft.Id,release.Id,[new("Question",catalog.Questions[0].Id,catalog.Questions[0].RevisionId)],provider);MappingPreparation prepared;
            await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);prepared=await MappingJobs.Enqueue(db,actor,input);if(mode=="mapping-compat-api-seed")db.Set<BackgroundJob>().Local.Single(j=>j.Id==prepared.JobId).NextRunAt=DateTimeOffset.UtcNow.AddHours(1);await db.SaveChangesAsync();await tx.CommitAsync();}
            if(mode=="mapping-compat-api-seed"){fixtures.Add(new{userName=user,familyId=family.Id,prepared.Id,prepared.JobId,prepared.RunId,input});continue;}
            async Task Refused(string status)
            {
                db.ChangeTracker.Clear();string Bytes(BackgroundJob job){var node=System.Text.Json.Nodes.JsonNode.Parse(Json.Write(job))!.AsObject();if(job.Status=="Running"){node.Remove("heartbeatAt");node.Remove("leaseExpiresAt");}return node.ToJsonString();}
                var before=Bytes(await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==prepared.JobId));var savedRuns=Json.Write(await db.Set<MappingRun>().Where(r=>r.FamilyId==family.Id).OrderBy(r=>r.Id).ToArrayAsync());var savedSuggestions=Json.Write(await db.Set<MappingSuggestion>().Where(s=>s.FamilyId==family.Id).OrderBy(s=>s.Id).ToArrayAsync());var history=Json.Write(await db.Audits.Where(a=>a.FamilyId==family.Id).OrderBy(a=>a.Id).ToArrayAsync());
                await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);try{await MappingBuilder.PrepareLegacy(db,actor,input);throw new Exception("legacy generation bypassed "+status);}catch(ApiError error){Check(error.Code=="MAPPING_JOB_REQUIRED" && error.Status==409,"wrong compatibility conflict");}await tx.RollbackAsync();}
                db.ChangeTracker.Clear();var job=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==prepared.JobId);Check(job.Status==status && Bytes(job)==before && Json.Write(await db.Set<MappingRun>().Where(r=>r.FamilyId==family.Id).OrderBy(r=>r.Id).ToArrayAsync())==savedRuns && Json.Write(await db.Set<MappingSuggestion>().Where(s=>s.FamilyId==family.Id).OrderBy(s=>s.Id).ToArrayAsync())==savedSuggestions && Json.Write(await db.Audits.Where(a=>a.FamilyId==family.Id).OrderBy(a=>a.Id).ToArrayAsync())==history,"refused legacy call changed original work or produced results");
            }
            await Refused("Queued");
            await using(var lease=await BackgroundJobs.Claim(db,types:[MappingJobs.Type],inputRef:prepared.Id)??throw new Exception("real mapping claim absent"))
            {
                await Refused("Running");db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(family.Id);await lease.Finish(db,"Retrying","CONTROLLED_TRANSIENT_FAILURE",DateTimeOffset.UtcNow.AddHours(1),CancellationToken.None);await tx.CommitAsync();
            }
            await Refused("Retrying");db.ChangeTracker.Clear();await using(var tx=await db.Database.BeginTransactionAsync()){await JobCancellation.Cancel(db,actor,prepared.JobId,"真实取消尚未完成的原准备请求");await db.SaveChangesAsync();await tx.CommitAsync();}await Refused("Cancelled");
            db.ChangeTracker.Clear();await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);await MappingJobs.Retry(db,actor,prepared.Id,"明确恢复原取消输入，继续经后台领取");await db.SaveChangesAsync();await tx.CommitAsync();}
            await MappingJobs.ProcessOne(db);db.ChangeTracker.Clear();var actual=await db.Set<MappingRun>().SingleAsync(r=>r.Id==prepared.RunId);var completedBytes=Json.Write(actual);var suggestionBytes=Json.Write(await db.Set<MappingSuggestion>().Where(s=>s.RunId==actual.Id).ToArrayAsync());var jobBytes=Json.Write(await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==prepared.JobId));
            await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);var reused=await MappingBuilder.PrepareLegacy(db,actor,input);Check(!reused.Created && reused.Run.Id==prepared.RunId,"actual completed output not reused");await db.SaveChangesAsync();await tx.CommitAsync();}
            db.ChangeTracker.Clear();Check(Json.Write(await db.Set<MappingRun>().SingleAsync(r=>r.Id==prepared.RunId))==completedBytes && Json.Write(await db.Set<MappingSuggestion>().Where(s=>s.RunId==prepared.RunId).ToArrayAsync())==suggestionBytes && Json.Write(await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==prepared.JobId))==jobBytes,"completed reuse changed original output or job facts");
            Check(await db.Set<JobLeaseAttempt>().CountAsync(a=>a.JobId==prepared.JobId && a.Status=="Succeeded")==1 && await db.Set<JobLeaseAttempt>().CountAsync(a=>a.JobId==prepared.JobId && a.Status=="Retrying")==1 && (await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==prepared.JobId)).RetryRound==1,"explicit recovery lost real lease/cancellation history");Console.WriteLine("PASS "+provider+" 旧入口在Queued/真实Running/Retrying/真实Cancelled均409且无输出/状态/审计改写；明确恢复后实际后台固定结果只提交一次，旧入口仅复用原完成字节");
            // A distinct input still has its own legitimate synchronous compatibility result.
            draft=await db.Drafts.SingleAsync(d=>d.Id==draft.Id);draft.Version++;draft.Title="独立新输入";await db.SaveChangesAsync();MappingRun fresh;
            await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);var result=await MappingBuilder.PrepareLegacy(db,actor,input);fresh=result.Run;Check(result.Created && fresh.Id!=prepared.RunId,"independent legacy input incorrectly blocked");await db.SaveChangesAsync();await tx.CommitAsync();}
            draft.Version++;await db.SaveChangesAsync();MappingPreparation failed;
            await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);failed=await MappingJobs.Enqueue(db,actor,input);await db.SaveChangesAsync();await tx.CommitAsync();}
            await db.Set<FamilyMembership>().Where(m=>m.Id==membership.Id).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,"Parent"));await MappingJobs.ProcessOne(db);db.ChangeTracker.Clear();Check((await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==failed.JobId)).Status=="Failed" && !await db.Set<MappingRun>().AnyAsync(r=>r.Id==failed.RunId),"revoked permission did not actually fail queued job");await db.Set<FamilyMembership>().Where(m=>m.Id==membership.Id).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,"Parent,ContentEditor"));
            prepared=failed;await Refused("Failed");await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(family.Id);await MappingJobs.Retry(db,actor,failed.Id,"实际失败后明确核对原输入与本轮权限");await db.SaveChangesAsync();await tx.CommitAsync();}await MappingJobs.ProcessOne(db);db.ChangeTracker.Clear();Check((await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==failed.JobId)).Status=="Succeeded" && await db.Set<MappingRun>().CountAsync(r=>r.Id==failed.RunId)==1,"failed fixed request did not recover through real worker");Console.WriteLine("PASS "+provider+" 独立新输入保持原同步兼容；实际权限失败的原输入不能借旧入口生成，需明确本轮恢复授权后经后台固定结果提交");
            await db.Families.Where(f=>f.Id==family.Id).ExecuteDeleteAsync();Check(!await db.Set<MappingPreparation>().AnyAsync(p=>p.FamilyId==family.Id) && !await db.Set<MappingRun>().AnyAsync(r=>r.FamilyId==family.Id),"compatibility result provenance retained after family deletion");
        }
        if(mode=="mapping-compat-api-seed")Console.WriteLine(Json.Write(fixtures));
    }
}
