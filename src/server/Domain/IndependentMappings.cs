using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
namespace Learning;
public class IndependentMappingDraft : Row
{
    public Guid SetRevisionId { get; set; }
    public Guid SourceDraftId { get; set; }
    public long SourceDraftVersion { get; set; }
    public string SourcePayload { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public Guid LibraryReleaseId { get; set; }
    public string LibraryHash { get; set; } = "";
    public Guid SubmittedBy { get; set; }
    public string Provider { get; set; } = "Manual";
    public string Reason { get; set; } = "";
}
public record IndependentMappingInput(Guid SourceDraftId,long ExpectedDraftVersion,Guid LibraryReleaseId,string OwnerType,Guid OwnerId,Guid OwnerRevisionId,string EvidencePolicy,SuggestedMappingItem[] Items,string Reason,string Provider="Manual");
public record IndependentMappingDetail(IndependentMappingDraft Source,MappingSetRevision Set,MappingSetItem[] Items,ContentDraft Draft,KC[] Library,string[] DraftValidationWarnings);
public record IndependentMappingSummary(Guid SetRevisionId,Guid DraftId,string OwnerType,Guid OwnerId,int RevisionNo,string ReviewStatus,long SourceDraftVersion,string Reason,DateTimeOffset CreatedAt);
public record IndependentMappingPage(IndependentMappingSummary[] Sets,int Total,int Offset,int Limit);
public static class IndependentMappings
{
    static async Task<Release> Library(Database db,IndependentMappingDraft source,bool requireActive)
    {
        var release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==source.LibraryReleaseId && r.FamilyId==source.FamilyId)??throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","固定能力库已不可用。");
        if(Content.Hash(release.Payload)!=source.LibraryHash || Content.Hash(source.SourcePayload)!=source.SourceHash)throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","原输入摘要不一致，请核对映射草稿。");
        if(requireActive && release.Withdrawn)throw new ApiError(422,"LIBRARY_WITHDRAWN","固定能力库已撤回，请选择有效版本重新建立映射草稿。");
        return release;
    }
    public static async Task Review(Database db,ContentReviewRecord review,Catalog catalog)
    {
        var sets=await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==review.FamilyId && s.DraftId==review.DraftId && s.ReviewStatus=="Draft").ToArrayAsync();
        foreach(var set in sets)
        {
            var selection=PublishedMappings.Owners(catalog).SingleOrDefault(o=>o.OwnerType==set.OwnerType && o.OwnerId==set.OwnerId && o.OwnerRevisionId==set.OwnerRevisionId);
            if(selection==null)continue; // A separately edited content revision does not approve the original draft set.
            var owner=MappingSuggestions.Owner(catalog,selection);var items=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==review.FamilyId && i.SetRevisionId==set.Id).OrderBy(i=>i.Sequence).ToArrayAsync();
            if(set.OwnerDefinitionHash!=Content.Hash(MappingSuggestions.Definition(catalog,set.OwnerType,set.OwnerId)) || !PublishedMappings.Matches(catalog,owner,set,items))continue;
            var source=await db.Set<IndependentMappingDraft>().SingleOrDefaultAsync(d=>d.FamilyId==review.FamilyId && d.SetRevisionId==set.Id)??throw new ApiError(422,"INPUT_SNAPSHOT_UNKNOWN","独立映射草稿来源缺失。");
            await Library(db,source,true);
            set.ContentReviewRecordId=review.Id;set.ReviewStatus="ReviewedCatalog";set.CoverageOrigin="CatalogReviewed";
        }
    }
    public static async Task ValidateForPublish(Database db,MappingSetRevision set)
    {
        var source=await db.Set<IndependentMappingDraft>().SingleOrDefaultAsync(d=>d.FamilyId==set.FamilyId && d.SetRevisionId==set.Id);
        if(source!=null)await Library(db,source,true);
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/content/mapping-sets",async Task<Created<IndependentMappingDetail>>(IndependentMappingInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");
            if(input.Provider!="Manual")throw new ApiError(422,"PROVIDER_UNCONFIGURED","模型稍后接入；当前入口保存手工映射草稿，不调用外部模型。");
            if(input.ExpectedDraftVersion<=0 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>4000)throw new ApiError(422,"INVALID_MAPPING_INPUT","请明确原草稿版本及最多4000字的维护依据。");
            var source=await db.Drafts.SingleOrDefaultAsync(d=>d.Id==input.SourceDraftId && d.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","来源草稿不存在。");
            if(source.Version!=input.ExpectedDraftVersion)throw new ApiError(412,"DRAFT_CHANGED","来源草稿已变化，请重新读取后核对映射；没有保存新草稿。");
            var release=await db.Releases.SingleOrDefaultAsync(r=>r.Id==input.LibraryReleaseId && r.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","能力库版本不存在。");
            if(release.Withdrawn)throw new ApiError(422,"LIBRARY_WITHDRAWN","请选择未撤回的正式能力库版本。");
            var catalog=Json.Read<Catalog>(source.Payload);MappingBuilder.Shape(catalog);var library=Json.Read<Catalog>(release.Payload).Kcs;
            var owner=MappingSuggestions.Owner(catalog,new(input.OwnerType,input.OwnerId,input.OwnerRevisionId));var proposal=new MappingProposal(input.EvidencePolicy,input.Items);
            var errors=MappingSuggestions.Validate(owner,proposal,library,MappingSuggestions.SourceRef(source.Id,owner));
            if(errors.Length>0)throw new ApiError(422,"INVALID_MAPPING",string.Join(" ",errors));
            if(input.Items.Any(i=>i.ModelScore!=null))throw new ApiError(422,"INVALID_MAPPING","手工映射不填模型分数。");
            var ids=input.Items.Select(i=>i.KCId).Distinct().ToArray();
            if(await db.Set<ContentIdentity>().AnyAsync(k=>k.FamilyId==a.FamilyId && k.EntityType=="KC" && ids.Contains(k.Id) && k.Status=="Deprecated"))throw new ApiError(422,"KC_DEPRECATED","所选能力已停用，请核对有效版本。");
            proposal=proposal with{Items=proposal.Items.OrderBy(i=>i.Sequence).ToArray()};
            var unchanged=owner.EvidencePolicy==proposal.EvidencePolicy && Json.Write(PublishedMappings.Projection(catalog,owner))==Json.Write(proposal.Items.Select(i=>new Mapping(i.KCId,i.Role,i.EvidenceShare,i.EvidenceMode,i.Step)).ToArray());
            var proposed=MappingSuggestions.Apply(catalog,owner,proposal,unchanged?owner.RevisionId:Guid.NewGuid(),library);
            var nextOwner=MappingSuggestions.Owner(proposed,new(owner.OwnerType,owner.Id,unchanged?owner.RevisionId:PublishedMappings.Owners(proposed).Single(o=>o.OwnerType==owner.OwnerType && o.OwnerId==owner.Id).OwnerRevisionId));
            var draft=new ContentDraft{FamilyId=a.FamilyId,Title=source.Title+" · 独立映射草稿",Payload=Json.Write(proposed)};
            var number=(await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==a.FamilyId && s.OwnerType==owner.OwnerType && s.OwnerId==owner.Id).MaxAsync(s=>(int?)s.RevisionNo)??0)+1;
            var set=new MappingSetRevision{FamilyId=a.FamilyId,DraftId=draft.Id,OwnerType=owner.OwnerType,OwnerId=owner.Id,OwnerRevisionId=nextOwner.RevisionId,OriginalOwnerRevisionId=owner.RevisionId,OwnerDefinitionHash=Content.Hash(MappingSuggestions.Definition(proposed,owner.OwnerType,owner.Id)),RevisionNo=number,EvidencePolicy=proposal.EvidencePolicy,ReviewStatus="Draft",CoverageOrigin="UnreviewedDraft"};
            var snapshot=new IndependentMappingDraft{FamilyId=a.FamilyId,SetRevisionId=set.Id,SourceDraftId=source.Id,SourceDraftVersion=source.Version,SourcePayload=source.Payload,SourceHash=Content.Hash(source.Payload),LibraryReleaseId=release.Id,LibraryHash=Content.Hash(release.Payload),SubmittedBy=a.Id,Reason=input.Reason.Trim()};
            var items=proposal.Items.Select(i=>new MappingSetItem{FamilyId=a.FamilyId,SetRevisionId=set.Id,KCId=i.KCId,KCRevisionId=i.KCRevisionId,Role=i.Role,CoverageWeight=i.CoverageWeight,EvidenceShare=i.EvidenceShare,EvidenceMode=i.EvidenceMode,Step=i.Step,Sequence=i.Sequence,SourceRefs=Json.Write(i.SourceRefs)}).ToArray();
            db.Add(draft);db.Add(set);db.Add(snapshot);db.AddRange(items);
            db.Audits.Add(new(){FamilyId=a.FamilyId,ActorId=a.Id,Action="IndependentMappingDraftCreated",Details=Json.Write(new{snapshot.Id,setId=set.Id,draftId=draft.Id,snapshot.SourceDraftId,snapshot.SourceDraftVersion,snapshot.LibraryReleaseId,snapshot.Reason})});
            return TypedResults.Created($"/api/v1/content/mapping-sets/{set.Id}",new IndependentMappingDetail(snapshot,set,items,draft,library,Content.Validate(proposed)));
        });
        api.MapGet("/content/mapping-sets",async(int? offset,int? limit,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var skip=offset??0;var take=limit??20;if(skip<0 || take is <1 or >100)throw new ApiError(422,"INVALID_PAGE","页码须非负，每页1至100项。");
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var query=from d in db.Set<IndependentMappingDraft>() join s in db.Set<MappingSetRevision>() on d.SetRevisionId equals s.Id where d.FamilyId==a.FamilyId && s.FamilyId==a.FamilyId select new {SetRevisionId=s.Id,s.DraftId,s.OwnerType,s.OwnerId,s.RevisionNo,s.ReviewStatus,d.SourceDraftVersion,d.Reason,d.CreatedAt};
            var total=await query.CountAsync();var rows=await query.OrderByDescending(s=>s.CreatedAt).ThenBy(s=>s.SetRevisionId).Skip(skip).Take(take).ToArrayAsync();var sets=rows.Select(s=>new IndependentMappingSummary(s.SetRevisionId,s.DraftId,s.OwnerType,s.OwnerId,s.RevisionNo,s.ReviewStatus,s.SourceDraftVersion,s.Reason,s.CreatedAt)).ToArray();await tx.CommitAsync();return new IndependentMappingPage(sets,total,skip,take);
        });
        api.MapGet("/content/mapping-sets/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var source=await db.Set<IndependentMappingDraft>().SingleOrDefaultAsync(d=>d.FamilyId==a.FamilyId && d.SetRevisionId==id)??throw new ApiError(404,"NOT_FOUND","独立映射草稿不存在。");
            var set=await db.Set<MappingSetRevision>().SingleAsync(s=>s.FamilyId==a.FamilyId && s.Id==id);var items=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==a.FamilyId && i.SetRevisionId==id).OrderBy(i=>i.Sequence).ToArrayAsync();
            var draft=await db.Drafts.SingleAsync(d=>d.Id==set.DraftId && d.FamilyId==a.FamilyId);var library=Json.Read<Catalog>((await Library(db,source,false)).Payload).Kcs;
            var result=new IndependentMappingDetail(source,set,items,draft,library,Content.Validate(Json.Read<Catalog>(draft.Payload)));await tx.CommitAsync();return result;
        });
    }
}
