namespace Learning;
public static class ReviewTargets
{
    public static bool Measures(Question question,Guid target)=>question.Policy!="NoEvidence" && question.Mappings.Any(m=>m.KCId==target && m.Share>0 && m.Mode is "WholeItem" or "StepObserved" && m.Role is not "Prerequisite" and not "Context");
    public static bool IndependentPass(AssessmentInput input,Guid target)
    {
        if(input.Task.Type!="Review" || input.Task.ReviewTargetId!=target || !Measures(input.Question,target) || input.Attempt.HintLevel>0 || input.Attempt.AnswerShown || input.Grade.Result is not ("Correct" or "Incorrect" or "Partial"))return false;
        var steps=Json.Read<ObservedStep[]>(input.Grade.Steps);
        return input.Question.Mappings.Where(m=>m.KCId==target && m.Share>0 && m.Role is not "Prerequisite" and not "Context").All(m=>m.Mode=="WholeItem"?input.Grade.Result=="Correct" && !steps.Any(s=>s.HintLevel>0):m.Mode=="StepObserved" && steps.FirstOrDefault(s=>s.Step==m.Step) is {Result:"Correct",HintLevel:0});
    }
    public static bool WholeIndependent(AssessmentInput input)=>input.Grade.Result=="Correct" && input.Attempt.HintLevel==0 && !input.Attempt.AnswerShown && !Json.Read<ObservedStep[]>(input.Grade.Steps).Any(s=>s.HintLevel>0);
}
