using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace Learning;

public class BuilderSemanticPreparation:Row
{
    public Guid CandidateId {get;set;}
    public Guid RunId {get;set;}
    public Guid JobId {get;set;}
    public Guid RequestedBy {get;set;}
    public Guid? LibraryReleaseId {get;set;}
    public string Snapshot {get;set;}="";
    public string SnapshotHash {get;set;}="";
    public string InputHash {get;set;}="";
}
public record BuilderSemanticFrozenInput(string Version,Guid FamilyId,Guid CandidateId,Guid RunId,Guid RequestedBy,Guid? LibraryReleaseId,string CandidatePayload,string RunSnapshot,string SourceSnapshot,string? LibraryHash,string ModelInputPayload,string ModelConfigPayload,string ModelConfigHash);

// Queue preparation and revalidation only. No production endpoint/consumer until durable calls are connected.
public static class BuilderSemanticJobs
{
    public const string Type="BuilderSemanticDecision";
    static ApiError Changed()=>new(422,"JOB_INPUT_SNAPSHOT_CHANGED","候选、来源、原能力库或固定语义输入已变化，请重新准备。");
    public static BuilderSemanticLocalConfiguration Current(IConfiguration configuration)=>new("semantic-local/1",configuration["Omlx:Endpoint"]??"http://127.0.0.1:8000/v1",configuration["Omlx:Model"]??"",configuration.GetValue<int?>("Omlx:MaxOutputTokens")??4096,configuration.GetValue<int?>("Omlx:TimeoutMilliseconds")??600000,BuilderSemanticProtocol.PromptHash);
    static async Task<BuilderSemanticFrozenInput> Capture(Database db,Actor actor,Guid candidateId,BuilderSemanticLocalConfiguration config,CancellationToken ct)
    {
        config.Validate();var context=await BuilderCandidateReviews.Read(db,actor,candidateId,ct);
        if(context.Candidate.Status!="Pending")throw new ApiError(422,"CANDIDATE_ALREADY_REVIEWED","候选已经处理，不能替换原人工决定。");
        if(context.Run.Status!="Completed" || context.Run.Type!="Candidates" || context.Protocol==null)throw new ApiError(422,"CANDIDATE_PROTOCOL_REQUIRED","需要已完成任务中有完整原结构输出的待审候选。");
        if(context.LibraryWithdrawn)throw new ApiError(422,"LIBRARY_WITHDRAWN","候选原能力库已撤回，请重新准备候选。");
        var release=context.Run.LibraryReleaseId==null?null:await db.Releases.AsNoTracking().SingleOrDefaultAsync(r=>r.FamilyId==actor.FamilyId && r.Id==context.Run.LibraryReleaseId,ct)??throw Changed();
        var library=release==null?new Catalog([],[],[],[],[]):Json.Read<Catalog>(release.Payload);
        var input=BuilderSemanticProtocol.Capture(candidateId,context.Protocol,context.Source.Fragments.Select(f=>new BuilderFragment(f.Id,f.Text)).ToArray(),library,context.Matches.Select(m=>m.Match).ToArray());
        var source=await db.Sources.AsNoTracking().SingleAsync(s=>s.Id==context.Source.Id && s.FamilyId==actor.FamilyId,ct);
        var sourceSnapshot=Content.Hash(Json.Write(new{source.Id,source.Title,source.Text,source.Hash,source.UsageScope,source.AllowExternalAI,fragments=context.Source.Fragments.OrderBy(f=>f.Id).Select(f=>new{f.Id,f.SourceId,f.Locator,f.Text}).ToArray()}));
        var payload=Json.Write(config);var c=context.Candidate;
        var candidatePayload=Json.Write(new{c.Id,c.RunId,c.ChunkId,c.Name,c.Behavior,c.Boundary,c.Type,c.Quote,c.Status,c.SuggestedAction,c.ProtocolPayload,c.Matches,c.Decision,c.ReviewReason,c.ExistingKCId,c.CreatedDraftId,c.CreatedKCId,c.ReviewedBy,c.ReviewedAt});
        return new("semantic-job/1",actor.FamilyId,candidateId,context.Run.Id,actor.Id,context.Run.LibraryReleaseId,candidatePayload,BackgroundJobs.Snapshot(context.Run),sourceSnapshot,release==null?null:Content.Hash(release.Payload),BuilderSemanticProtocol.UserFor(input),payload,Content.Hash(payload));
    }
    public static async Task<BuilderSemanticPreparation> Enqueue(Database db,Actor actor,Guid candidateId,CancellationToken ct=default)
    {
        actor.Require("ContentEditor");var frozen=await Capture(db,actor,candidateId,Current(db.RuntimeConfiguration),ct);var snapshot=Json.Write(frozen);var hash=Content.Hash(snapshot);
        var previous=await db.Set<BuilderSemanticPreparation>().SingleOrDefaultAsync(p=>p.FamilyId==actor.FamilyId && p.InputHash==hash,ct);if(previous!=null)return previous;
        var preparation=new BuilderSemanticPreparation{FamilyId=actor.FamilyId,CandidateId=candidateId,RunId=frozen.RunId,RequestedBy=actor.Id,LibraryReleaseId=frozen.LibraryReleaseId,Snapshot=snapshot,SnapshotHash=hash,InputHash=hash};
        var job=new BackgroundJob{FamilyId=actor.FamilyId,Type=Type,InputRef=preparation.Id,IdempotencyKey="semantic:"+preparation.Id,InputPayload=snapshot,InputHash=hash};preparation.JobId=job.Id;db.AddRange(preparation,job);
        db.Audits.Add(new(){FamilyId=actor.FamilyId,ActorId=actor.Id,Action="BuilderSemanticPreparationQueued",Details=Json.Write(new{preparationId=preparation.Id,preparation.JobId,candidateId,preparation.RunId,preparation.SnapshotHash})});return preparation;
    }
    public static BuilderSemanticLocalConfiguration ResolveLocal(BuilderSemanticFrozenInput frozen)
    {
        if(frozen.Version!="semantic-job/1" || frozen.ModelConfigPayload==null || Content.Hash(frozen.ModelConfigPayload)!=frozen.ModelConfigHash)throw Changed();
        try{var config=Json.Read<BuilderSemanticLocalConfiguration>(frozen.ModelConfigPayload)??throw Changed();config.Validate();return config;}catch(JsonException){throw Changed();}
    }
    // Both short capture and result commit must call this; current runtime configuration cannot replace a frozen model.
    public static async Task<(BuilderSemanticPreparation Preparation,BuilderSemanticFrozenInput Frozen,BuilderSemanticInput Input,Guid AuthorizedBy)> ValidateFrozen(Database db,JobLease lease,CancellationToken ct=default)
    {
        if(lease.Job.Type!=Type)throw Changed();
        var p=await db.Set<BuilderSemanticPreparation>().AsNoTracking().SingleOrDefaultAsync(p=>p.Id==lease.Job.InputRef && p.FamilyId==lease.Job.FamilyId,ct)??throw new ApiError(422,"JOB_INPUT_MISSING","语义任务原输入不存在。");
        if(p.JobId!=lease.Job.Id || p.Snapshot!=lease.Job.InputPayload || p.SnapshotHash!=lease.Job.InputHash || p.InputHash!=p.SnapshotHash || Content.Hash(p.Snapshot)!=p.SnapshotHash)throw Changed();
        BuilderSemanticFrozenInput f;try{f=Json.Read<BuilderSemanticFrozenInput>(p.Snapshot)??throw Changed();}catch(JsonException){throw Changed();}
        if(f.FamilyId!=p.FamilyId || f.CandidateId!=p.CandidateId || f.RunId!=p.RunId || f.RequestedBy!=p.RequestedBy || f.LibraryReleaseId!=p.LibraryReleaseId)throw Changed();var config=ResolveLocal(f);
        // Retry authorization will be added with the explicit retry endpoint; never accept an unaudited retry round.
        if(lease.Job.RetryRound!=0)throw new ApiError(422,"JOB_RETRY_AUTHORIZATION_MISSING","缺少本轮语义任务人工恢复依据。");
        var roles=(await db.Set<FamilyMembership>().AsNoTracking().SingleOrDefaultAsync(m=>m.FamilyId==p.FamilyId && m.AccountId==p.RequestedBy,ct))?.Roles??"";var actor=new Actor(Guid.Empty,p.FamilyId,p.RequestedBy,null,"Parent",roles);
        if(!actor.Can("ContentEditor"))throw new ApiError(422,"JOB_REQUESTER_FORBIDDEN","内容维护权限已变化，未保存语义建议。");
        // Clear prior tracked definitions before the second read; a model wait cannot reuse stale candidate/source rows.
        db.ChangeTracker.Clear();var current=await Capture(db,actor,p.CandidateId,config,ct);if(Json.Write(current)!=p.Snapshot)throw Changed();
        var input=Json.Read<BuilderSemanticInput>(f.ModelInputPayload);BuilderSemanticProtocol.ValidateInput(input);return(p,f,input,p.RequestedBy);
    }
}
