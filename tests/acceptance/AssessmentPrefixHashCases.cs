using Learning;
public static class AssessmentPrefixHashCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    static string Reference(string zone,IEnumerable<AssessmentInput> input,TeachingAnchor[] teaching)=>Content.Hash(Json.Write(new{prefixVersion="assessment-prefix/3",version=IncrementalAssessment.Version,inputVersion=Assessment.InputHashVersion,timeZone=zone,evidence=Assessment.EvidenceRuleVersion,mastery=Assessment.MasteryModelVersion,review=Assessment.ReviewRuleVersion,inputs=input.Select(x=>new{x.Attempt,x.Question,x.Grade,x.Kcs,x.MappingReleaseId,x.CorrectionBatchId,x.MappingSetRevisionId,x.GradingCorrectionBatchId,x.MappingCorrectionBatchId,x.ReviewConfirmation,session=new{x.Session.Id,x.Session.FamilyId,x.Session.StudentId,x.Session.TaskId,x.Session.ReleaseId,x.Session.QuestionId},task=new{x.Task.Id,x.Task.FamilyId,x.Task.StudentId,x.Task.Type,x.Task.ReviewTargetId}}).ToArray(),teaching}));
    public static void Run()
    {
        var family=Guid.NewGuid();var student=Guid.NewGuid();var kc=new KC(Guid.NewGuid(),Guid.NewGuid(),"MATH.HASH","混合𠀀运算<\"\\>","独立\n计算","只含一层括号");var question=new Question(Guid.NewGuid(),Guid.NewGuid(),"(24−8)÷4=?","4","先算括号","Numeric","Medium","SingleKC",[new(kc.Id)]);
        var session=new LearningSession{FamilyId=family,StudentId=student,TaskId=Guid.NewGuid(),ReleaseId=Guid.NewGuid(),QuestionId=question.Id};var task=new StudyTask{FamilyId=family,StudentId=student,Type="Review",ReviewTargetId=kc.Id};
        var input=new AssessmentInput(new Attempt{FamilyId=family,StudentId=student,SessionId=session.Id,Sequence=1,Number=1,Answer="4\n\"<>&𠀀",CreatedAt=new DateTimeOffset(2026,2,3,4,5,6,TimeSpan.FromHours(8))},session,question,new Grading{FamilyId=family,Result="Correct",Steps="[]"},[kc],task,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new ReviewTargetConfirmation{FamilyId=family,StudentId=student});
        var inputs=Enumerable.Range(0,9).Select(n=>input with{Attempt=new Attempt{FamilyId=family,StudentId=student,SessionId=session.Id,Sequence=n+1,Number=n%2+1,Answer=input.Attempt.Answer,CreatedAt=input.Attempt.CreatedAt.AddDays(n)},ReviewConfirmation=n%2==0?input.ReviewConfirmation:null,MappingReleaseId=n%2==0?input.MappingReleaseId:null}).ToArray();var teaching=new[]{new TeachingAnchor(kc.Id,input.Attempt.CreatedAt),new TeachingAnchor(kc.Id,input.Attempt.CreatedAt.AddHours(1))};
        foreach(var zone in new[]{"Asia/Shanghai","UTC","Zone𠀀<\"\\"})foreach(var anchors in new[]{Array.Empty<TeachingAnchor>(),teaching})foreach(var length in new[]{0,1,9})
        {
            var sample=inputs.Take(length).ToArray();var expected=Reference(zone,sample,anchors);
            for(var count=0;count<=length+1;count++)
            {
                var visits=0;IEnumerable<AssessmentInput> Once(){foreach(var item in sample){visits++;yield return item;}}
                var actual=AssessmentPrefixHashes.Compute(zone,Once(),anchors,count);Check(actual.Full==expected && actual.Prefix==(count<=length?Reference(zone,sample.Take(count),anchors):null),"streamed hash differs from original complete JSON byte contract");Check(visits==length,"prefix/full calculation enumerated source twice");
            }
        }
        Check(AssessmentCheckpoints.PrefixHash("UTC",inputs,teaching)==Reference("UTC",inputs,teaching),"existing caller contract changed");
        var original=AssessmentPrefixHashes.Compute("UTC",inputs,teaching,4);var changed=inputs.ToArray();changed[0]=changed[0] with{Question=question with{Explanation="历史更正"}};
        Check(AssessmentPrefixHashes.Compute("UTC",changed,teaching,4).Prefix!=original.Prefix && AssessmentPrefixHashes.Compute("UTC",changed,teaching,4).Full!=original.Full,"historical correction did not change both hashes");changed=inputs.ToArray();changed[8]=changed[8] with{Question=question with{Explanation="新后缀更正"}};var suffix=AssessmentPrefixHashes.Compute("UTC",changed,teaching,4);Check(suffix.Prefix==original.Prefix && suffix.Full!=original.Full,"suffix changed prior-prefix hash");
        if(Environment.GetEnvironmentVariable("PREFIX_HASH_BENCHMARK")=="1")
        {
            var workload=Enumerable.Repeat(input,10_000).ToArray();var split=workload.Length-1;
            _=AssessmentPrefixHashes.Compute("UTC",workload,teaching,split);_=Reference("UTC",workload,teaching);
            var allocation=GC.GetAllocatedBytesForCurrentThread();var timer=System.Diagnostics.Stopwatch.StartNew();var oldFull=Reference("UTC",workload,teaching);var oldPrefix=Reference("UTC",workload.Take(split),teaching);timer.Stop();var oldBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;var oldMs=timer.Elapsed.TotalMilliseconds;
            allocation=GC.GetAllocatedBytesForCurrentThread();timer.Restart();var streamed=AssessmentPrefixHashes.Compute("UTC",workload,teaching,split);timer.Stop();var newBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;
            Check(streamed.Full==oldFull && streamed.Prefix==oldPrefix,"benchmark bytes changed");Check(newBytes<oldBytes,"streaming did not reduce managed allocation");Console.WriteLine($"PREFIX_HASH_BENCHMARK items={workload.Length} originalVisits={workload.Length+split} streamedVisits={workload.Length} originalAllocated={oldBytes} streamedAllocated={newBytes} originalMs={oldMs:F2} streamedMs={timer.Elapsed.TotalMilliseconds:F2}");
        }
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();try{AssessmentPrefixHashes.Compute("UTC",inputs,teaching,4,cancelled.Token);throw new Exception("cancellation ignored");}catch(OperationCanceledException){}
    }
}
