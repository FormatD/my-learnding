using Learning;
public static class ReviewTargetCases
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        var family=Guid.NewGuid();var student=Guid.NewGuid();var generation=Guid.NewGuid();var skill=new KC(Guid.NewGuid(),Guid.NewGuid(),"TARGET","原能力","独立计算","测量原能力");var other=new KC(Guid.NewGuid(),Guid.NewGuid(),"OTHER","另一能力","独立计算","测量另一能力");var time=new DateTimeOffset(2026,1,1,8,0,0,TimeSpan.Zero);
        AssessmentInput Input(int sequence,int day=0,string result="Correct")
        {
            var q=new Question(Guid.NewGuid(),Guid.NewGuid(),"2×2","4","4","Numeric","Medium","SingleKC",[new(skill.Id)]);var task=new StudyTask{FamilyId=family,StudentId=student,Type="Practice"};var session=new LearningSession{FamilyId=family,StudentId=student,QuestionId=q.Id,TaskId=task.Id};var attempt=new Attempt{FamilyId=family,StudentId=student,SessionId=session.Id,Number=1,Sequence=sequence,CreatedAt=time.AddDays(day).AddMinutes(sequence)};return new(attempt,session,q,new Grading{FamilyId=family,AttemptId=attempt.Id,Result=result},[skill,other],task);
        }
        var prefix=Enumerable.Range(1,10).Select(n=>Input(n)).ToArray();
        void Verify(AssessmentInput suffix,bool executed,bool advanced)
        {
            var rows=prefix.Append(suffix).ToArray();var full=Assessment.Replay(family,student,generation,"UTC",rows,collectReviewHistory:true);var incremental=new IncrementalAssessment(family,student,generation,"UTC");foreach(var input in rows){incremental.Append(input);incremental=IncrementalAssessment.Resume(incremental.Freeze());}
            foreach(var output in new[]{full,incremental.Output()})
            {
                var review=output.Reviews.Single(r=>r.TargetType=="KC" && r.TargetId==skill.Id);Check(review.DueDate==(advanced?new DateOnly(2026,2,8):new DateOnly(2026,1,8)),"target date did not match independent expected boundary");Check(review.Stage==(advanced?"LowFrequency":"R1"),"target stage incorrectly advanced");var first=output.ReviewHistory.First(r=>r.TargetType=="KC" && r.TargetId==skill.Id);Check((first.ExecutedAttemptId==suffix.Attempt.Id)==executed,"target execution counted an unrelated measurement");
            }
        }
        AssessmentInput Review()=>Input(11,8) with{Task=new StudyTask{FamilyId=family,StudentId=student,Type="Review",ReviewTargetId=skill.Id}};
        Verify(Review(),true,true);
        var migrated=Review();Verify(migrated with{Question=migrated.Question with{Mappings=[new(other.Id)]}},false,false);
        var changed=migrated with{Question=migrated.Question with{Mappings=[new(other.Id)]}};changed.Session.TaskId=changed.Task.Id;
        var report=ReviewReporting.Calculate([changed.Attempt],[changed.Session],[changed.Task],[changed.Grade],"UTC",new(2026,1,1),new(2026,1,10),new Dictionary<Guid,AssessmentInput>{{changed.Attempt.Id,changed}});
        Check(report.GradedEncounters==1 && report.IndependentPasses==0 && report.Items.Single().Reason=="TARGET_CHANGED","report counted the unrelated target as passed or hid the encounter");
        changed.Task.ReviewTargetId=changed.Session.QuestionId=changed.Question.Id;
        var wrongReport=ReviewReporting.Calculate([changed.Attempt],[changed.Session],[changed.Task],[changed.Grade],"UTC",new(2026,1,1),new(2026,1,10),new Dictionary<Guid,AssessmentInput>{{changed.Attempt.Id,changed}});
        Check(wrongReport.IndependentPasses==1 && wrongReport.Items.Single().Reason=="TRUSTED_INDEPENDENT","wrong-question identity was mistaken for a knowledge-point identity");
        var no=Review();Verify(no with{Question=no.Question with{Policy="NoEvidence"}},false,false);
        var context=Review();Verify(context with{Question=context.Question with{Mappings=[new(skill.Id,"Context",1,"WholeItem")]}},false,false);
        var secondary=Review();Verify(secondary with{Question=secondary.Question with{Mappings=[new(skill.Id,"Secondary",1,"None")]}},false,false);
        var zero=Review();Verify(zero with{Question=zero.Question with{Mappings=[new(skill.Id,Share:0)]}},false,false);
        var practice=Review();practice.Task.Type="Practice";Verify(practice,false,false);
        var pending=Review();Verify(pending with{Grade=new Grading{FamilyId=family,AttemptId=pending.Attempt.Id,Result="Pending"}},true,false);
        var step=Review();var measured=step.Question with{Policy="ObservedSteps",Mappings=[new(skill.Id,"Primary",1,"StepObserved","a")]};
        Verify(step with{Question=measured,Grade=new Grading{FamilyId=family,AttemptId=step.Attempt.Id,Result="Partial",Steps=Json.Write(new[]{new ObservedStep("a","Correct")})}},true,true);
        Verify(step with{Question=measured,Grade=new Grading{FamilyId=family,AttemptId=step.Attempt.Id,Result="Correct",Steps="[]"}},true,false);
        Verify(step with{Question=measured,Grade=new Grading{FamilyId=family,AttemptId=step.Attempt.Id,Result="Correct",Steps=Json.Write(new[]{new ObservedStep("a","Correct",1)})}},true,false);
        Verify(step with{Question=measured with{Mappings=[new(skill.Id,"Primary",.5m,"StepObserved","a"),new(skill.Id,"Primary",.5m,"StepObserved","b")]},Grade=new Grading{FamilyId=family,AttemptId=step.Attempt.Id,Result="Partial",Steps=Json.Write(new[]{new ObservedStep("a","Correct"),new ObservedStep("b","Incorrect")})}},true,false);
        // A hint recorded only on an observed step also prevents a whole-question independent pass.
        var wrong=Input(1,result:"Incorrect");var helped=Input(2,2) with{Question=wrong.Question,Grade=new Grading{Result="Correct",Steps=Json.Write(new[]{new ObservedStep("a","Correct",1)})}};
        foreach(var output in new[]{Assessment.Replay(family,student,generation,"UTC",[wrong,helped]),RunIncremental([wrong,helped])}){var r=output.Reviews.Single(r=>r.TargetType=="WrongQuestion");Check(r.Stage=="R1" && r.DueDate==new DateOnly(2026,1,5),"step help falsely promoted whole-question review");}
        AssessmentOutput RunIncremental(AssessmentInput[] rows){var engine=new IncrementalAssessment(family,student,generation,"UTC");foreach(var input in rows)engine.Append(input);return engine.Output();}
    }
}
