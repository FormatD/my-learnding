using Learning;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
public static class CheckpointDeltaCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static async Task Run(Database db)
    {
        await db.Database.MigrateAsync();var family=new Family();var student=new Student{FamilyId=family.Id};var gen=new Generation{FamilyId=family.Id,StudentId=student.Id,InputHash=new string('a',64)};db.AddRange(family,student,gen);await db.SaveChangesAsync();
        var engine=new IncrementalAssessment(family.Id,student.Id,gen.Id,"Asia/Shanghai");var skill=new KC(Guid.NewGuid(),Guid.NewGuid(),"DELTA","混合运算","独立计算","括号");var stamp=new DateTimeOffset(2026,1,1,8,0,0,TimeSpan.Zero);var sequence=0;
        void Append()
        {
            var n=++sequence;var q=new Question(Guid.NewGuid(),Guid.NewGuid(),"(24−8)÷4","4","先算括号","Numeric","Medium","SingleKC",[new(skill.Id)]);var session=new LearningSession{FamilyId=family.Id,StudentId=student.Id,QuestionId=q.Id};var attempt=new Attempt{FamilyId=family.Id,StudentId=student.Id,SessionId=session.Id,Number=1,Sequence=n,CreatedAt=stamp.AddMinutes(n)};engine.Append(new(attempt,session,q,new Grading{FamilyId=family.Id,AttemptId=attempt.Id,Result=n%3==0?"Incorrect":"Correct"},[skill],new StudyTask{FamilyId=family.Id,StudentId=student.Id}));
        }
        for(var i=0;i<100;i++)Append();var initial=engine.Freeze();var prior=new AssessmentCheckpoint{FamilyId=family.Id,StudentId=student.Id,GenerationId=gen.Id,EngineVersion=IncrementalAssessment.Version,InputCount=sequence,PrefixHash=new string('b',64),Payload=initial,PayloadHash=Content.Hash(initial)};db.Add(prior);await db.SaveChangesAsync();var original=Json.Write(prior);var fullBytes=(long)System.Text.Encoding.UTF8.GetByteCount(initial);var storedBytes=fullBytes;var deltas=0;var bases=0;var snapshots=new List<string>{initial};AssessmentCheckpoint? deepest=null;string? deepestComplete=null;
        for(var i=0;i<40;i++)
        {
            var before=await AssessmentCheckpointPayloads.Read(db,student,prior,default);Check(before==engine.Freeze(),"previous canonical state changed");Append();if(i==7){Append();Append();}var complete=engine.Freeze();snapshots.Add(complete);
            var next=AssessmentCheckpoints.Save(db,student,gen,new(engine,"OnlineAppend",sequence-(int)prior.InputCount,null,new string('c',64),prior,before));await db.SaveChangesAsync();db.ChangeTracker.Clear();
            Check(await AssessmentCheckpointPayloads.Read(db,student,next,default)==complete,"bounded chain did not restore exact complete snapshot");Check(IncrementalAssessment.Resume(complete).Freeze()==IncrementalAssessment.Resume(await AssessmentCheckpointPayloads.Read(db,student,next,default)).Freeze(),"restored typed engine differs");
            if(next.DeltaDepth>0){deltas++;Check(next.BaseCheckpointId==prior.Id && next.DeltaDepth==(prior.DeltaDepth??0)+1 && next.Payload.Length<complete.Length,"delta base or size wrong");}else{bases++;Check(next.BaseCheckpointId==null,"complete reset retains base");}
            if(next.DeltaDepth==31){deepest=next;deepestComplete=complete;}
            fullBytes+=System.Text.Encoding.UTF8.GetByteCount(complete);storedBytes+=System.Text.Encoding.UTF8.GetByteCount(next.Payload);prior=next;
        }
        Check(deltas==39 && bases==1 && prior.DeltaDepth==8,$"31-segment boundary did not reset: deltas={deltas} bases={bases} depth={prior.DeltaDepth}");Check(Json.Write(await db.Set<AssessmentCheckpoint>().SingleAsync(c=>c.InputCount==100))==original,"legacy row backfilled or rewritten");
        await using var connection=new NpgsqlConnection(db.Database.GetConnectionString());await connection.OpenAsync();await using(var create=new NpgsqlCommand("CREATE TEMP TABLE checkpoint_full_baseline(payload text)",connection))await create.ExecuteNonQueryAsync();
        foreach(var payload in snapshots){await using var insert=new NpgsqlCommand("INSERT INTO checkpoint_full_baseline VALUES (@payload)",connection);insert.Parameters.AddWithValue("payload",payload);await insert.ExecuteNonQueryAsync();}
        long physicalFull,physicalDelta;await using(var query=new NpgsqlCommand("SELECT sum(pg_column_size(payload))::bigint FROM checkpoint_full_baseline",connection))physicalFull=(long)(await query.ExecuteScalarAsync())!;await using(var query=new NpgsqlCommand("SELECT sum(pg_column_size(\"Payload\"))::bigint FROM \"AssessmentCheckpoint\"",connection))physicalDelta=(long)(await query.ExecuteScalarAsync())!;
        Check(physicalDelta<physicalFull,"PostgreSQL stored columns did not shrink");Console.WriteLine($"CHECKPOINT_DELTA_STORAGE syntheticSnapshots={snapshots.Count} fullLogicalBytes={fullBytes} deltaLogicalBytes={storedBytes} fullStoredColumnBytes={physicalFull} deltaStoredColumnBytes={physicalDelta} deltas={deltas} completeResets={bases}");
        var baseline=new AssessmentCheckpoint{Id=deepest!.Id,FamilyId=family.Id,StudentId=student.Id,GenerationId=gen.Id,EngineVersion=IncrementalAssessment.Version,InputCount=deepest.InputCount,Payload=deepestComplete!,PayloadHash=Content.Hash(deepestComplete!),StorageVersion=AssessmentCheckpointPayloads.Version,DeltaDepth=0,StatePayloadHash=Content.Hash(deepestComplete!)};
        _=await AssessmentCheckpointPayloads.Read(db,student,deepest,default);_=await AssessmentCheckpointPayloads.Read(db,student,baseline,default);
        async Task<double> Measure(AssessmentCheckpoint row){var timer=System.Diagnostics.Stopwatch.StartNew();for(var i=0;i<3;i++)Check(await AssessmentCheckpointPayloads.Read(db,student,row,default)==deepestComplete,"measured restore changed state");return timer.Elapsed.TotalMilliseconds/3;}
        Console.WriteLine($"CHECKPOINT_DELTA_RESTORE syntheticInputs={deepest.InputCount} depth=31 iterations=3 fullMeanMs={await Measure(baseline):F2} deltaMeanMs={await Measure(deepest):F2}");
        async Task Reject(AssessmentCheckpoint bad){try{await AssessmentCheckpointPayloads.Read(db,student,bad,default);throw new Exception("corrupt chain accepted");}catch(ApiError error){Check(error.Code=="INCREMENTAL_STATE_INVALID","wrong corrupt-chain error");}}
        var latest=await db.Set<AssessmentCheckpoint>().AsNoTracking().SingleAsync(c=>c.Id==prior.Id);var savedHash=latest.PayloadHash;latest.PayloadHash=new string('0',64);await Reject(latest);latest.PayloadHash=savedHash;latest.StatePayloadHash=new string('0',64);await Reject(latest);latest.StatePayloadHash=prior.StatePayloadHash;latest.DeltaDepth=1;await Reject(latest);latest.DeltaDepth=prior.DeltaDepth;latest.BaseCheckpointId=Guid.NewGuid();await Reject(latest);latest.BaseCheckpointId=prior.BaseCheckpointId;latest.StorageVersion="future";await Reject(latest);
        try{await db.Set<AssessmentCheckpoint>().Where(c=>c.InputCount==100).ExecuteDeleteAsync();throw new Exception("base-only deletion accepted");}catch(PostgresException ex){Check(ex.SqlState=="23503","wrong base protection");}
        var other=new Generation{FamilyId=family.Id,StudentId=student.Id,InputHash=new string('d',64)};db.Add(other);await db.SaveChangesAsync();var foreign=new AssessmentCheckpoint{FamilyId=family.Id,StudentId=student.Id,GenerationId=other.Id,EngineVersion=IncrementalAssessment.Version,InputCount=999,PrefixHash=new string('e',64),Payload="{}",PayloadHash=Content.Hash("{}"),StorageVersion=AssessmentCheckpointPayloads.Version,DeltaDepth=1,BaseCheckpointId=prior.Id,StatePayloadHash=new string('f',64)};db.Add(foreign);try{await db.SaveChangesAsync();throw new Exception("cross-generation base accepted");}catch(DbUpdateException ex)when(ex.InnerException is PostgresException pg){Check(pg.SqlState=="23503","wrong foreign base protection");}db.ChangeTracker.Clear();
        try{await db.GetService<IMigrator>().MigrateAsync("20261003220215_ReviewTargetConfirmations");throw new Exception("delta storage silently downgraded");}catch(PostgresException ex){Check(ex.SqlState=="P0001","wrong downgrade refusal");}
        Check((await db.Database.GetAppliedMigrationsAsync()).Contains("20261004172710_BoundedCheckpointDeltas") && await db.Set<AssessmentCheckpoint>().CountAsync()==41,"refused downgrade changed history");
        await db.Students.Where(s=>s.Id==student.Id).ExecuteDeleteAsync();Check(!await db.Set<AssessmentCheckpoint>().AnyAsync() && !await db.Generations.AnyAsync(),"privacy deletion left chains");
        Console.WriteLine("PASS 旧格式原样、批量追加逐字恢复、31段重置、损坏摘要/深度/缺失/未知格式拒绝、跨世代基线外键、单删基线保护、含差量时拒绝降级与整学生清理");
    }
}
