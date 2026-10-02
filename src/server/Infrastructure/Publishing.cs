using Microsoft.EntityFrameworkCore;

namespace Learning;
public class ContentIdentity : Row
{
    public string EntityType { get; set; } = "KC";
    public string Code { get; set; } = "";
    public string Status { get; set; } = "Active";
}
public class ContentRevision : Row
{
    public Guid IdentityId { get; set; }
    public string EntityType { get; set; } = "KC";
    public string Hash { get; set; } = "";
    public string Definition { get; set; } = "";
}
public class ReleaseItem : Row
{
    public Guid ReleaseId { get; set; }
    public Guid IdentityId { get; set; }
    public Guid RevisionId { get; set; }
    public string EntityType { get; set; } = "KC";
}
public static class Publishing
{
    public static async Task Register(Database db,Release release)
    {
        var c=Json.Read<Catalog>(release.Payload);await CatalogDirectory.ValidateSources(db,release.FamilyId,c);
        var owners=c.Questions.Select(q=>new MappingOwnerSelection("Question",q.Id,q.RevisionId)).Concat(c.Lessons.Where(l=>l.RevisionId!=null).Select(l=>new MappingOwnerSelection("Lesson",l.Id,l.RevisionId!.Value))).Concat(c.Resources.Where(r=>r.RevisionId!=null).Select(r=>new MappingOwnerSelection("Resource",r.Id,r.RevisionId!.Value))).ToArray();
        var ownerRevisions=owners.Select(o=>o.OwnerRevisionId).ToArray();
        var mappingSets=await db.Set<MappingSetRevision>().Where(s=>s.FamilyId==release.FamilyId && ownerRevisions.Contains(s.OwnerRevisionId)).ToArrayAsync();
        foreach(var set in mappingSets)
        {
            if(!owners.Any(o=>o.OwnerType==set.OwnerType && o.OwnerId==set.OwnerId && o.OwnerRevisionId==set.OwnerRevisionId) || Content.Hash(MappingSuggestions.Definition(c,set.OwnerType,set.OwnerId))!=set.OwnerDefinitionHash)
                throw new ApiError(422,"MAPPING_REVISION_IMMUTABLE","人工接受的映射修订内容已变，请保存新的对象修订并重新审核。");
            var setItems=await db.Set<MappingSetItem>().Where(i=>i.FamilyId==release.FamilyId && i.SetRevisionId==set.Id).ToArrayAsync();
            if(setItems.Any(i=>!c.Kcs.Any(k=>k.Id==i.KCId && k.RevisionId==i.KCRevisionId)))throw new ApiError(422,"MAPPING_LIBRARY_REVISION_CHANGED","此映射修订固定的能力版本已变，请创建新的对象映射修订并重新审核。");
        }
        async Task Add(Guid identity,Guid revision,string type,string code,object definition)
        {
            var stable=await db.Set<ContentIdentity>().SingleOrDefaultAsync(i => i.Id==identity);
            if (stable==null && await db.Set<ContentIdentity>().AnyAsync(i=>i.FamilyId==release.FamilyId && i.EntityType==type && i.Code==code)) throw new ApiError(422,"IDENTITY_CODE_CONFLICT","已有相同编码的正式内容，请沿用其稳定身份创建修订。");
            if (stable!=null && (stable.FamilyId!=release.FamilyId || stable.EntityType!=type || stable.Code!=code)) throw new ApiError(422,"IDENTITY_CONFLICT","内容身份不能跨家庭复用或改变含义。");
            if(stable!=null && type=="KC")
            {
                if(stable.Status=="Deprecated" && !await db.Releases.AnyAsync(r=>r.Id==release.Id))throw new ApiError(422,"KC_DEPRECATED","新发布不能重新启用已停用能力；历史版本与已领取任务仍保留。请使用新的能力身份。");
                var prior=await db.Set<ContentRevision>().Where(r=>r.IdentityId==identity && r.EntityType=="KC").OrderBy(r=>r.CreatedAt).FirstOrDefaultAsync();
                if(prior!=null && Content.MeasurementSignature(Json.Read<KC>(prior.Definition))!=Content.MeasurementSignature((KC)definition))throw new ApiError(422,"MEASUREMENT_IDENTITY_CHANGED","已发布能力的测量行为、边界、类型与覆盖要求保持固定；改变测量含义请新增独立能力。");
            }
            if (stable==null) db.Add(new ContentIdentity { Id=identity,FamilyId=release.FamilyId,EntityType=type,Code=code });
            var text=Json.Write(definition);var hash=Content.Hash(text);var old=await db.Set<ContentRevision>().SingleOrDefaultAsync(r => r.Id==revision);
            if (old!=null && (old.FamilyId!=release.FamilyId || old.IdentityId!=identity || old.Hash!=hash)) throw new ApiError(422,"REVISION_IMMUTABLE","已发布修订内容有变化。请给修改的内容分配新的修订身份。");
            if (old==null) db.Add(new ContentRevision { Id=revision,FamilyId=release.FamilyId,IdentityId=identity,EntityType=type,Hash=hash,Definition=text });
            db.Add(new ReleaseItem { FamilyId=release.FamilyId,ReleaseId=release.Id,IdentityId=identity,RevisionId=revision,EntityType=type });
        }
        var legacyResourceIds=c.Resources.Where(r=>r.RevisionId==null).Select(r=>r.Id).ToArray();
        if(legacyResourceIds.Length>0 && !await db.Releases.AnyAsync(r=>r.Id==release.Id)
            && await db.Set<ContentIdentity>().AnyAsync(i=>i.FamilyId==release.FamilyId && i.EntityType=="Resource" && legacyResourceIds.Contains(i.Id)))
            throw new ApiError(422,"RESOURCE_REVISION_REQUIRED","已有独立修订的资源不能退回未记录版本，请保存新修订后重新审核。");
        foreach(var resource in c.Resources.Where(r=>r.RevisionId!=null))await Add(resource.Id,resource.RevisionId!.Value,"Resource",resource.Id.ToString(),resource);
        foreach(var b in c.Textbooks??[])await Add(b.Id,b.RevisionId,"Textbook",b.Id.ToString(),b);
        foreach(var u in c.Units??[])await Add(u.Id,u.RevisionId,"TextbookUnit",u.Id.ToString(),u);
        foreach(var course in c.Courses??[])await Add(course.Id,course.RevisionId,"Course",course.Id.ToString(),course);
        foreach(var lesson in c.Lessons.Where(l=>l.RevisionId!=null))await Add(lesson.Id,lesson.RevisionId!.Value,"Lesson",lesson.Id.ToString(),lesson);
        foreach (var k in c.Kcs) await Add(k.Id,k.RevisionId,"KC",k.Code,k);
        foreach (var q in c.Questions) await Add(q.Id,q.RevisionId,"Question",q.Id.ToString(),q);
    }
}
