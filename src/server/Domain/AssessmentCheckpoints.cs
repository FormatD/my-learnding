using Microsoft.EntityFrameworkCore;
namespace Learning;
public class AssessmentCheckpoint:Row
{
    public string? StorageVersion {get;set;}
    public Guid? BaseCheckpointId {get;set;}
    public int? DeltaDepth {get;set;}
    public string? StatePayloadHash {get;set;}
    public string? InputHash {get;set;}
    public long? Cursor {get;set;}
    public string? CalculationMode {get;set;}
    public int? ProcessedInputCount {get;set;}
    public Guid StudentId {get;set;}
    public Guid GenerationId {get;set;}
    public string EngineVersion {get;set;}="";
    public string PrefixHash {get;set;}="";
    public long InputCount {get;set;}
    public string Payload {get;set;}="";
    public string PayloadHash {get;set;}="";
}
public record IncrementalPreparation(IncrementalAssessment Engine,string Mode,int ProcessedInputs,Guid? BaseGenerationId,string PrefixHash,AssessmentCheckpoint? StorageBase=null,string? BasePayload=null);
public static class AssessmentCheckpoints
{
    public static string PrefixHash(string zone,IEnumerable<AssessmentInput> input,TeachingAnchor[] teaching)=>AssessmentPrefixHashes.Compute(zone,input,teaching).Full;
    public static async Task<IncrementalPreparation> Prepare(Database db,Student student,Guid target,AssessmentInput[] inputs,TeachingAnchor[] teaching,CancellationToken ct,bool forceFull=false,bool online=false)
    {
        var old=forceFull || student.ActiveGenerationId==null?null:await db.Set<AssessmentCheckpoint>().AsNoTracking().Where(c=>c.FamilyId==student.FamilyId && c.StudentId==student.Id && c.GenerationId==student.ActiveGenerationId).OrderByDescending(c=>c.InputCount).FirstOrDefaultAsync(ct);
        IncrementalAssessmentSnapshot? state=null;string? decodedPayload=null;
        if(old!=null)
        {
            if(old.InputHash!=null && !await db.Generations.AnyAsync(g=>g.Id==old.GenerationId && g.FamilyId==student.FamilyId && g.StudentId==student.Id && g.InputHash==old.InputHash && g.Cursor==old.Cursor,ct))throw new ApiError(422,"INCREMENTAL_STATE_INVALID","当前评估与最近增量状态摘要不一致。");
            decodedPayload=await AssessmentCheckpointPayloads.Read(db,student,old,ct);
            state=Json.Read<IncrementalAssessmentSnapshot>(decodedPayload);
            if(state.FamilyId!=student.FamilyId || state.StudentId!=student.Id || state.GenerationId!=old.GenerationId || state.InputCount!=old.InputCount)throw new ApiError(422,"INCREMENTAL_STATE_INVALID","增量状态不属于本学生评估。");
        }
        var canCompare=old!=null && state!=null && old.EngineVersion==IncrementalAssessment.Version && state.EvidenceRule==Assessment.EvidenceRuleVersion && state.MasteryModel==Assessment.MasteryModelVersion && state.ReviewRule==Assessment.ReviewRuleVersion && old.InputCount<=inputs.Length;
        var hashes=AssessmentPrefixHashes.Compute(student.TimeZone,inputs,teaching,canCompare?Math.Max(0,old!.InputCount):null,ct);
        IncrementalAssessment? engine=null;var mode="FullStream";var processed=inputs.Length;Guid? baseGeneration=null;
        if(old!=null)
        {
            if(canCompare && old.PrefixHash==hashes.Prefix)
            {
                processed=inputs.Length-(int)old.InputCount;var append=online && target==old.GenerationId && processed>0;engine=append?IncrementalAssessment.Resume(decodedPayload!):IncrementalAssessment.Fork(decodedPayload!,target);mode=append?"OnlineAppend":"IncrementalAppend";baseGeneration=mode=="OnlineAppend"?null:old.GenerationId;
                foreach(var input in inputs.Skip((int)old.InputCount))engine.Append(input);
            }
            else mode="FullStreamChangedPrefix";
        }
        if(engine==null){engine=new IncrementalAssessment(student.FamilyId,student.Id,target,student.TimeZone,teaching);foreach(var input in inputs)engine.Append(input);}
        return new(engine,mode,processed,baseGeneration,hashes.Full,mode=="OnlineAppend"?old:null,mode=="OnlineAppend"?decodedPayload:null);
    }
    public static async Task<bool> ValidateResult(Database db,AssessmentRebuildResult result,CancellationToken ct)
    {
        if(!await db.Generations.AnyAsync(g=>g.Id==result.GenerationId && g.FamilyId==result.FamilyId && g.StudentId==result.StudentId,ct))return false;
        if(result.CheckpointId is Guid checkpoint)return await db.Set<AssessmentCheckpoint>().AnyAsync(c=>c.Id==checkpoint && c.FamilyId==result.FamilyId && c.StudentId==result.StudentId && c.GenerationId==result.GenerationId && c.InputHash==result.InputHash && c.Cursor==result.Cursor,ct);
        // Legacy results retain their original immutable application fact, even after online advancement.
        var ev=await db.Set<DomainEvent>().AsNoTracking().SingleOrDefaultAsync(e=>e.Id==result.AppliedEventId && e.FamilyId==result.FamilyId && e.StudentId==result.StudentId && e.AggregateId==result.GenerationId && e.EventType=="AssessmentApplied",ct);
        if(ev==null || Content.Hash(ev.Payload)!=ev.PayloadHash)return false;
        try{using var doc=System.Text.Json.JsonDocument.Parse(ev.Payload);var data=doc.RootElement.GetProperty("data");return data.GetProperty("inputHash").GetString()==result.InputHash && data.GetProperty("cursor").GetInt64()==result.Cursor;}
        catch(Exception ex)when(ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException){return false;}
    }
    public static AssessmentCheckpoint Save(Database db,Student student,Generation generation,IncrementalPreparation prepared)
    {
        var state=prepared.Engine.Freeze();var payload=state;Guid? baseId=null;var depth=0;
        if(prepared.Mode=="OnlineAppend" && prepared.StorageBase is { } prior && prepared.BasePayload is { } before && prior.GenerationId==generation.Id && prior.InputCount<prepared.Engine.ProcessedInputs && (prior.DeltaDepth??0)<AssessmentCheckpointPayloads.MaximumDepth)
        {
            var delta=AssessmentStateDeltas.Create(before,state);
            // Preserve exact bytes; unexpected serialization shapes stay as complete snapshots.
            if(delta.Length<state.Length && AssessmentStateDeltas.Apply(before,delta)==state){payload=delta;baseId=prior.Id;depth=(prior.DeltaDepth??0)+1;}
        }
        var checkpoint=new AssessmentCheckpoint{StorageVersion=AssessmentCheckpointPayloads.Version,BaseCheckpointId=baseId,DeltaDepth=depth,StatePayloadHash=Content.Hash(state),FamilyId=student.FamilyId,StudentId=student.Id,GenerationId=generation.Id,InputHash=generation.InputHash,Cursor=generation.Cursor,CalculationMode=prepared.Mode,ProcessedInputCount=prepared.ProcessedInputs,EngineVersion=IncrementalAssessment.Version,PrefixHash=prepared.PrefixHash,InputCount=prepared.Engine.ProcessedInputs,Payload=payload,PayloadHash=Content.Hash(payload)};db.Add(checkpoint);return checkpoint;
    }
}
