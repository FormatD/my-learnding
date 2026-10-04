using Learning;
using System.Text.Json.Nodes;
public static class IncrementalAssessmentCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    static readonly Guid Family=Guid.NewGuid(),Student=Guid.NewGuid(),Generation=Guid.NewGuid();
    static readonly DateTimeOffset Origin=new(2026,1,1,8,0,0,TimeSpan.Zero);
    static readonly KC Skill=new(Guid.NewGuid(),Guid.NewGuid(),"INC","测试能力","独立计算","仅测试");
    static AssessmentInput Input(int sequence,int day=0,string result="Correct",Guid? question=null,int hint=0,string type="Practice",Guid? variant=null)
    {
        var q=new Question(question??Guid.NewGuid(),Guid.NewGuid(),"2×2","4","4","Numeric","Medium","SingleKC",[new(Skill.Id)],VariantGroupId:variant);
        var session=new LearningSession{FamilyId=Family,StudentId=Student,QuestionId=q.Id,ReleaseId=Guid.NewGuid()};var attempt=new Attempt{FamilyId=Family,StudentId=Student,SessionId=session.Id,Sequence=sequence,Number=1,HintLevel=hint,CreatedAt=Origin.AddDays(day).AddMinutes(sequence)};
        return new(attempt,session,q,new Grading{FamilyId=Family,AttemptId=attempt.Id,Result=result},[Skill],new StudyTask{FamilyId=Family,StudentId=Student,Type=type});
    }
    static string Canonical(AssessmentOutput output)
    {
        var contexts=output.Contexts.ToDictionary(c=>c.Id,c=>c.AttemptId.ToString());foreach(var e in output.Evidence)Check(contexts.TryGetValue(e.ContextId!.Value,out var a) && a==e.AttemptId.ToString(),"incremental evidence lost context link");
        JsonNode Row<T>(T row){var n=JsonNode.Parse(Json.Write(row))!;n.AsObject().Remove("id");n.AsObject().Remove("createdAt");return n;}
        var evidence=output.Evidence.OrderBy(e=>e.AttemptId).ThenBy(e=>e.KCId).ThenBy(e=>e.Part).ThenBy(e=>e.Positive).Select(e=>{var n=Row(e);n["contextId"]=contexts[e.ContextId!.Value];return n;});
        return Json.Write(new{evidence=evidence.ToArray(),contexts=output.Contexts.OrderBy(c=>c.AttemptId).Select(Row).ToArray(),masteries=output.Masteries.OrderBy(m=>m.KCId).Select(Row).ToArray(),reviews=output.Reviews.OrderBy(r=>r.TargetType).ThenBy(r=>r.TargetId).Select(Row).ToArray(),output.ReviewHistory,output.MasteryHistory});
    }
    static IncrementalAssessment Compare(AssessmentInput[] input,TeachingAnchor[]? teaching=null,string zone="Asia/Shanghai",int restartEvery=1)
    {
        var engine=new IncrementalAssessment(Family,Student,Generation,zone,teaching);var ordered=input.OrderBy(i=>i.Attempt.Sequence).ThenBy(i=>i.Attempt.Id).ToArray();
        for(var i=0;i<ordered.Length;i++)
        {
            engine.Append(ordered[i]);Check(engine.ProcessedInputs==i+1,"old prefix was processed again");
            var reference=Assessment.Replay(Family,Student,Generation,zone,ordered.Take(i+1),teaching,collectReviewHistory:true,collectMasteryHistory:true);Check(Canonical(reference)==Canonical(engine.Output()),$"stream/full mismatch at input {i+1}");
            if((i+1)%restartEvery==0){var frozen=engine.Freeze();engine=IncrementalAssessment.Resume(frozen);Check(Canonical(reference)==Canonical(engine.Output()),"persisted state changed prior projection");}
        }
        return engine;
    }
    // Keep the prior Fork's serialization path as an independent comparison.
    static IncrementalAssessment LegacyFork(string payload,Guid target)
    {
        var decoded=IncrementalAssessment.Resume(payload);var snapshot=Json.Read<IncrementalAssessmentSnapshot>(decoded.Freeze());var now=DateTimeOffset.UtcNow;var ids=snapshot.Contexts.ToDictionary(c=>c.Id,_=>Guid.NewGuid());
        foreach(var c in snapshot.Contexts){c.Id=ids[c.Id];c.GenerationId=target;c.CreatedAt=now;}foreach(var e in snapshot.Evidence){e.Id=Guid.NewGuid();e.GenerationId=target;e.CreatedAt=now;e.ContextId=ids[e.ContextId!.Value];}foreach(var skill in snapshot.Skills){skill.Value.Id=Guid.NewGuid();skill.Value.GenerationId=target;skill.Value.CreatedAt=now;}foreach(var r in snapshot.Reviews){r.Id=Guid.NewGuid();r.GenerationId=target;r.CreatedAt=now;}return IncrementalAssessment.Resume(Json.Write(snapshot with{GenerationId=target}));
    }
    public static void Run()
    {
        var mastery=Enumerable.Range(1,10).Select(n=>Input(n)).Append(Input(11,8,type:"Review")).Append(Input(12,9,"Incorrect")).Append(Input(13,10)).Append(Input(14,11)).ToArray();var e=Compare(mastery);Check(e.Output().Masteries.Single().Status=="Mastered" && !e.Output().Masteries.Single().NeedsRecheck,"independent diagnostic gold expectation failed");
        var stable=Compare(mastery.Take(11).Append(Input(12,38,type:"Review")).ToArray());Check(stable.Output().Masteries.Single().Status=="Stable","30-day stable gold failed");
        var twice=Compare(mastery.Take(12).Append(Input(13,10,"Incorrect")).ToArray());Check(twice.Output().Masteries.Single().Status=="Learning","two recent failures gold failed");
        var forkId=Guid.NewGuid();var fork=IncrementalAssessment.Fork(e.Freeze(),forkId);Check(Canonical(fork.Output())==Canonical(Assessment.Replay(Family,Student,forkId,"Asia/Shanghai",mastery,collectReviewHistory:true,collectMasteryHistory:true)),"forked projection differs from full replay");
        var originalPayload=e.Freeze();var oldFork=LegacyFork(originalPayload,forkId);Check(Canonical(oldFork.Output())==Canonical(fork.Output()),"new fork differs from prior serialization path");Check(originalPayload==e.Freeze(),"fork changed source engine");var originalSnapshot=Json.Read<IncrementalAssessmentSnapshot>(originalPayload);var forkSnapshot=Json.Read<IncrementalAssessmentSnapshot>(fork.Freeze());Check(forkSnapshot.Contexts.All(c=>!originalSnapshot.Contexts.Any(old=>old.Id==c.Id)) && forkSnapshot.Evidence.All(c=>!originalSnapshot.Evidence.Any(old=>old.Id==c.Id)),"fork reused original persistence identities");
        var forkNext=Input(15,12,"Incorrect");fork.Append(forkNext);Check(originalPayload==e.Freeze(),"appending to fork changed source state");Check(Canonical(fork.Output())==Canonical(Assessment.Replay(Family,Student,forkId,"Asia/Shanghai",mastery.Append(forkNext),collectReviewHistory:true,collectMasteryHistory:true)),"forked append differs from full replay");
        foreach(var property in new[]{"contextId","attemptId","gradingId"}){var damaged=JsonNode.Parse(originalPayload)!;damaged["evidence"]![0]![property]=Guid.NewGuid().ToString();try{IncrementalAssessment.Resume(damaged.ToJsonString());throw new Exception("broken composite evidence link accepted");}catch(ApiError x){Check(x.Code=="INCREMENTAL_STATE_INVALID","wrong corrupt link rejection");}}
        var nullLink=JsonNode.Parse(originalPayload)!;nullLink["evidence"]![0]!["contextId"]=null;try{IncrementalAssessment.Resume(nullLink.ToJsonString());throw new Exception("null evidence link accepted");}catch(ApiError x){Check(x.Code=="INCREMENTAL_STATE_INVALID","wrong null link rejection");}
        var q=Guid.NewGuid();var review=Compare([Input(1,0,"Incorrect",q),Input(2,2,question:q,type:"Review"),Input(3,7,question:q,type:"Review"),Input(4,30,question:q,type:"Review")]);Check(review.Output().Reviews.Single(r=>r.TargetType=="WrongQuestion").Status=="Completed" && review.Output().Reviews.Single(r=>r.TargetType=="KC").DueDate==new DateOnly(2026,3,2),"D0/D2/D7/D30 independent dates failed");
        var group=Guid.NewGuid();var variants=Enumerable.Range(1,4).Select(n=>Input(n,variant:group)).ToArray();Check(Compare(variants).Output().Evidence.Select(x=>x.Weight).SequenceEqual(new[]{1m,.8m,.2m,0m}),"uncorrected variant gold failed");var corrected=variants.ToArray();corrected[0]=corrected[0] with{Grade=new Grading{FamilyId=Family,AttemptId=corrected[0].Attempt.Id,Result="Incorrect"}};Check(Compare(corrected).Output().Evidence.Select(x=>x.Weight).SequenceEqual(new[]{1m,.8m,.8m,.4m}),"AT15 independent corrected caps failed");
        var teaching=new[]{new TeachingAnchor(Skill.Id,Origin.AddDays(7))};Compare([Input(1),Input(2,8,type:"Review")],teaching);Compare([Input(1,0,"Incorrect",q),Input(2,2,question:q,hint:1,type:"Review"),Input(3,7,question:q,type:"Review")],zone:"UTC");
        var random=new Random(71234);var questions=Enumerable.Range(0,12).Select(_=>Guid.NewGuid()).ToArray();var groups=Enumerable.Range(0,3).Select(_=>Guid.NewGuid()).ToArray();var rows=new List<AssessmentInput>();
        for(var n=1;n<=160;n++)
        {
            var index=random.Next(questions.Length);var value=Input(n,n/5,new[]{"Correct","Incorrect","Pending","Partial"}[random.Next(4)],questions[index],random.Next(3),n%3==0?"Review":"Practice",groups[index%3]);value.Attempt.AnswerShown=n%11==0;value.Attempt.Number=n%13==0?2:1;
            if(n%7==0)value=value with{Question=value.Question with{Policy="NoEvidence"}};
            if(n%4==0)value=value with{Question=value.Question with{Policy="ObservedSteps",Mappings=[new(Skill.Id,"Primary",.333333m,"StepObserved","s1"),new(Skill.Id,"Primary",.666667m,"StepObserved","s2")]},Grade=new Grading{FamilyId=Family,AttemptId=value.Attempt.Id,Result="Partial",Steps=Json.Write(new[]{new ObservedStep("s1",n%8==0?"Incorrect":"Correct"),new ObservedStep("s2","Correct",n%3)})}};
            rows.Add(value);
        }
        Compare(rows.ToArray(),teaching,restartEvery:11);
        var second=new KC(Guid.NewGuid(),Guid.NewGuid(),"INC2","另一能力","独立观察","仅测试",RequiredCoverage:["Basic","Application"]);var multi=Enumerable.Range(1,24).Select(n=>{var v=Input(n,n/2,type:n%3==0?"Review":"Practice");return v with{Kcs=[Skill,second],Question=v.Question with{Coverage=n%2==0?"Basic":"Application",Policy="ObservedSteps",Mappings=[new(Skill.Id,"Primary",.333333m,"StepObserved","a"),new(second.Id,"Primary",.666667m,"StepObserved","b")]},Grade=new Grading{FamilyId=Family,AttemptId=v.Attempt.Id,Result="Partial",Steps=Json.Write(new[]{new ObservedStep("a",n%7==0?"Incorrect":"Correct"),new ObservedStep("b",n%5==0?"Incorrect":"Correct")})}};}).ToArray();Compare(multi,restartEvery:3);

        if(Environment.GetEnvironmentVariable("STATE_FORK_BENCHMARK")=="1")
        {
            var large=new IncrementalAssessment(Family,Student,Generation,"UTC");for(var n=1;n<=1500;n++)large.Append(Input(n,n/10));var payload=large.Freeze();var target=Guid.NewGuid();_=LegacyFork(payload,target);_=IncrementalAssessment.Fork(payload,target);
            var allocation=GC.GetAllocatedBytesForCurrentThread();var timer=System.Diagnostics.Stopwatch.StartNew();var previous=LegacyFork(payload,target);timer.Stop();var oldBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;var oldMs=timer.Elapsed.TotalMilliseconds;
            allocation=GC.GetAllocatedBytesForCurrentThread();timer.Restart();var current=IncrementalAssessment.Fork(payload,target);timer.Stop();var newBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;Check(Canonical(previous.Output())==Canonical(current.Output()) && newBytes<oldBytes,"fork benchmark changed semantics or failed allocation reduction");Console.WriteLine($"STATE_FORK_BENCHMARK inputs=1500 originalSerializationAllocated={oldBytes} directSnapshotAllocated={newBytes} originalSerializationMs={oldMs:F2} directSnapshotMs={timer.Elapsed.TotalMilliseconds:F2}");
        }
        var saved=e.Freeze();try{e.Append(mastery[0]);throw new Exception("old correction accepted as append");}catch(ApiError x){Check(x.Code=="INCREMENTAL_ORDER_INVALID","wrong order rejection");}Check(saved==e.Freeze(),"rejected duplicate altered state");
        var foreign=Input(100);foreign.Attempt.FamilyId=Guid.NewGuid();try{e.Append(foreign);throw new Exception("foreign input accepted");}catch(ApiError x){Check(x.Code=="INCREMENTAL_CONTEXT_INVALID","wrong family rejection");}
        var failed=new IncrementalAssessment(Family,Student,Generation,"UTC");try{failed.Append(Input(1) with{Kcs=[]});throw new Exception("invalid skill accepted");}catch(InvalidOperationException){}
        try{failed.Freeze();throw new Exception("failed partial state saved");}catch(ApiError x){Check(x.Code=="INCREMENTAL_STATE_INVALID","partial state guard failed");}
    }
}
