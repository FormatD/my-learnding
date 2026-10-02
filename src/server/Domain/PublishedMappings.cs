using Microsoft.EntityFrameworkCore;

namespace Learning;
// Stable wire shape for existing suggestion clients; published sets have a separate API shape.
public class MappingReviewedSetDto
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid DraftId { get; set; }
    public Guid ReviewDecisionId { get; set; }
    public string OwnerType { get; set; } = "";
    public Guid OwnerId { get; set; }
    public Guid OwnerRevisionId { get; set; }
    public Guid OriginalOwnerRevisionId { get; set; }
    public string OwnerDefinitionHash { get; set; } = "";
    public int RevisionNo { get; set; }
    public string EvidencePolicy { get; set; } = "";
    public string ReviewStatus { get; set; } = "";
    public static MappingReviewedSetDto From(MappingSetRevision s)=>new(){Id=s.Id,FamilyId=s.FamilyId,CreatedAt=s.CreatedAt,DraftId=s.DraftId,ReviewDecisionId=s.ReviewDecisionId??throw new InvalidOperationException("Suggestion review required"),OwnerType=s.OwnerType,OwnerId=s.OwnerId,OwnerRevisionId=s.OwnerRevisionId,OriginalOwnerRevisionId=s.OriginalOwnerRevisionId,OwnerDefinitionHash=s.OwnerDefinitionHash,RevisionNo=s.RevisionNo,EvidencePolicy=s.EvidencePolicy,ReviewStatus=s.ReviewStatus};
}
public class ReleaseMappingSet : Row
{
    public Guid ReleaseId { get; set; }
    public Guid SetRevisionId { get; set; }
    public string OwnerType { get; set; } = "";
    public Guid OwnerId { get; set; }
    public Guid OwnerRevisionId { get; set; }
}
public record PublishedMappingDetail(Guid ReleaseId,string Status,ReleaseMappingSet[] Bindings,MappingSetRevision[] Sets,MappingSetItem[] Items,string[] UnversionedOwners);
public static class PublishedMappings
{
    public static MappingOwnerSelection[] Owners(Catalog c)=>c.Questions.Select(q=>new MappingOwnerSelection("Question",q.Id,q.RevisionId))
        .Concat(c.Lessons.Where(l=>l.RevisionId!=null).Select(l=>new MappingOwnerSelection("Lesson",l.Id,l.RevisionId!.Value)))
        .Concat(c.Resources.Where(r=>r.RevisionId!=null).Select(r=>new MappingOwnerSelection("Resource",r.Id,r.RevisionId!.Value))).ToArray();
    public static Mapping[] Projection(Catalog c,MappingOwner owner)=>owner.OwnerType=="Question"?owner.Mappings:
        (owner.OwnerType=="Lesson"?c.Lessons.Single(l=>l.Id==owner.Id).KCIds:c.Resources.Single(r=>r.Id==owner.Id).KCIds).Distinct().Select(id=>new Mapping(id,"Primary",0,"None",null)).ToArray();
    public static bool Matches(Catalog c,MappingOwner owner,MappingSetRevision set,MappingSetItem[] items,bool includeCoverage=true)
    {
        var projection=Projection(c,owner);var ordered=items.OrderBy(i=>i.Sequence).ToArray();
        return set.EvidencePolicy==owner.EvidencePolicy && projection.Length==ordered.Length && projection.Zip(ordered).All(pair=>
            pair.First.KCId==pair.Second.KCId && c.Kcs.Any(k=>k.Id==pair.Second.KCId && k.RevisionId==pair.Second.KCRevisionId) && pair.First.Role==pair.Second.Role && pair.First.Share==pair.Second.EvidenceShare && pair.First.Mode==pair.Second.EvidenceMode && pair.First.Step==pair.Second.Step && (!includeCoverage || !(c.MappingCoverage??[]).Any(r=>r.OwnerType==owner.OwnerType && r.OwnerId==owner.Id) || CoverageEditing.Weight(c,owner,pair.First)==pair.Second.CoverageWeight)) && ordered.Select(i=>i.Sequence).SequenceEqual(Enumerable.Range(1,ordered.Length));
    }
    public static async Task Bind(Database db,Release release,Catalog c,ContentReviewRecord review)
    {
        if(review.FamilyId!=release.FamilyId || review.SourcePayload!=release.Payload || review.PayloadHash!=release.Hash)throw new ApiError(422,"REVIEW_SNAPSHOT_REQUIRED","发布映射必须对应完整内容审核快照。");
        await CoverageEditing.ValidateSources(db,release.FamilyId,c);
        foreach(var selection in Owners(c))
        {
            var owner=MappingSuggestions.Owner(c,selection);var definitionHash=Content.Hash(MappingSuggestions.Definition(c,owner.OwnerType,owner.Id));
            var candidates=await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==release.FamilyId && s.OwnerType==owner.OwnerType && s.OwnerId==owner.Id && s.OwnerRevisionId==owner.RevisionId).OrderByDescending(s=>s.ReviewDecisionId!=null).ThenByDescending(s=>s.RevisionNo).ToArrayAsync();
            MappingSetRevision? chosen=null;
            foreach(var set in candidates)
            {
                var items=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==release.FamilyId && i.SetRevisionId==set.Id).ToArrayAsync();
                if(set.OwnerDefinitionHash!=definitionHash)throw new ApiError(422,"MAPPING_REVISION_IMMUTABLE","同一对象修订不能更改内容与映射。");
                if(Matches(c,owner,set,items) && (!(c.MappingCoverage??[]).Any(r=>r.OwnerType==owner.OwnerType && r.OwnerId==owner.Id) || Projection(c,owner).Select((m,i)=>CoverageEditing.Weight(c,owner,m)==items.OrderBy(x=>x.Sequence).ElementAt(i).CoverageWeight).All(v=>v))){chosen=set;break;}
                if(set.ReviewDecisionId!=null && !Matches(c,owner,set,items,includeCoverage:false))throw new ApiError(422,"MAPPING_LIBRARY_REVISION_CHANGED","逐项审核映射必须保留原能力版本和完整归因。");
            }
            if(chosen==null)
            {
                var projection=Projection(c,owner);
                if(projection.Any(m=>decimal.Round(m.Share,6)!=m.Share))throw new ApiError(422,"MAPPING_PRECISION_INVALID","证据份额最多六位小数，请校正后重新审核。");
                var number=(await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==release.FamilyId && s.OwnerType==owner.OwnerType && s.OwnerId==owner.Id).MaxAsync(s=>(int?)s.RevisionNo)??0)+1;
                chosen=new(){FamilyId=release.FamilyId,DraftId=review.DraftId,ContentReviewRecordId=review.Id,OwnerType=owner.OwnerType,OwnerId=owner.Id,OwnerRevisionId=owner.RevisionId,OriginalOwnerRevisionId=owner.RevisionId,OwnerDefinitionHash=definitionHash,RevisionNo=number,EvidencePolicy=owner.EvidencePolicy,ReviewStatus="ReviewedCatalog",CoverageOrigin=(c.MappingCoverage??[]).Any(r=>r.OwnerType==owner.OwnerType && r.OwnerId==owner.Id && r.Origin!="Default")?"CatalogReviewed":"CatalogDefault"};db.Add(chosen);
                foreach(var pair in projection.Select((m,i)=>(m,i)))db.Add(new MappingSetItem{FamilyId=release.FamilyId,SetRevisionId=chosen.Id,KCId=pair.m.KCId,KCRevisionId=c.Kcs.Single(k=>k.Id==pair.m.KCId).RevisionId,Role=pair.m.Role,CoverageWeight=CoverageEditing.Weight(c,owner,pair.m),EvidenceShare=pair.m.Share,EvidenceMode=pair.m.Mode,Step=pair.m.Step,Sequence=pair.i+1,SourceRefs=Json.Write(new[]{MappingSuggestions.SourceRef(review.DraftId,owner)}.Concat(CoverageEditing.Entry(c,owner,pair.m)?.SourceSetRevisionId is {} inherited?new[]{"mapping-set:"+inherited}:Array.Empty<string>()).ToArray())});
            }
            db.Add(new ReleaseMappingSet{FamilyId=release.FamilyId,ReleaseId=release.Id,SetRevisionId=chosen.Id,OwnerType=owner.OwnerType,OwnerId=owner.Id,OwnerRevisionId=owner.RevisionId});
        }
    }
    public static Question Project(Catalog catalog,Question question,MappingSetRevision set,MappingSetItem[] items)
    {
        var owner=MappingSuggestions.Owner(catalog,new("Question",question.Id,question.RevisionId));
        if(set.OwnerType!="Question" || set.OwnerId!=question.Id || set.OwnerRevisionId!=question.RevisionId || set.OwnerDefinitionHash!=Content.Hash(Json.Write(question)) || !Matches(catalog,owner,set,items))
            throw new ApiError(422,"MAPPING_SNAPSHOT_UNKNOWN","固定映射与原题快照不一致，请核对发布记录。");
        return question with{Policy=set.EvidencePolicy,Mappings=items.OrderBy(i=>i.Sequence).Select(i=>new Mapping(i.KCId,i.Role,i.EvidenceShare,i.EvidenceMode,i.Step)).ToArray()};
    }
    public static async Task<Guid?> Resolve(Database db,Release release,Question question)
    {
        if(!await db.Set<ContentReviewRecord>().AnyAsync(r=>r.FamilyId==release.FamilyId && r.PublishedReleaseId==release.Id && r.PublishedMappingVersion=="mapping-container/1"))return null;
        var binding=await db.Set<ReleaseMappingSet>().SingleOrDefaultAsync(b=>b.FamilyId==release.FamilyId && b.ReleaseId==release.Id && b.OwnerType=="Question" && b.OwnerId==question.Id);
        var set=binding==null?null:await db.Set<MappingSetRevision>().SingleOrDefaultAsync(s=>s.FamilyId==release.FamilyId && s.Id==binding.SetRevisionId);
        if(binding==null || set==null || binding.OwnerRevisionId!=question.RevisionId)throw new ApiError(422,"MAPPING_SNAPSHOT_UNKNOWN","这个发布版本缺少准确题目映射，不能创建新会话。");
        var items=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==release.FamilyId && i.SetRevisionId==set.Id).ToArrayAsync();
        Project(Json.Read<Catalog>(release.Payload),question,set,items);
        return set.Id;
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/content/releases/{id:guid}/mapping-sets",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var release=await db.Releases.SingleOrDefaultAsync(r=>r.FamilyId==a.FamilyId && r.Id==id)??throw new ApiError(404,"NOT_FOUND","发布版本不存在。");
            var c=Json.Read<Catalog>(release.Payload);var bindings=await db.Set<ReleaseMappingSet>().Where(b=>b.FamilyId==a.FamilyId && b.ReleaseId==id).OrderBy(b=>b.OwnerType).ThenBy(b=>b.OwnerId).ToArrayAsync();var ids=bindings.Select(b=>b.SetRevisionId).ToArray();
            var sets=await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==a.FamilyId && ids.Contains(s.Id)).ToArrayAsync();var items=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==a.FamilyId && ids.Contains(i.SetRevisionId)).OrderBy(i=>i.SetRevisionId).ThenBy(i=>i.Sequence).ToArrayAsync();
            var unknown=c.Resources.Where(r=>r.RevisionId==null).Select(r=>"Resource:"+r.Id).Concat(c.Lessons.Where(l=>l.RevisionId==null).Select(l=>"Lesson:"+l.Id)).ToArray();
            var recorded=await db.Set<ContentReviewRecord>().AnyAsync(r=>r.FamilyId==a.FamilyId && r.PublishedReleaseId==id && r.PublishedMappingVersion=="mapping-container/1");
            var expected=Owners(c);
            var complete=recorded && bindings.Length==expected.Length && expected.All(o=>bindings.Any(b=>b.OwnerType==o.OwnerType && b.OwnerId==o.OwnerId && b.OwnerRevisionId==o.OwnerRevisionId && sets.Any(s=>s.Id==b.SetRevisionId && s.OwnerType==o.OwnerType && s.OwnerId==o.OwnerId && s.OwnerRevisionId==o.OwnerRevisionId && s.OwnerDefinitionHash==Content.Hash(MappingSuggestions.Definition(c,o.OwnerType,o.OwnerId)) && Matches(c,MappingSuggestions.Owner(c,o),s,items.Where(i=>i.SetRevisionId==s.Id).ToArray()))));
            var status=!recorded?"LegacySnapshot":!complete?"BindingIncomplete":unknown.Length>0?"UnversionedOwners":"Bound";
            return new PublishedMappingDetail(id,status,bindings,sets,items,unknown);
        });
    }
}
