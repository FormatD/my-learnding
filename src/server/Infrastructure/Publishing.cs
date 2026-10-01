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
        var c=Json.Read<Catalog>(release.Payload);
        async Task Add(Guid identity,Guid revision,string type,string code,object definition)
        {
            var stable=await db.Set<ContentIdentity>().SingleOrDefaultAsync(i => i.Id==identity);
            if (stable==null && await db.Set<ContentIdentity>().AnyAsync(i=>i.FamilyId==release.FamilyId && i.EntityType==type && i.Code==code)) throw new ApiError(422,"IDENTITY_CODE_CONFLICT","已有相同编码的正式内容，请沿用其稳定身份创建修订。");
            if (stable!=null && (stable.FamilyId!=release.FamilyId || stable.EntityType!=type || stable.Code!=code)) throw new ApiError(422,"IDENTITY_CONFLICT","内容身份不能跨家庭复用或改变含义。");
            if (stable==null) db.Add(new ContentIdentity { Id=identity,FamilyId=release.FamilyId,EntityType=type,Code=code });
            var text=Json.Write(definition);var hash=Content.Hash(text);var old=await db.Set<ContentRevision>().SingleOrDefaultAsync(r => r.Id==revision);
            if (old!=null && (old.FamilyId!=release.FamilyId || old.IdentityId!=identity || old.Hash!=hash)) throw new ApiError(422,"REVISION_IMMUTABLE","已发布修订内容有变化。请给修改的内容分配新的修订身份。");
            if (old==null) db.Add(new ContentRevision { Id=revision,FamilyId=release.FamilyId,IdentityId=identity,EntityType=type,Hash=hash,Definition=text });
            db.Add(new ReleaseItem { FamilyId=release.FamilyId,ReleaseId=release.Id,IdentityId=identity,RevisionId=revision,EntityType=type });
        }
        foreach (var k in c.Kcs) await Add(k.Id,k.RevisionId,"KC",k.Code,k);
        foreach (var q in c.Questions) await Add(q.Id,q.RevisionId,"Question",q.Id.ToString(),q);
    }
}
