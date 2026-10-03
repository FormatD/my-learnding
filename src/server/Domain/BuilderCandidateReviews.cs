using Microsoft.EntityFrameworkCore;
namespace Learning;
public record CandidateReviewSource(Guid Id,string Title,string UsageScope,string Hash,Chunk[] Fragments);
public record CandidateReviewMatch(Match Match,KC? Definition,int SupportCount,Question[] SupportQuestions,Question[] ContrastQuestions);
public record CandidateReviewContext(Candidate Candidate,BuilderRun Run,CandidateReviewSource Source,BuilderCandidateOutput? Protocol,int? LibraryNumber,bool LibraryWithdrawn,CandidateReviewMatch[] Matches,string Notice);
public static class BuilderCandidateReviews
{
    public static async Task<CandidateReviewContext> Read(Database db,Actor actor,Guid id,CancellationToken ct=default)
    {
        actor.Require("ContentEditor");
        var candidate=await db.Candidates.SingleOrDefaultAsync(c=>c.Id==id && c.FamilyId==actor.FamilyId,ct)??throw new ApiError(404,"NOT_FOUND","候选不存在。");
        var run=await db.BuilderRuns.SingleAsync(r=>r.Id==candidate.RunId && r.FamilyId==actor.FamilyId,ct);
        var source=await db.Sources.SingleAsync(s=>s.Id==run.SourceId && s.FamilyId==actor.FamilyId,ct);
        BuilderCandidateOutput? protocol;
        try{protocol=candidate.ProtocolPayload==null?null:Json.Read<BuilderCandidateOutput>(candidate.ProtocolPayload);}
        catch(System.Text.Json.JsonException){throw new ApiError(422,"CANDIDATE_PROTOCOL_UNKNOWN","原候选结构输出无法读取，请核对运行记录。");}
        if(protocol==null && run.InputVersion=="builder-input/3")throw new ApiError(422,"CANDIDATE_PROTOCOL_UNKNOWN","原候选缺少本次结构输出，请核对运行记录；不会补造年级或评分。");
        if(protocol!=null && (protocol.SourceChunkIds==null || protocol.SourceChunkIds.Length==0))throw new ApiError(422,"BUILDER_SOURCE_INVALID","原候选缺少来源引用，请核对运行记录。");
        var ids=protocol?.SourceChunkIds??[candidate.ChunkId];
        var fragments=await db.Chunks.Where(c=>c.FamilyId==actor.FamilyId && c.SourceId==source.Id && ids.Contains(c.Id)).OrderBy(c=>c.Locator).ThenBy(c=>c.Id).ToArrayAsync(ct);
        if(!ids.Contains(candidate.ChunkId) || fragments.Length!=ids.Distinct().Count())throw new ApiError(422,"SOURCE_QUOTE_INVALID","原候选引用与来源片段不一致，请核对原运行记录。");
        if(protocol!=null)BuilderProtocol.Validate("{\"schemaVersion\":\"kc-candidate/1\",\"candidates\":["+candidate.ProtocolPayload+"]}",fragments.Select(c=>new BuilderFragment(c.Id,c.Text)).ToArray());
        if(string.IsNullOrWhiteSpace(candidate.Quote) || !fragments.Single(c=>c.Id==candidate.ChunkId).Text.Contains(candidate.Quote,StringComparison.Ordinal) || protocol!=null && (ids[0]!=candidate.ChunkId || protocol.SupportingQuotes[0]!=candidate.Quote))throw new ApiError(422,"SOURCE_QUOTE_INVALID","原候选主引用与来源不一致，请核对运行记录。");
        var library=run.LibraryReleaseId==null?null:await db.Releases.SingleAsync(r=>r.Id==run.LibraryReleaseId && r.FamilyId==actor.FamilyId,ct);
        var catalog=library==null?null:Json.Read<Catalog>(library.Payload);
        bool Measures(Question q,Guid kc)=>q.Policy!="NoEvidence" && q.Mappings.Any(m=>m.KCId==kc && m.Share>0 && m.Mode is "WholeItem" or "StepObserved" && m.Role is not "Context" and not "Prerequisite");
        var matches=Json.Read<Match[]>(candidate.Matches).Select(match=>
        {
            var definition=catalog?.Kcs.SingleOrDefault(k=>k.Id==match.KCId);
            var questions=definition==null?[]:catalog!.Questions.Where(q=>Measures(q,match.KCId)).ToArray();
            var contrasts=definition==null?[]:catalog!.Questions.Where(q=>q.Policy!="NoEvidence" && !Measures(q,match.KCId)).Take(2).ToArray();
            return new CandidateReviewMatch(match,definition,questions.Length,questions.Take(3).ToArray(),contrasts);
        }).ToArray();
        return new(candidate,run,new(source.Id,source.Title,source.UsageScope,source.Hash,fragments),protocol,library?.Number,library?.Withdrawn??false,matches,protocol==null?"旧候选未保存完整结构输出，仅展示实际保留的来源与检索结果，不补造年级或模型评分。":"展示创建时固定正式内容中的定义和测量题；对照题仅用于核对其他测量目标，不是当前能力的独立证据。模拟检索分数不能解释为通过概率。");
    }
    public static void Map(RouteGroupBuilder api)=>api.MapGet("/builder/candidates/{id:guid}/review-context",async(Guid id,Database db,HttpContext ctx,CancellationToken ct)=>
    {
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var context=await Read(db,ctx.Actor(),id,ct);await tx.CommitAsync(ct);return TypedResults.Ok(context);
    });
}
