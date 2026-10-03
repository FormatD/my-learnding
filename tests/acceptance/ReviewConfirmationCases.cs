using Learning;
public static class ReviewConfirmationCases
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        var family=Guid.NewGuid();var student=Guid.NewGuid();var generation=Guid.NewGuid();var first=new KC(Guid.NewGuid(),Guid.NewGuid(),"OLD","原目标","计算","原目标测量");var second=new KC(Guid.NewGuid(),Guid.NewGuid(),"NEW","实际目标","计算","实际目标测量");var time=new DateTimeOffset(2026,1,1,8,0,0,TimeSpan.Zero);
        AssessmentInput Make(int sequence,Guid skill,int day=0,bool review=false){var q=new Question(Guid.NewGuid(),Guid.NewGuid(),"2×2","4","4","Numeric","Medium","SingleKC",[new(skill)]);var task=new StudyTask{FamilyId=family,StudentId=student,Type=review?"Review":"Practice",ReviewTargetId=review?first.Id:null};var session=new LearningSession{FamilyId=family,StudentId=student,TaskId=task.Id,QuestionId=q.Id,ReleaseId=Guid.NewGuid()};var a=new Attempt{FamilyId=family,StudentId=student,SessionId=session.Id,Number=1,Sequence=sequence,CreatedAt=time.AddDays(day).AddMinutes(sequence)};return new(a,session,q,new Grading{FamilyId=family,AttemptId=a.Id,Result="Correct"},[first,second],task);}
        var prefix=Enumerable.Range(1,20).Select(n=>Make(n,n<=10?first.Id:second.Id)).ToArray();var answer=Make(21,second.Id,8,true);
        ReviewTargetConfirmation Decision(string action,Guid target){var snapshot=Json.Write(ReviewTargetConfirmations.Measurement(answer));return new(){FamilyId=family,StudentId=student,AttemptId=answer.Attempt.Id,TaskId=answer.Task.Id,OriginalTargetId=first.Id,ConfirmedTargetId=target,Action=action,MeasurementSnapshot=snapshot,MeasurementHash=Content.Hash(snapshot)};}
        void Verify(AssessmentInput row,bool adopted)
        {
            var inputs=prefix.Append(row).ToArray();var full=Assessment.Replay(family,student,generation,"UTC",inputs,collectReviewHistory:true);var engine=new IncrementalAssessment(family,student,generation,"UTC");foreach(var i in inputs){engine.Append(i);engine=IncrementalAssessment.Resume(engine.Freeze());}
            foreach(var output in new[]{full,engine.Output()}){var original=output.Reviews.Single(r=>r.TargetType=="KC" && r.TargetId==first.Id);var actual=output.Reviews.Single(r=>r.TargetType=="KC" && r.TargetId==second.Id);Check(original.Stage=="R1" && original.DueDate==new DateOnly(2026,1,8),"parent choice silently completed the unmeasured original target");Check(actual.Stage==(adopted?"LowFrequency":"R1") && actual.DueDate==(adopted?new DateOnly(2026,2,8):new DateOnly(2026,1,8)),"confirmed actual target did not respect due/pass boundaries");Check(output.ReviewHistory.Any(r=>r.TargetId==second.Id && r.ExecutedAttemptId==row.Attempt.Id)==adopted,"execution did not follow the explicit current measurement choice");Check(!output.ReviewHistory.Any(r=>r.TargetId==first.Id && r.ExecutedAttemptId==row.Attempt.Id),"original target falsely marked executed");}
        }
        Verify(answer,false);Verify(answer with{ReviewConfirmation=Decision("KeepOriginal",first.Id)},false);var confirmed=answer with{ReviewConfirmation=Decision("AdoptMeasuredTarget",second.Id)};Verify(confirmed,true);
        var stale=answer with{Question=answer.Question with{RevisionId=Guid.NewGuid()},ReviewConfirmation=confirmed.ReviewConfirmation};Verify(stale,false);
        var damaged=Decision("AdoptMeasuredTarget",second.Id);damaged.MeasurementHash=new string('0',64);Verify(answer with{ReviewConfirmation=damaged},false);
        var foreign=Decision("AdoptMeasuredTarget",second.Id);foreign.StudentId=Guid.NewGuid();Verify(answer with{ReviewConfirmation=foreign},false);
        var unmapped=Decision("AdoptMeasuredTarget",Guid.NewGuid());Verify(answer with{ReviewConfirmation=unmapped},false);
        var plain=Assessment.Replay(family,student,generation,"UTC",prefix.Append(answer));var accepted=Assessment.Replay(family,student,generation,"UTC",prefix.Append(confirmed));
        Check(plain.Evidence.Select(e=>(e.AttemptId,e.KCId,e.Weight,e.Positive)).SequenceEqual(accepted.Evidence.Select(e=>(e.AttemptId,e.KCId,e.Weight,e.Positive))),"review confirmation transferred or fabricated knowledge evidence");Check(answer.Task.ReviewTargetId==first.Id,"confirmation rewrote the published task association");
    }
}
