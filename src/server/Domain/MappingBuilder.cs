using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Learning;
public record MappingRunInput(Guid DraftId,Guid LibraryReleaseId,MappingOwnerSelection[] Owners,string Provider="Mock");
public record MappingDecisionInput(Guid SuggestionId,string Decision,string Reason,MappingProposal? CorrectedProposal=null);
public record MappingBatchInput(MappingDecisionInput[] Decisions);
public record MappingBatchResult(Guid RunId,Guid? DraftId,Guid[] DecisionIds,int Pending,string[] DraftValidationWarnings);
public record MappingQuality(int Suggested,int Pending,int Accepted,int Rejected,int Corrected,int Unchanged,string EvaluationStatus,string Note);
public record MappingRunSummary(Guid Id,Guid SourceDraftId,long SourceDraftVersion,string SourceTitle,Guid LibraryReleaseId,string Provider,string Model,string Status,DateTimeOffset CreatedAt);
public record MappingRunDetail(MappingRun Run,MappingSuggestion[] Suggestions,MappingReviewDecision[] Decisions,MappingReviewedSetDto[] Sets,MappingSetItem[] Items,KC[] Library,MappingQuality Quality);

public static class MappingBuilder
{
    static void Shape(Catalog source)
    {
        if(source.Kcs==null || source.Questions==null || source.Resources==null || source.Lessons==null || source.Relations==null || source.Kcs.Any(k=>k==null) || source.Questions.Any(q=>q==null || q.Mappings==null || q.Mappings.Any(m=>m==null)) || source.Resources.Any(r=>r==null || r.KCIds==null) || source.Lessons.Any(l=>l==null || l.KCIds==null) || source.Relations.Any(r=>r==null) || (source.Textbooks??[]).Any(b=>b==null) || (source.Units??[]).Any(u=>u==null) || (source.Courses??[]).Any(c=>c==null))
            throw new ApiError(422,"MAPPING_SOURCE_INVALID","草稿结构不完整，请先在内容编辑器核对并保存。");
        if(source.Kcs.Select(k=>k.Id).Distinct().Count()!=source.Kcs.Length || source.Questions.Select(q=>q.Id).Distinct().Count()!=source.Questions.Length || source.Resources.Select(r=>r.Id).Distinct().Count()!=source.Resources.Length || source.Lessons.Select(l=>l.Id).Distinct().Count()!=source.Lessons.Length)
            throw new ApiError(422,"MAPPING_SOURCE_INVALID","草稿中存在重复内容身份，请先核对。");
        if(source.Questions.GroupBy(q=>q.RevisionId).Any(g=>g.Count()>1) || source.Resources.Where(r=>r.RevisionId!=null).GroupBy(r=>r.RevisionId).Any(g=>g.Count()>1) || source.Lessons.Where(l=>l.RevisionId!=null).GroupBy(l=>l.RevisionId).Any(g=>g.Count()>1))
            throw new ApiError(422,"MAPPING_SOURCE_INVALID","草稿中存在重复对象修订，来源定位不明确，请先保存独立修订。");
    }
    static async Task<MappingRun> Owned(Database db,Actor actor,Guid id)=>await db.Set<MappingRun>().SingleOrDefaultAsync(r=>r.Id==id && r.FamilyId==actor.FamilyId)??throw new ApiError(404,"NOT_FOUND","映射建议运行不存在。");
    static async Task<Catalog> Library(Database db,MappingRun run,bool accepting=false)
    {
        var release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==run.LibraryReleaseId && r.FamilyId==run.FamilyId)??throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","冻结能力库已不可用。");
        if(Content.Hash(release.Payload)!=run.LibraryHash || Content.Hash(run.SourcePayload)!=run.SourceHash)throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","原输入摘要不一致，不能继续使用本次建议。");
        if(accepting && release.Withdrawn)throw new ApiError(422,"LIBRARY_WITHDRAWN","本次能力库已经撤回；可拒绝旧建议，请选择有效版本重新准备。");
        return Json.Read<Catalog>(release.Payload);
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/builder/mapping-runs",async(Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");
            return await db.Set<MappingRun>().Where(r=>r.FamilyId==a.FamilyId).OrderByDescending(r=>r.CreatedAt).ThenBy(r=>r.Id).Take(50).Select(r=>new MappingRunSummary(r.Id,r.SourceDraftId,r.SourceDraftVersion,r.SourceTitle,r.LibraryReleaseId,r.Provider,r.Model,r.Status,r.CreatedAt)).ToArrayAsync();
        });
        api.MapGet("/builder/mapping-runs/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var run=await Owned(db,a,id);var library=await Library(db,run);
            var suggestions=await db.Set<MappingSuggestion>().Where(s=>s.RunId==id && s.FamilyId==a.FamilyId).OrderBy(s=>s.OwnerType).ThenBy(s=>s.OwnerId).ToArrayAsync();var ids=suggestions.Select(s=>s.Id).ToArray();
            var decisions=await db.Set<MappingReviewDecision>().Where(d=>d.FamilyId==a.FamilyId && ids.Contains(d.SuggestionId)).OrderBy(d=>d.CreatedAt).ThenBy(d=>d.Id).ToArrayAsync();var dids=decisions.Select(d=>d.Id).ToArray();
            var sets=await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==a.FamilyId && s.ReviewDecisionId!=null && dids.Contains(s.ReviewDecisionId.Value)).OrderBy(s=>s.OwnerType).ThenBy(s=>s.OwnerId).ToArrayAsync();var sids=sets.Select(s=>s.Id).ToArray();
            var items=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==a.FamilyId && sids.Contains(i.SetRevisionId)).OrderBy(i=>i.SetRevisionId).ThenBy(i=>i.Sequence).ToArrayAsync();
            var accepted=decisions.Where(d=>d.Decision=="Accept").ToArray();var corrected=accepted.Count(d=>
            {
                var original=suggestions.Single(s=>s.Id==d.SuggestionId);
                return !MappingSuggestions.SameMapping(new(original.EvidencePolicy,Json.Read<SuggestedMappingItem[]>(original.SuggestedItems)),Json.Read<MappingProposal>(d.CorrectedPayload));
            });
            var quality=new MappingQuality(suggestions.Length,suggestions.Count(s=>s.Status=="Pending"),accepted.Length,decisions.Count(d=>d.Decision=="Reject"),corrected,accepted.Length-corrected,"NotEvaluated","仅统计人工处理与校正次数；处理次数、模拟排序和接受率不代表映射正确率，没有正式金标准质量评测。");
            return new MappingRunDetail(run,suggestions,decisions,sets.Select(MappingReviewedSetDto.From).ToArray(),items,library.Kcs,quality);
        });
        api.MapPost("/builder/mapping-runs",async Task<Results<Ok<MappingRun>,Created<MappingRun>>>(MappingRunInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");
            if(input.Provider is not "Mock" and not "Manual")throw new ApiError(422,"PROVIDER_UNCONFIGURED","模型稍后接入，当前支持本地模拟建议或手工维护，不外发内容。");
            if(input.Owners==null || input.Owners.Length is <1 or >100 || input.Owners.Any(o=>o==null) || input.Owners.Select(o=>new{o.OwnerType,o.OwnerId}).Distinct().Count()!=input.Owners.Length)throw new ApiError(422,"INVALID_SELECTION","请选择1到100个不同对象和明确修订。");
            var draft=await db.Drafts.SingleOrDefaultAsync(d=>d.Id==input.DraftId && d.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","内容草稿不存在。");
            var release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==input.LibraryReleaseId && r.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","能力库版本不存在。");
            if(release.Withdrawn)throw new ApiError(422,"LIBRARY_WITHDRAWN","请选择未撤回的正式能力库版本。");
            var source=Json.Read<Catalog>(draft.Payload);Shape(source);var library=Json.Read<Catalog>(release.Payload).Kcs;
            if(library.Length is <1 or >1000)throw new ApiError(422,"LIBRARY_SIZE_INVALID","本地建议支持1到1000个正式能力，请选择合适的能力库版本。");
            var selections=input.Owners.OrderBy(o=>o.OwnerType,StringComparer.Ordinal).ThenBy(o=>o.OwnerId).ToArray();var owners=selections.Select(o=>MappingSuggestions.Owner(source,o)).ToArray();
            var sourceHash=Content.Hash(draft.Payload);var libraryHash=Content.Hash(release.Payload);
            var model=input.Provider=="Manual"?"None":Retrieval.Space;var promptVersion=input.Provider=="Manual"?"manual-source/1":"mapping-suggestion/1";
            var hash=Content.Hash(Json.Write(new{draft.Id,draft.Version,sourceHash,releaseId=release.Id,libraryHash,selections,input.Provider,model,promptVersion}));
            var existing=await db.Set<MappingRun>().SingleOrDefaultAsync(r=>r.FamilyId==a.FamilyId && r.InputHash==hash);if(existing!=null)return TypedResults.Ok(existing);
            var run=new MappingRun{FamilyId=a.FamilyId,SourceDraftId=draft.Id,SourceDraftVersion=draft.Version,SourceTitle=draft.Title,SourcePayload=draft.Payload,SourceHash=sourceHash,LibraryReleaseId=release.Id,LibraryHash=libraryHash,InputHash=hash,Provider=input.Provider,Model=model,PromptVersion=promptVersion};db.Add(run);
            foreach(var owner in owners)
            {
                var reference=MappingSuggestions.SourceRef(draft.Id,owner);
                var result=input.Provider=="Manual"?MappingSuggestions.Manual(source,owner,library,reference):MappingSuggestions.Suggest(owner,library,reference);
                var validation=MappingSuggestions.Validate(owner,result.Proposal,library,MappingSuggestions.SourceRef(draft.Id,owner));
                db.Add(new MappingSuggestion{FamilyId=a.FamilyId,RunId=run.Id,OwnerType=owner.OwnerType,OwnerId=owner.Id,OwnerRevisionId=owner.RevisionId,OwnerTitle=owner.Title,EvidencePolicy=result.Proposal.EvidencePolicy,SuggestedItems=Json.Write(result.Proposal.Items),Matches=Json.Write(result.Matches),ValidationFlags=Json.Write(result.Flags.Concat(validation).ToArray())});
            }
            db.Audits.Add(new(){FamilyId=a.FamilyId,ActorId=a.Id,Action="MappingSuggestionsPrepared",Details=Json.Write(new{runId=run.Id,run.SourceDraftId,run.SourceDraftVersion,run.LibraryReleaseId,run.InputHash,owners=selections.Length,provider=run.Provider})});
            return TypedResults.Created($"/api/v1/builder/mapping-runs/{run.Id}",run);
        });
        api.MapPost("/builder/mapping-runs/{id:guid}/suggestions:decide",async(Guid id,MappingBatchInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var run=await Owned(db,a,id);
            if(input.Decisions==null || input.Decisions.Length is <1 or >100 || input.Decisions.Any(d=>d==null) || input.Decisions.Select(d=>d.SuggestionId).Distinct().Count()!=input.Decisions.Length)throw new ApiError(422,"INVALID_SELECTION","请明确审核1到100条不同建议。");
            if(input.Decisions.Any(d=>d.Decision is not "Accept" and not "Reject" || string.IsNullOrWhiteSpace(d.Reason) || d.Reason.Length>4000 || d.Decision=="Accept" && d.CorrectedProposal==null || d.Decision=="Reject" && d.CorrectedProposal!=null))throw new ApiError(422,"INVALID_DECISION","每项需明确接受或拒绝并填写依据；接受须提交完整校正映射，拒绝不附带映射。");
            var ids=input.Decisions.Select(d=>d.SuggestionId).ToArray();var suggestions=await db.Set<MappingSuggestion>().Where(s=>s.RunId==id && s.FamilyId==a.FamilyId && ids.Contains(s.Id)).ToArrayAsync();
            if(suggestions.Length!=ids.Length)throw new ApiError(404,"NOT_FOUND","审核选择含不属于本次运行的建议。");
            if(suggestions.Any(s=>s.Status!="Pending"))throw new ApiError(409,"ALREADY_REVIEWED","选择中有已经处理的建议，请刷新后重新核对。整批没有再次执行。");
            var accepting=input.Decisions.Any(d=>d.Decision=="Accept");var library=await Library(db,run,accepting);var source=Json.Read<Catalog>(run.SourcePayload);Shape(source);var proposed=source;
            if(accepting)
            {
                var priorSuggestions=await db.Set<MappingSuggestion>().Where(s=>s.RunId==id && s.FamilyId==a.FamilyId && s.Status=="Accepted").OrderBy(s=>s.OwnerType).ThenBy(s=>s.OwnerId).ToArrayAsync();
                foreach(var prior in priorSuggestions)
                {
                    var priorDecision=await db.Set<MappingReviewDecision>().SingleOrDefaultAsync(d=>d.FamilyId==a.FamilyId && d.SuggestionId==prior.Id && d.Decision=="Accept");
                    var priorSet=priorDecision==null?null:await db.Set<MappingSetRevision>().SingleOrDefaultAsync(s=>s.FamilyId==a.FamilyId && s.ReviewDecisionId==priorDecision.Id);
                    if(priorDecision==null || priorSet==null || Content.Hash(priorDecision.CorrectedPayload)!=priorDecision.CorrectedPayloadHash)throw new ApiError(422,"REVIEW_SNAPSHOT_UNKNOWN","此前人工审核记录不完整，请核对原运行。");
                    var priorOwner=MappingSuggestions.Owner(source,new(prior.OwnerType,prior.OwnerId,prior.OwnerRevisionId));var priorProposal=Json.Read<MappingProposal>(priorDecision.CorrectedPayload);
                    if(MappingSuggestions.Validate(priorOwner,priorProposal,library.Kcs,MappingSuggestions.SourceRef(run.SourceDraftId,priorOwner)).Length>0)throw new ApiError(422,"REVIEW_SNAPSHOT_UNKNOWN","此前人工映射与冻结输入不一致。");
                    proposed=MappingSuggestions.Apply(proposed,priorOwner,priorProposal,priorSet.OwnerRevisionId,library.Kcs);
                    if(Content.Hash(MappingSuggestions.Definition(proposed,priorOwner.OwnerType,priorOwner.Id))!=priorSet.OwnerDefinitionHash)throw new ApiError(422,"REVIEW_SNAPSHOT_UNKNOWN","此前人工映射对象摘要不一致。");
                }
            }
            var draft=accepting?new ContentDraft{FamilyId=a.FamilyId,Title=run.SourceTitle+" · 映射审核草稿"}:null;
            var decisions=new List<MappingReviewDecision>();
            foreach(var entry in input.Decisions)
            {
                var suggestion=suggestions.Single(s=>s.Id==entry.SuggestionId);var owner=MappingSuggestions.Owner(source,new(suggestion.OwnerType,suggestion.OwnerId,suggestion.OwnerRevisionId));
                var original=new MappingProposal(suggestion.EvidencePolicy,Json.Read<SuggestedMappingItem[]>(suggestion.SuggestedItems));
                var corrected=entry.CorrectedProposal;
                if(corrected!=null && (corrected.Items==null || corrected.Items.Any(i=>i==null)))throw new ApiError(422,"INVALID_MAPPING","接受映射需要有效条目数组。");
                if(corrected!=null)corrected=corrected with{Items=corrected.Items.OrderBy(i=>i.Sequence).ToArray()};
                var decision=new MappingReviewDecision{FamilyId=a.FamilyId,SuggestionId=suggestion.Id,Decision=entry.Decision,ReviewerId=a.Id,Reason=entry.Reason.Trim(),OriginalPayloadHash=Content.Hash(Json.Write(original)),CorrectedPayload=corrected==null?"":Json.Write(corrected),CorrectedPayloadHash=corrected==null?"":Content.Hash(Json.Write(corrected)),CreatedDraftId=entry.Decision=="Accept"?draft!.Id:null};
                if(corrected!=null)
                {
                    var errors=MappingSuggestions.Validate(owner,corrected,library.Kcs,MappingSuggestions.SourceRef(run.SourceDraftId,owner));if(errors.Length>0)throw new ApiError(422,"INVALID_MAPPING",string.Join(" ",errors));
                    var selectedKCs=corrected.Items.Select(i=>i.KCId).Distinct().ToArray();
                    if(await db.Set<ContentIdentity>().AnyAsync(k=>k.FamilyId==a.FamilyId && k.EntityType=="KC" && selectedKCs.Contains(k.Id) && k.Status=="Deprecated"))throw new ApiError(422,"KC_DEPRECATED","选中能力已经停用，请选择有效能力库重新准备建议。");
                    var revision=Guid.NewGuid();proposed=MappingSuggestions.Apply(proposed,owner,corrected,revision,library.Kcs);
                    var number=(await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==a.FamilyId && s.OwnerType==owner.OwnerType && s.OwnerId==owner.Id).MaxAsync(s=>(int?)s.RevisionNo)??0)+1;
                    var set=new MappingSetRevision{FamilyId=a.FamilyId,DraftId=draft!.Id,ReviewDecisionId=decision.Id,OwnerType=owner.OwnerType,OwnerId=owner.Id,OwnerRevisionId=revision,OriginalOwnerRevisionId=owner.RevisionId,OwnerDefinitionHash=Content.Hash(MappingSuggestions.Definition(proposed,owner.OwnerType,owner.Id)),RevisionNo=number,EvidencePolicy=corrected.EvidencePolicy};db.Add(set);
                    foreach(var item in corrected.Items)db.Add(new MappingSetItem{FamilyId=a.FamilyId,SetRevisionId=set.Id,KCId=item.KCId,KCRevisionId=item.KCRevisionId,Role=item.Role,CoverageWeight=item.CoverageWeight,EvidenceShare=item.EvidenceShare,EvidenceMode=item.EvidenceMode,Step=item.Step,Sequence=item.Sequence,ModelScore=item.ModelScore,SourceRefs=Json.Write(item.SourceRefs)});
                }
                suggestion.Status=entry.Decision=="Accept"?"Accepted":"Rejected";suggestion.Version++;db.Add(decision);decisions.Add(decision);
            }
            string[] warnings=[];if(draft!=null){draft.Payload=Json.Write(proposed);warnings=Content.Validate(proposed);db.Drafts.Add(draft);}
            db.Audits.Add(new(){FamilyId=a.FamilyId,ActorId=a.Id,Action="MappingSuggestionsReviewed",Details=Json.Write(new{runId=id,draftId=draft?.Id,decisions=decisions.Select(d=>new{d.Id,d.SuggestionId,d.Decision,d.Reason,d.OriginalPayloadHash,d.CorrectedPayloadHash})})});
            var totalPending=await db.Set<MappingSuggestion>().CountAsync(s=>s.RunId==id && s.FamilyId==a.FamilyId && s.Status=="Pending");
            return new MappingBatchResult(id,draft?.Id,decisions.Select(d=>d.Id).ToArray(),totalPending-suggestions.Length,warnings);
        });
    }
}
