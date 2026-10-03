using Learning;
using System.Security.Cryptography;
using System.Text;

public record FixtureEvent(int Sequence,int QuestionIndex,string SessionKey,int Day=0,int Minutes=0,int AttemptNumber=1,string Result="Correct",int HintLevel=0,bool AnswerShown=false,string Type="Practice",string[]? GradingRevisions=null,string? Answer=null);
public record FixtureMastery(string Code,decimal Alpha,decimal Beta);
public record FixtureReview(string TargetType,int QuestionIndex,string Stage,string DueDate,string Status="Pending");
public record FixtureCase(string Name,string Anchor,string TimeZone,FixtureEvent[] Events,int EvidenceParts,FixtureMastery[] Masteries,FixtureReview[] Reviews);
public static class FixtureCases
{
    static void Require(bool condition,string reason){if(!condition)throw new Exception(reason);}
    static Guid Id(string name)=>new(SHA256.HashData(Encoding.UTF8.GetBytes("learning-synthetic-fixture/1:"+name)).Take(16).ToArray());
    public static void Verify()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"Fixtures");
        var catalog=Json.Read<Catalog>(File.ReadAllText(Path.Combine(directory,"minimal-unit.json")));
        Require(catalog.Questions.Length==20 && Content.Validate(catalog).Length==0,"Minimal fixture content invalid");
        var cases=Json.Read<FixtureCase[]>(File.ReadAllText(Path.Combine(directory,"replay-cases.json")));
        Require(cases.Length>=4 && cases.Select(c=>c.Name).Distinct().Count()==cases.Length,"Fixture cases missing or duplicated");
        foreach(var scenario in cases)
        {
            var family=Id(scenario.Name+":family");var student=Id(scenario.Name+":student");var generation=Id(scenario.Name+":generation");
            var anchor=DateTimeOffset.Parse(scenario.Anchor,System.Globalization.CultureInfo.InvariantCulture);
            Require(scenario.Events.Select(e=>e.Sequence).Distinct().Count()==scenario.Events.Length,"Fixture sequences must be unique");
            var inputs=scenario.Events.Select(e=>
            {
                Require(e.QuestionIndex>=0 && e.QuestionIndex<catalog.Questions.Length && e.Sequence>0 && e.AttemptNumber>0,"Fixture reference invalid");
                Require(e.HintLevel is >=0 and <=3 && e.Type is "Practice" or "Review","Fixture enum invalid");
                var question=catalog.Questions[e.QuestionIndex];var stamp=anchor.AddDays(e.Day).AddMinutes(e.Minutes);
                var task=new StudyTask{Id=Id(scenario.Name+":task:"+e.SessionKey),FamilyId=family,StudentId=student,Type=e.Type,QuestionId=question.Id};
                var session=new LearningSession{Id=Id(scenario.Name+":session:"+e.SessionKey),FamilyId=family,StudentId=student,TaskId=task.Id,QuestionId=question.Id,ReleaseId=Id(scenario.Name+":release")};
                var attempt=new Attempt{Id=Id(scenario.Name+":attempt:"+e.Sequence),FamilyId=family,StudentId=student,SessionId=session.Id,Sequence=e.Sequence,Number=e.AttemptNumber,CreatedAt=stamp,Answer=e.Answer??throw new Exception("Fixture raw answer missing"),HintLevel=e.HintLevel,AnswerShown=e.AnswerShown};
                var revisions=e.GradingRevisions??[e.Result];Require(revisions.Length>0 && revisions.All(r=>r is "Correct" or "Incorrect" or "Partial" or "Pending"),"Fixture grade invalid");
                var grade=new Grading{Id=Id(scenario.Name+":grading:"+e.Sequence+":"+revisions.Length),AttemptId=attempt.Id,Number=revisions.Length,Result=revisions.Last(),CreatedAt=stamp.AddMinutes(1),Method=revisions.Length>1?"ParentConfirmed":"Rule"};
                return new AssessmentInput(attempt,session,question,grade,catalog.Kcs,task);
            }).ToArray();
            Require(inputs.GroupBy(i=>i.Session.Id).All(g=>g.Select(i=>i.Session.QuestionId).Distinct().Count()==1 && g.Select(i=>i.Attempt.Number).Distinct().Count()==g.Count()),"Fixture session identity invalid");
            var sourceSnapshot=Json.Write(inputs);
            var output=Assessment.Replay(family,student,generation,scenario.TimeZone,inputs);
            Require(Json.Write(inputs)==sourceSnapshot,scenario.Name+": original input was modified");
            Require(output.Evidence.Count==scenario.EvidenceParts,scenario.Name+": evidence count mismatch");
            Require(output.Masteries.Count==scenario.Masteries.Length,scenario.Name+": mastery count mismatch");
            foreach(var expected in scenario.Masteries)
            {
                var kc=catalog.Kcs.Single(k=>k.Code==expected.Code);var actual=output.Masteries.Single(m=>m.KCId==kc.Id);
                Require(actual.Alpha==expected.Alpha && actual.Beta==expected.Beta,scenario.Name+": Beta parameters mismatch");
            }
            Require(output.Reviews.Count==scenario.Reviews.Length,scenario.Name+": review count mismatch");
            foreach(var expected in scenario.Reviews)
            {
                var question=catalog.Questions[expected.QuestionIndex];var target=expected.TargetType=="WrongQuestion"?question.Id:question.Mappings.First(m=>m.Mode!="None").KCId;
                var actual=output.Reviews.Single(r=>r.TargetType==expected.TargetType && r.TargetId==target);
                Require(actual.Stage==expected.Stage && actual.DueDate.ToString("yyyy-MM-dd")==expected.DueDate && actual.Status==expected.Status,scenario.Name+": review progression mismatch");
            }
            var stream=new IncrementalAssessment(family,student,generation,scenario.TimeZone);
            foreach(var input in inputs.OrderBy(i=>i.Attempt.Sequence).ThenBy(i=>i.Attempt.Id)){stream.Append(input);stream=IncrementalAssessment.Resume(stream.Freeze());}
            var incremental=stream.Output();Require(incremental.Evidence.Count==scenario.EvidenceParts && incremental.Masteries.Count==scenario.Masteries.Length && incremental.Reviews.Count==scenario.Reviews.Length,scenario.Name+": incremental golden counts mismatch");
            foreach(var expected in scenario.Masteries){var skill=catalog.Kcs.Single(k=>k.Code==expected.Code);var actual=incremental.Masteries.Single(m=>m.KCId==skill.Id);Require(actual.Alpha==expected.Alpha && actual.Beta==expected.Beta,scenario.Name+": incremental golden Beta mismatch");}
            foreach(var expected in scenario.Reviews){var target=expected.TargetType=="WrongQuestion"?catalog.Questions[expected.QuestionIndex].Id:catalog.Questions[expected.QuestionIndex].Mappings[0].KCId;var actual=incremental.Reviews.Single(r=>r.TargetType==expected.TargetType && r.TargetId==target);Require(actual.Stage==expected.Stage && actual.DueDate.ToString("yyyy-MM-dd")==expected.DueDate && actual.Status==expected.Status,scenario.Name+": incremental golden dates mismatch");}
            Require(Json.Write(inputs)==sourceSnapshot,scenario.Name+": incremental modified original source");
            var reversed=Assessment.Replay(family,student,generation,scenario.TimeZone,inputs.Reverse());
            Require(Json.Write(output.Masteries.Select(m=>new{m.KCId,m.Alpha,m.Beta,m.Status,m.NeedsRecheck}))==Json.Write(reversed.Masteries.Select(m=>new{m.KCId,m.Alpha,m.Beta,m.Status,m.NeedsRecheck})),scenario.Name+": event ordering mismatch");
        }
    }
}
