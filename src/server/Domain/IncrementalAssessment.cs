namespace Learning;
public record IncrementalOutcome(DateTimeOffset Time,bool Positive);
public record IncrementalEncounter(Guid Id,DateTimeOffset Time);
public record IncrementalDailyCap(Guid Group,DateOnly Day,decimal Weight);
public record IncrementalSkill(Mastery Value,Guid[] Questions,Guid[] Sessions,string[] Coverage,IncrementalOutcome[] Outcomes,bool Delay2,bool Delay7,bool Delay30,Guid[] DiagnosticPasses);
public record IncrementalAssessmentSnapshot(string Version,Guid FamilyId,Guid StudentId,Guid GenerationId,string TimeZone,string EvidenceRule,string MasteryModel,string ReviewRule,long InputCount,long LastSequence,Guid LastAttemptId,TeachingAnchor[] Teaching,IncrementalSkill[] Skills,IncrementalEncounter[] Encounters,IncrementalEncounter[] LastKC,Guid[] Variants,IncrementalDailyCap[] DailyPositive,Evidence[] Evidence,AssessmentContext[] Contexts,Review[] Reviews,ReviewOccurrence[] ReviewHistory,MasteryEvent[] MasteryHistory);
// Independent streaming transition engine. Assessment.Replay remains the full replay oracle.
public sealed class IncrementalAssessment
{
    public const string Version="assessment-incremental/1";
    readonly Guid family,student,generation;readonly string zone;
    readonly TeachingAnchor[] anchors;
    readonly List<Evidence> evidence=[];readonly List<AssessmentContext> contexts=[];
    readonly Dictionary<Guid,Stats> stats=[];
    readonly Dictionary<(string,Guid),Review> reviews=[];
    readonly Dictionary<Guid,DateTimeOffset> encounters=[],lastKC=[];
    readonly HashSet<Guid> variants=[];
    readonly Dictionary<(Guid,DateOnly),decimal> dailyPositive=[];
    readonly List<ReviewOccurrence> reviewHistory=[];readonly List<MasteryEvent> masteryHistory=[];
    readonly Dictionary<(string,Guid),int> activeHistory=[];
    long count,lastSequence;Guid lastAttempt;bool faulted;
    public long ProcessedInputs=>count;
    sealed class Stats
    {
        public Mastery Value=new();public HashSet<Guid> Questions=[],Sessions=[],DiagnosticPasses=[];public HashSet<string> Coverage=[];
        public List<IncrementalOutcome> Outcomes=[];public bool Delay2,Delay7,Delay30;
    }
    public IncrementalAssessment(Guid familyId,Guid studentId,Guid generationId,string timeZone,IEnumerable<TeachingAnchor>? teaching=null)
    {family=familyId;student=studentId;generation=generationId;zone=timeZone;TimeZoneInfo.FindSystemTimeZoneById(zone);anchors=(teaching??[]).OrderBy(t=>t.Time).ThenBy(t=>t.KCId).ToArray();}
    public static IncrementalAssessment Resume(string payload)
    {
        var s=Json.Read<IncrementalAssessmentSnapshot>(payload);
        if(s.Version!=Version || s.EvidenceRule!=Assessment.EvidenceRuleVersion || s.MasteryModel!=Assessment.MasteryModelVersion || s.ReviewRule!=Assessment.ReviewRuleVersion || s.InputCount!=s.Contexts.Length)throw new ApiError(422,"INCREMENTAL_STATE_INVALID","增量状态版本或输入记录不一致，需要完整重建。");
        if(s.InputCount<0 || s.Contexts.Select(c=>c.AttemptId).Distinct().Count()!=s.Contexts.Length || s.Skills.Any(k=>k.Value.FamilyId!=s.FamilyId || k.Value.StudentId!=s.StudentId || k.Value.GenerationId!=s.GenerationId) || s.Contexts.Any(c=>c.FamilyId!=s.FamilyId || c.StudentId!=s.StudentId || c.GenerationId!=s.GenerationId) || s.Evidence.Any(e=>e.FamilyId!=s.FamilyId || e.StudentId!=s.StudentId || e.GenerationId!=s.GenerationId || !s.Contexts.Any(c=>c.Id==e.ContextId && c.AttemptId==e.AttemptId && c.GradingRevisionId==e.GradingId)) || s.Reviews.Any(r=>r.FamilyId!=s.FamilyId || r.StudentId!=s.StudentId || r.GenerationId!=s.GenerationId))throw new ApiError(422,"INCREMENTAL_STATE_INVALID","增量状态的学生、世代或证据引用不一致。");
        var engine=new IncrementalAssessment(s.FamilyId,s.StudentId,s.GenerationId,s.TimeZone,s.Teaching){count=s.InputCount,lastSequence=s.LastSequence,lastAttempt=s.LastAttemptId};
        foreach(var skill in s.Skills)engine.stats.Add(skill.Value.KCId,new(){Value=skill.Value,Questions=skill.Questions.ToHashSet(),Sessions=skill.Sessions.ToHashSet(),Coverage=skill.Coverage.ToHashSet(),Outcomes=skill.Outcomes.ToList(),Delay2=skill.Delay2,Delay7=skill.Delay7,Delay30=skill.Delay30,DiagnosticPasses=skill.DiagnosticPasses.ToHashSet()});
        foreach(var row in s.Encounters)engine.encounters.Add(row.Id,row.Time);foreach(var row in s.LastKC)engine.lastKC.Add(row.Id,row.Time);engine.variants.UnionWith(s.Variants);foreach(var row in s.DailyPositive)engine.dailyPositive.Add((row.Group,row.Day),row.Weight);
        engine.evidence.AddRange(s.Evidence);engine.contexts.AddRange(s.Contexts);foreach(var row in s.Reviews)engine.reviews.Add((row.TargetType,row.TargetId),row);engine.reviewHistory.AddRange(s.ReviewHistory);engine.masteryHistory.AddRange(s.MasteryHistory);
        for(var i=0;i<engine.reviewHistory.Count;i++){var row=engine.reviewHistory[i];if(row.ClosedAt==null)engine.activeHistory.Add((row.TargetType,row.TargetId),i);}return engine;
    }
    public static IncrementalAssessment Fork(string payload,Guid newGeneration)
    {
        var decoded=Resume(payload);var snapshot=Json.Read<IncrementalAssessmentSnapshot>(decoded.Freeze());var now=DateTimeOffset.UtcNow;var contextIds=snapshot.Contexts.ToDictionary(c=>c.Id,_=>Guid.NewGuid());
        foreach(var c in snapshot.Contexts){c.Id=contextIds[c.Id];c.GenerationId=newGeneration;c.CreatedAt=now;}
        foreach(var e in snapshot.Evidence){e.Id=Guid.NewGuid();e.GenerationId=newGeneration;e.CreatedAt=now;e.ContextId=contextIds[e.ContextId!.Value];}
        foreach(var skill in snapshot.Skills){skill.Value.Id=Guid.NewGuid();skill.Value.GenerationId=newGeneration;skill.Value.CreatedAt=now;}
        foreach(var r in snapshot.Reviews){r.Id=Guid.NewGuid();r.GenerationId=newGeneration;r.CreatedAt=now;}
        return Resume(Json.Write(snapshot with{GenerationId=newGeneration}));
    }
    public string Freeze(){if(faulted)throw new ApiError(422,"INCREMENTAL_STATE_INVALID","本批计算已失败，不能保存状态。");return Json.Write(new IncrementalAssessmentSnapshot(Version,family,student,generation,zone,Assessment.EvidenceRuleVersion,Assessment.MasteryModelVersion,Assessment.ReviewRuleVersion,count,lastSequence,lastAttempt,anchors,stats.OrderBy(p=>p.Key).Select(p=>new IncrementalSkill(p.Value.Value,p.Value.Questions.Order().ToArray(),p.Value.Sessions.Order().ToArray(),p.Value.Coverage.Order().ToArray(),p.Value.Outcomes.ToArray(),p.Value.Delay2,p.Value.Delay7,p.Value.Delay30,p.Value.DiagnosticPasses.Order().ToArray())).ToArray(),encounters.OrderBy(p=>p.Key).Select(p=>new IncrementalEncounter(p.Key,p.Value)).ToArray(),lastKC.OrderBy(p=>p.Key).Select(p=>new IncrementalEncounter(p.Key,p.Value)).ToArray(),variants.Order().ToArray(),dailyPositive.OrderBy(p=>p.Key.Item1).ThenBy(p=>p.Key.Item2).Select(p=>new IncrementalDailyCap(p.Key.Item1,p.Key.Item2,p.Value)).ToArray(),evidence.ToArray(),contexts.ToArray(),reviews.Values.ToArray(),reviewHistory.ToArray(),masteryHistory.ToArray()));}
    public AssessmentOutput Output(){if(faulted)throw new ApiError(422,"INCREMENTAL_STATE_INVALID","本批计算已失败，不能使用结果。");return new(evidence,stats.Values.Select(s=>s.Value).ToList(),reviews.Values.ToList(),reviewHistory,masteryHistory,contexts);}
    DateOnly Local(DateTimeOffset time)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time,TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
    Review Schedule(string type,Guid target,Guid? kc,DateOnly day)
    {
        if(reviews.TryGetValue((type,target),out var old))return old;
        var row=new Review{FamilyId=family,StudentId=student,GenerationId=generation,TargetType=type,TargetId=target,KCId=kc,DueDate=day.AddDays(2)};reviews.Add((type,target),row);return row;
    }
    void ObserveHistory(Attempt attempt,Question question,StudyTask task,DateOnly day,Guid? confirmedTarget)
    {
        foreach(var i in activeHistory.Values.ToArray()){var row=reviewHistory[i];var matches=row.TargetType=="WrongQuestion"?row.TargetId==question.Id:task.Type=="Review" && (confirmedTarget??task.ReviewTargetId)==row.TargetId && ReviewTargets.Measures(question,row.TargetId);if(matches && day>=row.DueDate && row.ExecutedAt==null)reviewHistory[i]=row with{ExecutedAttemptId=attempt.Id,ExecutedAt=attempt.CreatedAt};}
    }
    void SynchronizeHistory(Attempt attempt)
    {
        foreach(var review in reviews.Values){var key=(review.TargetType,review.TargetId);if(activeHistory.TryGetValue(key,out var i)){var old=reviewHistory[i];if(review.Status=="Pending" && old.Stage==review.Stage && old.DueDate==review.DueDate && old.KCId==review.KCId)continue;reviewHistory[i]=old with{ClosedAt=attempt.CreatedAt};activeHistory.Remove(key);}if(review.Status!="Pending")continue;activeHistory[key]=reviewHistory.Count;reviewHistory.Add(new($"{review.TargetType}:{review.TargetId:N}:{attempt.Id:N}:{reviewHistory.Count}",review.TargetType,review.TargetId,review.KCId,review.Stage,review.DueDate,attempt.Id,attempt.CreatedAt,null,null,null));}
    }
    public void Append(AssessmentInput input)
    {
        if(count>0 && (input.Attempt.Sequence<lastSequence || input.Attempt.Sequence==lastSequence && input.Attempt.Id.CompareTo(lastAttempt)<=0))throw new ApiError(422,"INCREMENTAL_ORDER_INVALID","增量输入不是原序列的严格后缀，需要重新重建。");
        if(input.Attempt.FamilyId!=family || input.Attempt.StudentId!=student)throw new ApiError(422,"INCREMENTAL_CONTEXT_INVALID","增量输入不属于当前家庭学生。");
        if(faulted)throw new ApiError(422,"INCREMENTAL_STATE_INVALID","本批计算已失败，请从已提交状态恢复。");
        try{Apply(input);}catch{faulted=true;throw;}count++;lastSequence=input.Attempt.Sequence;lastAttempt=input.Attempt.Id;
    }
    void Apply(AssessmentInput input)
    {

            var a=input.Attempt; var q=input.Question; var g=input.Grade; var s=input.Session;
            var context=new AssessmentContext{FamilyId=family,StudentId=student,GenerationId=generation,AttemptId=a.Id,GradingRevisionId=g.Id,MappingSetRevisionId=input.MappingSetRevisionId,QuestionRevisionId=q.RevisionId,ContentReleaseId=s.ReleaseId,MappingReleaseId=input.MappingReleaseId??s.ReleaseId,CorrectionBatchId=input.CorrectionBatchId,GradingCorrectionBatchId=input.GradingCorrectionBatchId,MappingCorrectionBatchId=input.MappingCorrectionBatchId,EvidenceRuleVersion=Assessment.EvidenceRuleVersion,MappingSource=input.MappingSetRevisionId!=null?"FixedContainer":"LegacySnapshot",EvidencePolicy=q.Policy,AdmissionStatus=a.Number!=1?"RetryExcluded":g.Result is not "Correct" and not "Incorrect" and not "Partial"?"Pending":q.Policy=="NoEvidence"?"NoEvidence":q.Policy=="ObservedSteps"?"ObservedSteps":"Eligible"};contexts.Add(context);
            if (a.Number != 1) return; // Never substitute a later retry for an ungraded first answer.
            var time=a.CreatedAt; var day=Local(time);
            // Execution is a submitted first answer, even if pending or assisted; it is not a pass.
            ObserveHistory(a,q,input.Task,day,ReviewTargets.Target(input));
            var duplicate=encounters.TryGetValue(q.Id,out var prior) && time-prior < TimeSpan.FromHours(24);
            var novelty=encounters.ContainsKey(q.Id) ? .5m : q.VariantGroupId.HasValue && variants.Contains(q.VariantGroupId.Value) ? .8m : 1m;
            if (!duplicate) encounters[q.Id]=time;
            if (q.VariantGroupId.HasValue) variants.Add(q.VariantGroupId.Value);
            var trusted=g.Result is "Correct" or "Incorrect" or "Partial";
            var independentlyCorrect=trusted && ReviewTargets.WholeIndependent(input) && !duplicate;
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
                if (!duplicate && ReviewTargets.Target(input) is Guid target && ReviewTargets.IndependentPass(input,target) && reviews.TryGetValue(("KC",target),out var kr) && day>=kr.DueDate)
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
                evidence.Add(new Evidence { FamilyId=family, StudentId=student, GenerationId=generation,ContextId=context.Id, AttemptId=a.Id, GradingId=g.Id, ReleaseId=s.ReleaseId, MappingReleaseId=input.MappingReleaseId??s.ReleaseId,MappingSetRevisionId=input.MappingSetRevisionId,CorrectionBatchId=input.CorrectionBatchId, KCId=kc.Id, KCRevisionId=kc.RevisionId, Part=m.Step??"WholeItem", Positive=positive, RawWeight=raw, Weight=weight, OccurredAt=time, Factors=Json.Write(new { share=m.Share, quality=1, difficulty, independence, delayDays=delay, delayFactor, novelty, suppressed, suppressedWeight=raw-weight, rule=Assessment.EvidenceRuleVersion }) });
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
                if (independent) state.Outcomes.Add(new(time,positive));
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
            SynchronizeHistory(a);
                foreach(var id in mappings.Select(m=>m.KCId).Distinct())
                {
                    var state=stats[id];var v=state.Value;var kc=input.Kcs.Single(k=>k.Id==id);
                    masteryHistory.Add(new(a.Id,time,new(id,kc.RevisionId,kc.Name,v.Status,v.NeedsRecheck,v.Probability,v.Confidence,v.EffectiveEvidence,v.DistinctQuestions,(kc.RequiredCoverage??["Basic"]).Order().ToArray(),state.Coverage.Order().ToArray(),state.Delay2,state.Delay7,state.Delay30,v.Reason)));
                }
    }
}
