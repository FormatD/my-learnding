using Microsoft.EntityFrameworkCore;
namespace Learning;
public class AssessmentCheckpoint:Row
{
    public Guid StudentId {get;set;}
    public Guid GenerationId {get;set;}
    public string EngineVersion {get;set;}="";
    public string PrefixHash {get;set;}="";
    public long InputCount {get;set;}
    public string Payload {get;set;}="";
    public string PayloadHash {get;set;}="";
}
public record IncrementalPreparation(IncrementalAssessment Engine,string Mode,int ProcessedInputs,Guid? BaseGenerationId,string PrefixHash);
public static class AssessmentCheckpoints
{
    public static string PrefixHash(string zone,IEnumerable<AssessmentInput> input,TeachingAnchor[] teaching)=>Content.Hash(Json.Write(new{version=IncrementalAssessment.Version,inputVersion=Assessment.InputHashVersion,timeZone=zone,evidence=Assessment.EvidenceRuleVersion,mastery=Assessment.MasteryModelVersion,review=Assessment.ReviewRuleVersion,inputs=input.ToArray(),teaching}));
    public static async Task<IncrementalPreparation> Prepare(Database db,Student student,Guid target,AssessmentInput[] inputs,TeachingAnchor[] teaching,CancellationToken ct,bool forceFull=false)
    {
        var old=forceFull || student.ActiveGenerationId==null?null:await db.Set<AssessmentCheckpoint>().AsNoTracking().SingleOrDefaultAsync(c=>c.FamilyId==student.FamilyId && c.StudentId==student.Id && c.GenerationId==student.ActiveGenerationId,ct);
        IncrementalAssessment? engine=null;var mode="FullStream";var processed=inputs.Length;Guid? baseGeneration=null;
        if(old!=null)
        {
            if(Content.Hash(old.Payload)!=old.PayloadHash)throw new ApiError(422,"INCREMENTAL_STATE_INVALID","增量状态摘要不一致，不能静默使用或覆盖。");
            var state=Json.Read<IncrementalAssessmentSnapshot>(old.Payload);
            if(state.FamilyId!=student.FamilyId || state.StudentId!=student.Id || state.GenerationId!=old.GenerationId || state.InputCount!=old.InputCount)throw new ApiError(422,"INCREMENTAL_STATE_INVALID","增量状态不属于本学生评估。");
            if(old.EngineVersion==IncrementalAssessment.Version && state.EvidenceRule==Assessment.EvidenceRuleVersion && state.MasteryModel==Assessment.MasteryModelVersion && state.ReviewRule==Assessment.ReviewRuleVersion && old.InputCount<=inputs.Length && old.PrefixHash==PrefixHash(student.TimeZone,inputs.Take((int)old.InputCount),teaching))
            {
                engine=IncrementalAssessment.Fork(old.Payload,target);processed=inputs.Length-(int)old.InputCount;mode="IncrementalAppend";baseGeneration=old.GenerationId;
                foreach(var input in inputs.Skip((int)old.InputCount))engine.Append(input);
            }
            else mode="FullStreamChangedPrefix";
        }
        if(engine==null){engine=new IncrementalAssessment(student.FamilyId,student.Id,target,student.TimeZone,teaching);foreach(var input in inputs)engine.Append(input);}
        return new(engine,mode,processed,baseGeneration,PrefixHash(student.TimeZone,inputs,teaching));
    }
    public static void Save(Database db,Student student,Generation generation,IncrementalPreparation prepared)
    {
        var payload=prepared.Engine.Freeze();db.Add(new AssessmentCheckpoint{FamilyId=student.FamilyId,StudentId=student.Id,GenerationId=generation.Id,EngineVersion=IncrementalAssessment.Version,PrefixHash=prepared.PrefixHash,InputCount=prepared.Engine.ProcessedInputs,Payload=payload,PayloadHash=Content.Hash(payload)});
    }
}
