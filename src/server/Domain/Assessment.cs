using Microsoft.EntityFrameworkCore;

namespace Learning;
public record AssessmentInput(Attempt Attempt, LearningSession Session, Question Question, Grading Grade, KC[] Kcs, StudyTask Task,Guid? MappingReleaseId=null,Guid? CorrectionBatchId=null);
public record AssessmentOutput(List<Evidence> Evidence, List<Mastery> Masteries, List<Review> Reviews);
public record TeachingAnchor(Guid KCId,DateTimeOffset Time);
public static class Assessment
{
    public const string EvidenceRuleVersion="evidence/1.1";
    public const string MasteryModelVersion="mastery/1.1";
    public const string ReviewRuleVersion="review/1";
    private sealed class Stats
    {
        public Mastery Value = new();
        public HashSet<Guid> Questions = [];
        public HashSet<Guid> Sessions = [];
        public HashSet<string> Coverage = [];
        public List<(DateTimeOffset Time, bool Positive)> Outcomes = [];
        public bool Delay2, Delay7, Delay30;
        public HashSet<Guid> DiagnosticPasses = [];
    }
    public static AssessmentOutput Replay(Guid family, Guid student, Guid generation, string zone, IEnumerable<AssessmentInput> inputs,IEnumerable<TeachingAnchor>? teaching=null)
    {
        var evidence = new List<Evidence>();
        var stats = new Dictionary<Guid, Stats>();
        var reviews = new Dictionary<(string, Guid), Review>();
        var encounters = new Dictionary<Guid, DateTimeOffset>();
        var lastKC = new Dictionary<Guid, DateTimeOffset>();
        var variants = new HashSet<Guid>();
        var dailyPositive = new Dictionary<(Guid, DateOnly), decimal>();
        var anchors=(teaching??[]).OrderBy(t=>t.Time).ThenBy(t=>t.KCId).ToArray();
        DateOnly Local(DateTimeOffset time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
        Review Schedule(string type, Guid target, Guid? kc, DateOnly date)
        {
            if (reviews.TryGetValue((type,target), out var found)) return found;
            var value = new Review { FamilyId=family, StudentId=student, GenerationId=generation, TargetId=target, KCId=kc, TargetType=type, DueDate=date.AddDays(2) };
            reviews[(type,target)] = value; return value;
        }
        foreach (var input in inputs.OrderBy(x => x.Attempt.Sequence).ThenBy(x => x.Attempt.Id))
        {
            var a=input.Attempt; var q=input.Question; var g=input.Grade; var s=input.Session;
            if (a.Number != 1) continue; // Never substitute a later retry for an ungraded first answer.
            var time=a.CreatedAt; var day=Local(time);
            var duplicate=encounters.TryGetValue(q.Id,out var prior) && time-prior < TimeSpan.FromHours(24);
            var novelty=encounters.ContainsKey(q.Id) ? .5m : q.VariantGroupId.HasValue && variants.Contains(q.VariantGroupId.Value) ? .8m : 1m;
            if (!duplicate) encounters[q.Id]=time;
            if (q.VariantGroupId.HasValue) variants.Add(q.VariantGroupId.Value);
            var trusted=g.Result is "Correct" or "Incorrect" or "Partial";
            var independentlyCorrect=trusted && g.Result=="Correct" && a.HintLevel==0 && !a.AnswerShown && !duplicate;
            var primary=q.Mappings.FirstOrDefault(m => m.Mode!="None")?.KCId;
            if (trusted && !duplicate)
            {
                if (g.Result=="Incorrect")
                {
                    var r=Schedule("WrongQuestion",q.Id,primary,day); r.Stage="R1"; r.DueDate=day.AddDays(2); r.Status="Pending"; r.WrongCount++;
                }
                else if (reviews.TryGetValue(("WrongQuestion",q.Id),out var r) && r.Status!="Completed" && day>=r.DueDate)
                {
                    if (independentlyCorrect)
                    {
                        if (r.Stage=="R1") { r.Stage="R2"; r.DueDate=day.AddDays(5); }
                        else if (r.Stage=="R2") { r.Stage="R3"; r.DueDate=day.AddDays(23); }
                        else { r.Status="Completed"; if (r.KCId.HasValue) { var low=Schedule("KC",r.KCId.Value,r.KCId,day); low.Stage="LowFrequency"; low.DueDate=day.AddDays(30); } }
                    }
                    else if (g.Result=="Correct") r.DueDate=day.AddDays(2);
                }
                if (independentlyCorrect && input.Task.ReviewTargetId is Guid target && reviews.TryGetValue(("KC",target),out var kr) && day>=kr.DueDate)
                { kr.Stage="LowFrequency"; kr.DueDate=day.AddDays(30); }
            }
            var mappings=q.Policy=="NoEvidence" ? [] : q.Mappings.Where(m => m.Mode!="None" && m.Role is not "Prerequisite" and not "Context").ToArray();
            var signals=new List<(Guid KCId,bool Positive,bool Independent,decimal Delay)>();
            foreach (var m in mappings)
            {
                var kc=input.Kcs.Single(k => k.Id==m.KCId);
                if (!stats.TryGetValue(kc.Id,out var state)) stats[kc.Id]=state=new Stats { Value=new Mastery { FamilyId=family, StudentId=student, GenerationId=generation, KCId=kc.Id } };
                var observed=Json.Read<ObservedStep[]>(g.Steps).FirstOrDefault(x => x.Step==m.Step);
                var result=m.Mode=="StepObserved" ? observed?.Result : g.Result;
                var hint=m.Mode=="StepObserved" ? Math.Max(a.HintLevel,observed?.HintLevel ?? 0) : a.HintLevel;
                if (result is not "Correct" and not "Incorrect" || !trusted) continue;
                var positive=result=="Correct";
                var last=lastKC.GetValueOrDefault(kc.Id);
                var taught=anchors.LastOrDefault(t=>t.KCId==kc.Id && t.Time<=time);
                if(taught!=null && taught.Time>last)last=taught.Time;
                var delay=last==default ? 0 : (decimal)(time-last).TotalDays;
                var difficulty=positive ? q.Difficulty=="Hard" ? 1.2m : q.Difficulty=="Easy" ? .8m : 1m : q.Difficulty=="Easy" ? 1.2m : q.Difficulty=="Hard" ? .8m : 1m;
                var independence=positive ? a.AnswerShown ? 0m : hint>=2 ? .4m : hint==1 ? .7m : 1m : 1m;
                var delayFactor=positive && input.Task.Type=="Review" ? delay>=30 ? 1.4m : delay>=7 ? 1.25m : delay>=2 ? 1.1m : 1m : 1m;
                var raw=m.Share*difficulty*independence*delayFactor*novelty;
                var weight=duplicate ? 0m : raw;
                var suppressed=duplicate ? "ROLLING_24H" : "";
                if (positive && q.VariantGroupId is Guid group)
                {
                    var used=dailyPositive.GetValueOrDefault((group,day)); weight=Math.Min(weight,Math.Max(0,2-used));
                    if (weight<raw && !duplicate) suppressed="VARIANT_DAILY_CAP";
                    dailyPositive[(group,day)]=used+weight;
                }
                evidence.Add(new Evidence { FamilyId=family, StudentId=student, GenerationId=generation, AttemptId=a.Id, GradingId=g.Id, ReleaseId=s.ReleaseId, MappingReleaseId=input.MappingReleaseId??s.ReleaseId,CorrectionBatchId=input.CorrectionBatchId, KCId=kc.Id, KCRevisionId=kc.RevisionId, Part=m.Step??"WholeItem", Positive=positive, RawWeight=raw, Weight=weight, OccurredAt=time, Factors=Json.Write(new { share=m.Share, quality=1, difficulty, independence, delayDays=delay, delayFactor, novelty, suppressed, suppressedWeight=raw-weight, rule=EvidenceRuleVersion }) });
                if (weight<=0) continue;
                var v=state.Value; if (positive) v.Alpha+=weight; else v.Beta+=weight;
                v.EffectiveEvidence=v.Alpha+v.Beta-4; v.Probability=v.Alpha/(v.Alpha+v.Beta);
                state.Questions.Add(q.Id); state.Sessions.Add(s.Id); v.DistinctQuestions=state.Questions.Count;
                signals.Add((kc.Id,positive,hint==0 && !a.AnswerShown,delay));
            }
            // State transitions count one encounter per KC, even with several observed steps.
            foreach(var group in signals.GroupBy(x=>x.KCId))
            {
                var kc=input.Kcs.Single(k=>k.Id==group.Key);var state=stats[kc.Id];var v=state.Value;
                var independent=group.Any(x=>x.Independent);
                var positive=group.Where(x=>x.Independent).All(x=>x.Positive);
                var delay=group.Max(x=>x.Delay);
                if (independent) state.Outcomes.Add((time,positive));
                if (positive && independent)
                {
                    state.Coverage.Add(q.Coverage);
                    if (input.Task.Type=="Review") { state.Delay2 |= delay>=2; state.Delay7 |= delay>=7; state.Delay30 |= delay>=30; }
                    if (v.NeedsRecheck) state.DiagnosticPasses.Add(q.Id);
                }
                v.Confidence=v.EffectiveEvidence>=8 && state.Questions.Count>=5 && state.Sessions.Count>=3 && state.Delay2 ? "High" : v.EffectiveEvidence>=3 && state.Questions.Count>=3 && state.Sessions.Count>=2 ? "Medium" : "Low";
                var negative7=state.Outcomes.Where(x => x.Time>=time.AddDays(-7) && !x.Positive).ToArray();
                var recentPositive=state.Outcomes.Any(x => x.Positive && x.Time>=time.AddDays(-7));
                var coverage=(kc.RequiredCoverage??["Basic"]).All(state.Coverage.Contains);
                var promoted=v.Probability>=.85m && v.EffectiveEvidence>=8 && v.Confidence=="High" && coverage && state.Delay7 && negative7.Length==0 ? state.Delay30 ? "Stable" : "Mastered" : v.Probability>=.75m && v.EffectiveEvidence>=4 && v.Confidence!="Low" && recentPositive ? "CanDo" : "Learning";
                var priorStatus=v.Status;
                if (priorStatus is "Mastered" or "Stable" && !positive && independent)
                {
                    v.NeedsRecheck=true; state.DiagnosticPasses.Clear();
                    var diagnostic=Schedule("KC",kc.Id,kc.Id,day); diagnostic.Stage="Diagnostic"; diagnostic.DueDate=day.AddDays(1);
                }
                var recentNeg=state.Outcomes.Where(x => x.Time>=time.AddDays(-7)).TakeLast(3).Count(x => !x.Positive);
                if ((recentNeg>=2 || v.Probability<.65m && v.Confidence!="Low") && priorStatus is "CanDo" or "Mastered" or "Stable")
                { v.Status="Learning"; v.Reason="TRUSTED_FAILURES"; }
                else if (priorStatus is "Mastered" or "Stable" && promoted is "Learning" or "CanDo")
                { v.Status=priorStatus; v.Reason="HYSTERESIS_RECHECK"; }
                else if (priorStatus=="CanDo" && promoted=="Learning" && v.Probability>=.65m)
                { v.NeedsRecheck=true; v.Reason="CAN_DO_HYSTERESIS"; }
                else { v.Status=promoted; v.Reason="COVERAGE_AND_RETENTION"; }
                if (v.NeedsRecheck && state.DiagnosticPasses.Count>=2) { v.NeedsRecheck=false; v.Reason="DIAGNOSTIC_PASSED"; }
                v.Gaps=Json.Write(new { evidence=Math.Max(0,8-v.EffectiveEvidence), distinctQuestions=Math.Max(0,5-state.Questions.Count), requires7DayReview=!state.Delay7, requires30DayReview=!state.Delay30, requiredCoverage=(kc.RequiredCoverage??["Basic"]).Except(state.Coverage).ToArray() });
                if (!reviews.ContainsKey(("KC",kc.Id)) && v.Status=="CanDo") { var r=Schedule("KC",kc.Id,kc.Id,day); r.DueDate=day.AddDays(7); }
            }
            // An encounter/teaching event resets retention even if it could not supply evidence.
            foreach (var id in mappings.Select(m => m.KCId).Distinct()) lastKC[id]=time;
        }
        return new(evidence, stats.Values.Select(x => x.Value).ToList(),reviews.Values.ToList());
    }
    public static async Task Rebuild(Database db, Student student, CancellationToken ct=default)
    {
        var attempts=await db.Attempts.Where(a => a.StudentId==student.Id).OrderBy(a => a.Sequence).ToListAsync(ct);
        var sessions=await db.Sessions.Where(s => s.StudentId==student.Id).ToDictionaryAsync(s => s.Id,ct);
        var grades=await db.Gradings.Where(g => g.FamilyId==student.FamilyId).OrderBy(g => g.Number).ToListAsync(ct);
        var releases=await db.Releases.Where(r => r.FamilyId==student.FamilyId).ToDictionaryAsync(r => r.Id,ct);
        var tasks=await db.Tasks.Where(t => t.StudentId==student.Id).ToDictionaryAsync(t => t.Id,ct);
        var attemptIds=attempts.Select(a=>a.Id).ToArray();var corrections=await db.Set<CorrectionItem>().Where(c=>c.FamilyId==student.FamilyId && attemptIds.Contains(c.AttemptId)).OrderBy(c=>c.Sequence).ToListAsync(ct);
        var inputs=attempts.Select(a => { var s=sessions[a.SessionId];var correction=corrections.LastOrDefault(c=>c.AttemptId==a.Id);var mappingRelease=correction?.MappingReleaseId??s.ReleaseId; var catalog=Json.Read<Catalog>(releases[mappingRelease].Payload); return new AssessmentInput(a,s,catalog.Questions.Single(q => q.Id==s.QuestionId),grades.Last(g => g.AttemptId==a.Id),catalog.Kcs,tasks[s.TaskId],mappingRelease,correction?.BatchId); }).ToArray();
        var teaching=tasks.Values.Where(t=>t.Type=="Resource" && t.KCId!=null && t.CompletedAt!=null).Select(t=>new TeachingAnchor(t.KCId!.Value,t.CompletedAt!.Value)).OrderBy(t=>t.Time).ThenBy(t=>t.KCId).ToArray();
        var hash=Content.Hash(Json.Write(new {inputs,teaching,rule=EvidenceRuleVersion,model=MasteryModelVersion,review=ReviewRuleVersion}));
        if (student.ActiveGenerationId.HasValue && await db.Generations.AnyAsync(g => g.Id==student.ActiveGenerationId && g.InputHash==hash,ct)) return;
        var gen=new Generation { FamilyId=student.FamilyId,StudentId=student.Id,InputHash=hash,RuleVersion=EvidenceRuleVersion,ModelVersion=MasteryModelVersion,Cursor=attempts.LastOrDefault()?.Sequence??0 };
        db.Generations.Add(gen);
        var output=Replay(student.FamilyId,student.Id,gen.Id,student.TimeZone,inputs,teaching);
        db.Evidence.AddRange(output.Evidence); db.Masteries.AddRange(output.Masteries); db.Reviews.AddRange(output.Reviews);
        if (student.ActiveGenerationId.HasValue) (await db.Generations.SingleAsync(g => g.Id==student.ActiveGenerationId,ct)).Status="Retired";
        gen.Status="Active"; student.ActiveGenerationId=gen.Id;
        await db.SaveChangesAsync(ct); // Binding and all projections are committed by the outer family transaction.
    }
}
