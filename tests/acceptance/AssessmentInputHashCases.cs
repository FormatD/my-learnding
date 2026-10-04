using Learning;
public static class AssessmentInputHashCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    // The prior production implementation is retained as a byte-contract oracle.
    static string Reference(string zone,AssessmentInput[] inputs,TeachingAnchor[] teaching)=>Content.Hash(Json.Write(new{inputs,teaching,mappingContext="assessment-context/1",inputVersion=Assessment.InputHashVersion,timeZone=zone,rule=Assessment.EvidenceRuleVersion,model=Assessment.MasteryModelVersion,review=Assessment.ReviewRuleVersion}));
    public static void Run()
    {
        var family=Guid.NewGuid();var student=Guid.NewGuid();var skill=new KC(Guid.NewGuid(),Guid.NewGuid(),"MATH.HASH","混合𠀀运算<\"\\>","独立\n计算","单层括号");var question=new Question(Guid.NewGuid(),Guid.NewGuid(),"(24−8)÷4=?","4","先算括号","Numeric","Medium","SingleKC",[new(skill.Id)]);
        var session=new LearningSession{FamilyId=family,StudentId=student,TaskId=Guid.NewGuid(),ReleaseId=Guid.NewGuid(),QuestionId=question.Id};var task=new StudyTask{FamilyId=family,StudentId=student,Type="Review",ReviewTargetId=skill.Id};
        var input=new AssessmentInput(new Attempt{FamilyId=family,StudentId=student,SessionId=session.Id,Sequence=1,Number=1,Answer="4\n\"<>&𠀀",CreatedAt=new DateTimeOffset(2026,2,3,4,5,6,TimeSpan.FromHours(8))},session,question,new Grading{FamilyId=family,Result="Correct",Steps="[]"},[skill],task,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new ReviewTargetConfirmation{FamilyId=family,StudentId=student});
        var data=Enumerable.Range(0,9).Select(n=>input with{MappingReleaseId=n%2==0?input.MappingReleaseId:null,ReviewConfirmation=n%2==0?input.ReviewConfirmation:null}).ToArray();var teaching=new[]{new TeachingAnchor(skill.Id,input.Attempt.CreatedAt),new TeachingAnchor(skill.Id,input.Attempt.CreatedAt.AddHours(1))};
        foreach(var zone in new[]{"Asia/Shanghai","UTC","Zone𠀀<\"\\"})foreach(var anchors in new[]{Array.Empty<TeachingAnchor>(),teaching})foreach(var length in new[]{0,1,9})
        {
            var sample=data.Take(length).ToArray();var visits=0;IEnumerable<AssessmentInput> Once(){foreach(var item in sample){visits++;yield return item;}}
            Check(AssessmentInputHashes.Compute(zone,Once(),anchors)==Reference(zone,sample,anchors),"streamed generation input changed prior JSON bytes");Check(visits==length,"generation input enumerated twice");
        }
        var original=AssessmentInputHashes.Compute("UTC",data,teaching);var prefix=AssessmentCheckpoints.PrefixHash("UTC",data,teaching);task.Status="Completed";
        Check(AssessmentInputHashes.Compute("UTC",data,teaching)!=original && AssessmentInputHashes.Compute("UTC",data,teaching)==Reference("UTC",data,teaching),"full task workflow fields lost from original input contract");Check(AssessmentCheckpoints.PrefixHash("UTC",data,teaching)==prefix,"shared hash stream changed mathematical prefix contract");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();try{AssessmentInputHashes.Compute("UTC",data,teaching,cancelled.Token);throw new Exception("cancellation ignored");}catch(OperationCanceledException){}
        if(Environment.GetEnvironmentVariable("INPUT_HASH_BENCHMARK")=="1")
        {
            var workload=Enumerable.Repeat(input,10_000).ToArray();_=AssessmentInputHashes.Compute("UTC",workload,teaching);_=Reference("UTC",workload,teaching);
            var allocation=GC.GetAllocatedBytesForCurrentThread();var timer=System.Diagnostics.Stopwatch.StartNew();var old=Reference("UTC",workload,teaching);timer.Stop();var oldBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;var oldMs=timer.Elapsed.TotalMilliseconds;
            allocation=GC.GetAllocatedBytesForCurrentThread();timer.Restart();var streamed=AssessmentInputHashes.Compute("UTC",workload,teaching);timer.Stop();var newBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;Check(streamed==old && newBytes<oldBytes,"benchmark digest changed or allocation not reduced");Console.WriteLine($"INPUT_HASH_BENCHMARK items={workload.Length} originalAllocated={oldBytes} streamedAllocated={newBytes} originalMs={oldMs:F2} streamedMs={timer.Elapsed.TotalMilliseconds:F2}");
        }
    }
}
