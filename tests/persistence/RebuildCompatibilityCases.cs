using Learning;
using Microsoft.EntityFrameworkCore;
public static class RebuildCompatibilityCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static async Task Run(Database db,string mode)
    {
        var request=await AssessmentRebuildCases.Seed(db);var actor=new Actor(request.RequestedBy,request.FamilyId,request.RequestedBy,null,"Parent","Parent");
        var originalJob=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==request.JobId);originalJob.LeaseSeconds=120;originalJob.HeartbeatSeconds=30;await db.SaveChangesAsync();
        if(mode=="rebuild-compat-api-seed")
        {
            var job=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==request.JobId);job.NextRunAt=DateTimeOffset.UtcNow.AddHours(1);await db.SaveChangesAsync();
            Console.WriteLine(Json.Write(new{userName=(await db.Accounts.SingleAsync(a=>a.Id==request.RequestedBy)).UserName,studentId=request.StudentId,requestId=request.Id,jobId=request.JobId}));return;
        }
        async Task<string> Bytes()
        {
            await db.Database.OpenConnectionAsync();using var cmd=db.Database.GetDbConnection().CreateCommand();
            var tables=new[]{"AssessmentRebuildRequest","AssessmentRebuildResult","BackgroundJob","JobLeaseAttempt","Generation","AssessmentCheckpoint","Evidence","AssessmentContext","Mastery","Review","Outbox","ConsumerReceipt","AssessmentConsumerCursor","DomainEvent","Audit","Student","Attempt","Grading","StudyTask"};
            cmd.CommandText="SELECT jsonb_build_object("+string.Join(",",tables.Select(t=>"'"+t+"',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r.\"Id\") FROM \""+db.Model.GetEntityTypes().Single(e=>e.ClrType.Name==t).GetTableName()+"\" r)"))+")::text";
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        async Task Refused(string status)
        {
            db.ChangeTracker.Clear();var before=await Bytes();
            await using(var tx=await db.Database.BeginTransactionAsync())
            {
                await db.Lock(request.FamilyId);var student=await db.Students.SingleAsync(s=>s.Id==request.StudentId);
                try{await AssessmentRebuildJobs.RequireLegacyCompatible(db,student);await Assessment.Rebuild(db,student);throw new Exception("legacy rebuild bypassed "+status);}
                catch(ApiError e){Check(e.Status==409 && e.Code=="REBUILD_JOB_REQUIRED","wrong legacy conflict");}
                await tx.RollbackAsync();
            }
            Check(await Bytes()==before,"refused old route changed original rows or produced results");
        }
        await Refused("Queued");
        await using(var lease=await BackgroundJobs.Claim(db,types:[AssessmentRebuildJobs.Type],inputRef:request.Id)??throw new Exception("actual rebuild claim absent"))
        {
            // Keep the real heartbeat outside this short byte-comparison window.
            await Refused("Running");db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(request.FamilyId);await lease.Finish(db,"Retrying","CONTROLLED_TRANSIENT_FAILURE",DateTimeOffset.UtcNow.AddHours(1),CancellationToken.None);await tx.CommitAsync();
        }
        await Refused("Retrying");
        db.ChangeTracker.Clear();await using(var tx=await db.Database.BeginTransactionAsync()){await JobCancellation.Cancel(db,actor,request.JobId,"明确取消原可选重建");await db.SaveChangesAsync();await tx.CommitAsync();}
        await Refused("Cancelled");Console.WriteLine("PASS 旧重建入口在Queued/真实Running/受控Retrying/真实Cancelled均409；19表全部原字段保持，零旁路重算");
        async Task<AssessmentRebuildRequest> Prepare(string reason)
        {
            db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(request.FamilyId);var next=await AssessmentRebuildJobs.Enqueue(db,actor,request.StudentId,reason);await db.SaveChangesAsync();await tx.CommitAsync();return next;
        }
        var failed=await Prepare("明确重新准备，随后受控撤销家长权限");await db.Set<FamilyMembership>().Where(m=>m.FamilyId==request.FamilyId).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,""));await AssessmentRebuildJobs.ProcessOne(db);db.ChangeTracker.Clear();Check((await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==failed.JobId)).LastErrorCode=="JOB_REQUESTER_FORBIDDEN","actual worker did not reject revoked requester");await db.Set<FamilyMembership>().Where(m=>m.FamilyId==request.FamilyId).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,"Parent"));await Refused("Failed");
        var completed=await Prepare("明确核对权限并重新准备后台请求");await AssessmentRebuildJobs.ProcessOne(db);db.ChangeTracker.Clear();Check((await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==completed.JobId)).Status=="Succeeded","explicit new request did not complete");var original=await Bytes();
        await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(request.FamilyId);var student=await db.Students.SingleAsync(s=>s.Id==request.StudentId);await AssessmentRebuildJobs.RequireLegacyCompatible(db,student);await Assessment.Rebuild(db,student);await tx.CommitAsync();}
        Check(await Bytes()==original,"completed unchanged legacy input altered original result");Console.WriteLine("PASS 实际权限失败仍不能借旧入口重算；明确新请求经真实后台完成后，同输入兼容复用，原取消/失败/领取保留");
        var other=new Student{FamilyId=request.FamilyId};db.Add(other);await db.SaveChangesAsync();await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(request.FamilyId);await AssessmentRebuildJobs.RequireLegacyCompatible(db,other);await Assessment.Rebuild(db,other);await tx.CommitAsync();}Check(other.ActiveGenerationId!=null,"independent student compatibility incorrectly blocked");Console.WriteLine("PASS 同家庭另一学生没有后台请求时仍可按原契约重建，不继承他人的停止状态");
    }
}
