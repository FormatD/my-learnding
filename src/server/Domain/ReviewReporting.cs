using Microsoft.EntityFrameworkCore;
namespace Learning;
public record ReviewEncounter(Guid AttemptId,Guid SessionId,Guid TaskId,Guid QuestionId,DateTimeOffset OccurredAt,Guid? GradingId,DateTimeOffset? GradedAt,string Result,bool Graded,bool IndependentlyPassed,string Reason);
public record ReviewPassSummary(int Encounters,int GradedEncounters,int IndependentPasses,int PendingEncounters,decimal? Rate,ReviewEncounter[] Items,string Note);
public static class ReviewReporting
{
    public static ReviewPassSummary Calculate(IEnumerable<Attempt> attempts,IEnumerable<LearningSession> sessions,IEnumerable<StudyTask> tasks,IEnumerable<Grading> grades,string zone,DateOnly start,DateOnly end,IReadOnlyDictionary<Guid,AssessmentInput>? effectiveInputs=null)
    {
        var sessionMap=sessions.ToDictionary(s=>s.Id);var taskMap=tasks.ToDictionary(t=>t.Id);
        var latest=grades.GroupBy(g=>g.AttemptId).ToDictionary(g=>g.Key,g=>g.OrderByDescending(v=>v.Number).First());
        var encountered=new Dictionary<Guid,DateTimeOffset>();var rows=new List<ReviewEncounter>();var timeZone=TimeZoneInfo.FindSystemTimeZoneById(zone);
        foreach(var a in attempts.Where(a=>a.Number==1).OrderBy(a=>a.Sequence).ThenBy(a=>a.Id))
        {
            var s=sessionMap[a.SessionId];var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(a.CreatedAt,timeZone).DateTime);
            // Keep the same rolling encounter suppression as evidence replay, including ungraded first answers.
            var duplicate=encountered.TryGetValue(s.QuestionId,out var previous) && a.CreatedAt-previous<TimeSpan.FromHours(24);
            if(!duplicate)encountered[s.QuestionId]=a.CreatedAt;
            if(day<start || day>end || taskMap[s.TaskId].Type!="Review")continue;
            var grade=latest.GetValueOrDefault(a.Id);var graded=grade?.Result is "Correct" or "Incorrect" or "Partial";
            var assisted=a.HintLevel>0 || a.AnswerShown || grade!=null && Json.Read<ObservedStep[]>(grade.Steps).Any(v=>v.HintLevel>0);
            var source=effectiveInputs?.GetValueOrDefault(a.Id);var target=source==null?taskMap[s.TaskId].ReviewTargetId:ReviewTargets.Target(source);
            var changed=target!=null && target!=s.QuestionId && effectiveInputs!=null && (source==null || !ReviewTargets.Measures(source.Question,target.Value));
            var passed=graded && grade!.Result=="Correct" && !assisted && !duplicate && !changed;
            var reason=changed?(source?.ReviewConfirmation?.Action=="KeepOriginal"?"TARGET_RETAINED":"TARGET_CHANGED"):!graded?"PENDING_GRADING":grade!.Result!="Correct"?"NOT_CORRECT":assisted?"ASSISTED":duplicate?"ROLLING_24H":source?.ReviewConfirmation?.Action=="AdoptMeasuredTarget"?"TRUSTED_TARGET_CONFIRMED":"TRUSTED_INDEPENDENT";
            rows.Add(new(a.Id,s.Id,s.TaskId,s.QuestionId,a.CreatedAt,grade?.Id,grade?.CreatedAt,grade?.Result??"Pending",graded,passed,reason));
        }
        var count=rows.Count(r=>r.Graded);var independent=rows.Count(r=>r.IndependentlyPassed);
        return new(rows.Count,count,independent,rows.Count(r=>!r.Graded),count==0?null:(decimal)independent/count,rows.ToArray(),
            "按学生时区内实际作答日期统计复习任务的首次遇题答案，采用查看时最新有效判分；正确、错误及部分正确进入已判分分母。提示、看答案、24小时内重复原题或实际题目不再测量任务原知识点不计独立通过；目标变化交家长核对，重试不能替代首次答案。此处通过率要求整题独立正确，知识点日程推进另核对该目标的全部可测步骤。未作答不进入通过率分母，待判分另列；空分母不推定100%。这不是到期复习覆盖率，也不是过去时刻的判分快照。");
    }
    public static async Task<ReviewPassSummary> Read(Database db,Guid studentId,string zone,DateOnly start,DateOnly end,CancellationToken ct)
    {
        var attempts=await db.Attempts.Where(a=>a.StudentId==studentId && a.Number==1).OrderBy(a=>a.Sequence).ToListAsync(ct);
        var sessions=await db.Sessions.Where(s=>s.StudentId==studentId).ToListAsync(ct);var taskIds=sessions.Select(s=>s.TaskId).Distinct().ToArray();
        var tasks=await db.Tasks.Where(t=>t.StudentId==studentId && taskIds.Contains(t.Id)).ToListAsync(ct);var ids=attempts.Select(a=>a.Id).ToArray();
        var grades=await db.Gradings.Where(g=>ids.Contains(g.AttemptId)).ToListAsync(ct);
        var student=await db.Students.SingleAsync(s=>s.Id==studentId,ct);var (inputs,_)=await Assessment.LoadInputs(db,student,ct);
        return Calculate(attempts,sessions,tasks,grades,zone,start,end,inputs.ToDictionary(i=>i.Attempt.Id));
    }
}
