using Microsoft.EntityFrameworkCore;
namespace Learning;
public record MappingCoverage(string OwnerType,Guid OwnerId,Guid KCId,string Role,string EvidenceMode,string? Step,decimal EvidenceShare,decimal CoverageWeight,Guid? SourceSetRevisionId=null,string Origin="Explicit");
public record CoveragePreview(Guid DraftId,long DraftVersion,string Title,Catalog Catalog);
public static class CoverageEditing
{
    public static bool Key(MappingCoverage row,string type,Guid id,Mapping m)=>row.OwnerType==type && row.OwnerId==id && row.KCId==m.KCId && row.Role==m.Role && row.EvidenceMode==m.Mode && row.Step==m.Step && row.EvidenceShare==m.Share;
    public static MappingCoverage? Entry(Catalog c,MappingOwner owner,Mapping m)=>(c.MappingCoverage??[]).SingleOrDefault(r=>Key(r,owner.OwnerType,owner.Id,m));
    public static decimal Weight(Catalog c,MappingOwner owner,Mapping m)=>Entry(c,owner,m)?.CoverageWeight??1;
    public static string[] Validate(Catalog c)
    {
        if(c.MappingCoverage==null)return[];
        if(c.Kcs==null || c.Questions==null || c.Lessons==null || c.Resources==null || c.MappingCoverage.Any(r=>r==null) || c.Kcs.Any(k=>k==null) || c.Questions.Any(q=>q==null || q.Mappings==null || q.Mappings.Any(m=>m==null)) || c.Lessons.Any(l=>l==null || l.KCIds==null) || c.Resources.Any(r=>r==null || r.KCIds==null))return["覆盖元数据需要有效内容结构和条目。"];
        var errors=new List<string>();
        if(c.Kcs.Select(k=>k.Id).Distinct().Count()!=c.Kcs.Length || PublishedMappings.Owners(c).GroupBy(o=>new{o.OwnerType,o.OwnerId}).Any(g=>g.Count()>1))return["覆盖内容的能力和对象身份不能重复。"];
        foreach(var row in c.MappingCoverage)
        {
            var selection=PublishedMappings.Owners(c).FirstOrDefault(o=>o.OwnerType==row.OwnerType && o.OwnerId==row.OwnerId);
            if(selection==null){errors.Add("覆盖条目必须对应有明确修订的当前对象。");continue;}
            if(selection.OwnerId==Guid.Empty || selection.OwnerRevisionId==Guid.Empty){errors.Add("覆盖对象须有明确身份和修订。");continue;}
            var owner=MappingSuggestions.Owner(c,selection);
            if(!PublishedMappings.Projection(c,owner).Any(m=>Key(row,owner.OwnerType,owner.Id,m)))errors.Add("覆盖条目与当前能力、角色、份额或观察点不匹配，请核对变化。");
            if(row.CoverageWeight is <0 or >1 || decimal.Round(row.CoverageWeight,6)!=row.CoverageWeight || row.OwnerType!="Question" && row.CoverageWeight<=0)errors.Add("覆盖权重须在0到1且最多六位小数；课时/资源需正权重。");
            if(row.Origin is not "Explicit" and not "Default" and not "Inherited" || row.Origin=="Default" && row.CoverageWeight!=1 || (row.Origin=="Inherited")!=(row.SourceSetRevisionId!=null))errors.Add("覆盖来源需明确：默认为1、人工填写无旧引用、继承须有真实来源容器。");
        }
        if(c.MappingCoverage.GroupBy(r=>new{r.OwnerType,r.OwnerId,r.KCId,r.Role,r.EvidenceMode,r.Step,r.EvidenceShare}).Any(g=>g.Count()>1))errors.Add("同一关联不能重复提交覆盖权重。");
        return errors.ToArray();
    }
    public static async Task ValidateSources(Database db,Guid family,Catalog c)
    {
        var errors=Validate(c);if(errors.Length>0)throw new ApiError(422,"INVALID_MAPPING_COVERAGE",string.Join(" ",errors));
        foreach(var row in (c.MappingCoverage??[]).Where(r=>r.SourceSetRevisionId!=null))
        {
            var set=await db.Set<MappingSetRevision>().SingleOrDefaultAsync(s=>s.Id==row.SourceSetRevisionId && s.FamilyId==family);
            if(set==null || set.OwnerType!=row.OwnerType || set.OwnerId!=row.OwnerId)throw new ApiError(422,"COVERAGE_SOURCE_UNKNOWN","覆盖来源不属于当前家庭和对象。");
            var items=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==family && i.SetRevisionId==set.Id).ToArrayAsync();
            var item=items.SingleOrDefault(i=>i.KCId==row.KCId && i.Role==row.Role && i.EvidenceMode==row.EvidenceMode && i.Step==row.Step && i.EvidenceShare==row.EvidenceShare);
            var definition=item==null?null:await db.Set<ContentRevision>().SingleOrDefaultAsync(r=>r.Id==item.KCRevisionId && r.FamilyId==family && r.EntityType=="KC");
            var current=c.Kcs.SingleOrDefault(k=>k.Id==row.KCId);
            if(item==null || item.CoverageWeight!=row.CoverageWeight || definition==null || current==null || string.IsNullOrWhiteSpace(current.Behavior) || string.IsNullOrWhiteSpace(current.Boundary) || Content.MeasurementSignature(Json.Read<KC>(definition.Definition))!=Content.MeasurementSignature(current))
                throw new ApiError(422,"COVERAGE_SOURCE_CHANGED","继承权重与原映射/测量含义不符；请明确填写新权重并重新审核。");
        }
    }
    public static async Task<Catalog> Preview(Database db,ContentDraft draft)
    {
        var c=Json.Read<Catalog>(draft.Payload);
        if(c.Kcs==null || c.Questions==null || c.Resources==null || c.Lessons==null || c.Kcs.Any(k=>k==null) || c.Questions.Any(q=>q==null || q.Mappings==null || q.Mappings.Any(m=>m==null)) || c.Resources.Any(r=>r==null || r.KCIds==null) || c.Lessons.Any(l=>l==null || l.KCIds==null))throw new ApiError(422,"SOURCE_DRAFT_INVALID","请先补齐有效的能力、题目、课时和资源结构，再预览映射。");
        await ValidateSources(db,draft.FamilyId,c);var rows=new List<MappingCoverage>();
        foreach(var selection in PublishedMappings.Owners(c))
        {
            var owner=MappingSuggestions.Owner(c,selection);var projection=PublishedMappings.Projection(c,owner);
            var candidates=await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==draft.FamilyId && s.OwnerType==owner.OwnerType && s.OwnerId==owner.Id && s.OwnerRevisionId==owner.RevisionId).OrderByDescending(s=>s.ReviewDecisionId!=null).ThenByDescending(s=>s.RevisionNo).ToArrayAsync();
            MappingSetRevision? source=null;MappingSetItem[] fixedItems=[];
            foreach(var set in candidates)
            {
                var items=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==draft.FamilyId && i.SetRevisionId==set.Id).OrderBy(i=>i.Sequence).ToArrayAsync();
                if(set.OwnerDefinitionHash==Content.Hash(MappingSuggestions.Definition(c,owner.OwnerType,owner.Id)) && PublishedMappings.Matches(c,owner,set,items) && (!(c.MappingCoverage??[]).Any(r=>r.OwnerType==owner.OwnerType && r.OwnerId==owner.Id) || projection.Select((m,i)=>Weight(c,owner,m)==items[i].CoverageWeight).All(v=>v)))
                {source=set;fixedItems=items;break;}
            }
            foreach(var pair in projection.Select((m,i)=>(m,i)))
            {
                if(source!=null)rows.Add(new(owner.OwnerType,owner.Id,pair.m.KCId,pair.m.Role,pair.m.Mode,pair.m.Step,pair.m.Share,fixedItems[pair.i].CoverageWeight,source.Id,"Inherited"));
                else rows.Add(Entry(c,owner,pair.m)??new(owner.OwnerType,owner.Id,pair.m.KCId,pair.m.Role,pair.m.Mode,pair.m.Step,pair.m.Share,1,null,"Default"));
            }
        }
        return c with{MappingCoverage=rows.ToArray()};
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/content/drafts/{id:guid}/mapping-preview",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var draft=await db.Drafts.SingleOrDefaultAsync(d=>d.FamilyId==a.FamilyId && d.Id==id)??throw new ApiError(404,"NOT_FOUND","内容草稿不存在。");
            return new CoveragePreview(id,draft.Version,draft.Title,await Preview(db,draft));
        });
    }
}
