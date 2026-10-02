using Microsoft.EntityFrameworkCore;

namespace Learning;
public class KCChangeProposal : Row
{
    public string ProposalType { get; set; } = "Split";
    public string Rationale { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public long Version { get; set; } = 1;
    public Guid FromReleaseId { get; set; }
    public Guid EffectiveReleaseId { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? AppliedBy { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}
public class KCChangeProposalItem : Row
{
    public Guid ProposalId { get; set; }
    public string Side { get; set; } = "From";
    public Guid KCId { get; set; }
    public Guid ProposedRevisionId { get; set; }
    public decimal? Weight { get; set; }
}
public class KnowledgeMigration : Row
{
    public Guid ProposalId { get; set; }
    public Guid FromKCId { get; set; }
    public Guid ToKCId { get; set; }
    public Guid FromRevisionId { get; set; }
    public Guid ToRevisionId { get; set; }
    public string MigrationType { get; set; } = "Split";
    public decimal? Weight { get; set; }
    public Guid EffectiveReleaseId { get; set; }
    public string EvidencePolicy { get; set; } = "PreserveHistoryNoTransfer";
}
public class KCProposalEvent : Row
{
    public Guid ProposalId { get; set; }
    public long Version { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "Created";
    public string Reason { get; set; } = "";
    public string Snapshot { get; set; } = "";
}
public record KCChangeTarget(Guid KCId,decimal? Weight=null);
public record KCChangeInput(string ProposalType,string Rationale,Guid FromReleaseId,Guid EffectiveReleaseId,Guid[] FromKCIds,KCChangeTarget[] Targets);
public record KCChangeDecision(string Decision,string Reason);
public record KCChangeApply(string PreviewHash,string Confirm);
public static class KnowledgeChanges
{
    const string Notice="只记录经过人工确认的能力变更与正式版本关系；旧作答、证据、复习日程和任务保留，不复制或分摊掌握状态，也不自动切换学生内容。";
    static ApiError Invalid(string message)=>new(422,"KC_CHANGE_INVALID",message);
    static async Task<KCChangeProposal> Owned(Database db,Actor a,Guid id)=>await db.Set<KCChangeProposal>().SingleOrDefaultAsync(p=>p.Id==id && p.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到变更提案。");
    static Task<KCChangeProposalItem[]> Items(Database db,Guid id)=>db.Set<KCChangeProposalItem>().Where(i=>i.ProposalId==id).OrderBy(i=>i.Side).ThenBy(i=>i.KCId).ToArrayAsync();
    static async Task<KCChangeProposalItem[]> Validate(Database db,Actor a,KCChangeInput input,Guid proposalId)
    {
        if(string.IsNullOrWhiteSpace(input.Rationale) || input.Rationale.Length>4000)throw Invalid("请填写能力变更理由，最多4000字。");
        var from=input.FromKCIds??[];var targets=input.Targets??[];if(targets.Any(t=>t is null))throw Invalid("目标能力不能为空。");var to=targets.Select(t=>t.KCId).ToArray();
        if(from.Length is <1 or >20 || to.Length is <1 or >20 || from.Distinct().Count()!=from.Length || to.Distinct().Count()!=to.Length || from.Intersect(to).Any())throw Invalid("来源与目标能力必须各自唯一且互不重叠，最多各20项。");
        if(!(input.ProposalType switch {"Split"=>from.Length==1 && to.Length>=2,"Merge"=>from.Length>=2 && to.Length==1,"Redirect"=>from.Length==1 && to.Length==1,_=>false}))throw Invalid("拆分为一对多，合并为多对一，替换为一对一。");
        if(targets.Any(t=>t.Weight is <=0 or >1 || t.Weight is decimal w && decimal.Round(w,6)!=w))throw Invalid("可选权重必须大于0且不超过1，最多6位小数；权重仅记录，不用于转移证据。");
        var old=await db.Releases.SingleOrDefaultAsync(r=>r.Id==input.FromReleaseId && r.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到来源发布版本。");
        var effective=await db.Releases.SingleOrDefaultAsync(r=>r.Id==input.EffectiveReleaseId && r.FamilyId==a.FamilyId)??throw new ApiError(404,"NOT_FOUND","找不到生效发布版本。");
        if(old.Withdrawn || effective.Withdrawn || old.Number>=effective.Number)throw Invalid("请选择未撤回的来源版本与较新的正式生效版本。");
        var oldCatalog=Json.Read<Catalog>(old.Payload);var newCatalog=Json.Read<Catalog>(effective.Payload);
        if(from.Any(id=>!oldCatalog.Kcs.Any(k=>k.Id==id)) || to.Any(id=>!newCatalog.Kcs.Any(k=>k.Id==id)))throw Invalid("所选能力必须存在于对应的正式版本。");
        if(from.Any(id=>newCatalog.Kcs.Any(k=>k.Id==id)))throw Invalid("生效版本应使用新能力替代来源能力，请先人工整理题目、课时与映射并发布。");
        // Targets are independent identities first introduced in this effective release.
        if(input.ProposalType!="Redirect" && await (from item in db.Set<ReleaseItem>() join release in db.Releases on item.ReleaseId equals release.Id where item.FamilyId==a.FamilyId && item.EntityType=="KC" && to.Contains(item.IdentityId) && release.Number<effective.Number select item.Id).AnyAsync())throw Invalid("目标能力必须在生效版本首次发布，不能把已有能力伪装为拆分后的新能力。");
        var identities=await db.Set<ContentIdentity>().Where(k=>k.FamilyId==a.FamilyId && k.EntityType=="KC" && (from.Contains(k.Id)||to.Contains(k.Id))).ToArrayAsync();
        if(identities.Length!=from.Length+to.Length || identities.Any(k=>k.Status!="Active"))throw Invalid("所选能力必须具有有效正式身份，已停用的来源不能再次应用变更。");
        return from.Select(id=>new KCChangeProposalItem{FamilyId=a.FamilyId,ProposalId=proposalId,Side="From",KCId=id,ProposedRevisionId=oldCatalog.Kcs.Single(k=>k.Id==id).RevisionId})
            .Concat(targets.Select(t=>new KCChangeProposalItem{FamilyId=a.FamilyId,ProposalId=proposalId,Side="To",KCId=t.KCId,ProposedRevisionId=newCatalog.Kcs.Single(k=>k.Id==t.KCId).RevisionId,Weight=t.Weight})).ToArray();
    }
    static KCChangeInput Input(KCChangeProposal p,KCChangeProposalItem[] items)=>new(p.ProposalType,p.Rationale,p.FromReleaseId,p.EffectiveReleaseId,items.Where(i=>i.Side=="From").Select(i=>i.KCId).ToArray(),items.Where(i=>i.Side=="To").Select(i=>new KCChangeTarget(i.KCId,i.Weight)).ToArray());
    static void Event(Database db,KCChangeProposal p,KCChangeProposalItem[] items,Actor a,string action,string reason)=>db.Add(new KCProposalEvent{FamilyId=a.FamilyId,ProposalId=p.Id,Version=p.Version,ActorId=a.Id,Action=action,Reason=reason,Snapshot=Json.Write(new{proposal=p,items})});
    static async Task<string> Hash(Database db,KCChangeProposal p,KCChangeProposalItem[] items)
    {
        var family=await db.Families.SingleAsync(f=>f.Id==p.FamilyId);
        return Content.Hash(Json.Write(new{proposal=p,items,familyVersion=family.Version}));
    }
    public static async Task<object> PublishedHistory(Database db,Guid family)
    {
        var proposals=await db.Set<KCChangeProposal>().Where(p=>p.FamilyId==family && p.Status=="Applied").ToArrayAsync();var ids=proposals.Select(p=>p.Id).ToArray();
        return new{proposals,items=await db.Set<KCChangeProposalItem>().Where(i=>i.FamilyId==family && ids.Contains(i.ProposalId)).ToArrayAsync(),events=await db.Set<KCProposalEvent>().Where(e=>e.FamilyId==family && ids.Contains(e.ProposalId)).ToArrayAsync(),migrations=await db.Set<KnowledgeMigration>().Where(m=>m.FamilyId==family && ids.Contains(m.ProposalId)).ToArrayAsync()};
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/content/kc-changes",async(Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");
            return new{proposals=await db.Set<KCChangeProposal>().Where(p=>p.FamilyId==a.FamilyId).OrderByDescending(p=>p.CreatedAt).ToArrayAsync(),items=await db.Set<KCChangeProposalItem>().Where(i=>i.FamilyId==a.FamilyId).ToArrayAsync(),migrations=await db.Set<KnowledgeMigration>().Where(m=>m.FamilyId==a.FamilyId).ToArrayAsync(),identities=await db.Set<ContentIdentity>().Where(k=>k.FamilyId==a.FamilyId && k.EntityType=="KC").Select(k=>new{k.Id,k.Status}).ToArrayAsync(),notice=Notice};
        });
        api.MapGet("/content/kc-changes/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var p=await Owned(db,a,id);return new{proposal=p,items=await Items(db,id),events=await db.Set<KCProposalEvent>().Where(e=>e.ProposalId==id).OrderBy(e=>e.Version).ToArrayAsync(),migrations=await db.Set<KnowledgeMigration>().Where(m=>m.ProposalId==id).ToArrayAsync(),notice=Notice};
        });
        api.MapPost("/content/kc-changes",async(KCChangeInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var p=new KCChangeProposal{FamilyId=a.FamilyId,ProposalType=input.ProposalType,Rationale=(input.Rationale??"").Trim(),FromReleaseId=input.FromReleaseId,EffectiveReleaseId=input.EffectiveReleaseId,CreatedBy=a.Id};var items=await Validate(db,a,input,p.Id);db.Add(p);db.AddRange(items);Event(db,p,items,a,"Created",p.Rationale);return TypedResults.Created($"/api/v1/content/kc-changes/{p.Id}",p);
        });
        api.MapPut("/content/kc-changes/{id:guid}",async(Guid id,KCChangeInput input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var p=await Owned(db,a,id);if(p.Status!="Draft")throw new ApiError(409,"KC_CHANGE_FROZEN","只有草稿可以编辑；提交审核后保持记录不变。");var items=await Validate(db,a,input,id);db.RemoveRange(await Items(db,id));db.AddRange(items);p.ProposalType=input.ProposalType;p.Rationale=input.Rationale.Trim();p.FromReleaseId=input.FromReleaseId;p.EffectiveReleaseId=input.EffectiveReleaseId;p.Version++;Event(db,p,items,a,"Edited",p.Rationale);return TypedResults.Ok(p);
        });
        api.MapPost("/content/kc-changes/{id:guid}:submit",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var p=await Owned(db,a,id);if(p.Status!="Draft")throw new ApiError(409,"KC_CHANGE_STATE","只可提交草稿。");var items=await Items(db,id);await Validate(db,a,Input(p,items),id);p.Status="InReview";p.Version++;Event(db,p,items,a,"Submitted",p.Rationale);return TypedResults.Ok(p);
        });
        api.MapPost("/content/kc-changes/{id:guid}:decide",async(Guid id,KCChangeDecision input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Publisher");var p=await Owned(db,a,id);if(p.Status!="InReview")throw new ApiError(409,"KC_CHANGE_STATE","提案不在待审核状态。");if(input.Decision is not "Approved" and not "Rejected" || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>4000)throw Invalid("请选择通过或拒绝，并填写审核依据，最多4000字。");var items=await Items(db,id);if(input.Decision=="Approved")await Validate(db,a,Input(p,items),id);p.Status=input.Decision;p.ReviewedBy=a.Id;p.ReviewedAt=DateTimeOffset.UtcNow;p.Version++;Event(db,p,items,a,input.Decision,input.Reason.Trim());return TypedResults.Ok(p);
        });
        api.MapGet("/content/kc-changes/{id:guid}/preview",async(Guid id,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Publisher");var p=await Owned(db,a,id);if(p.Status!="Approved")throw new ApiError(422,"KC_CHANGE_REVIEW_REQUIRED","提案须先通过人工审核。");var items=await Items(db,id);await Validate(db,a,Input(p,items),id);return new{proposal=p,items,previewHash=await Hash(db,p,items),requiredConfirm="记录能力变更",notice=Notice};
        });
        api.MapPost("/content/kc-changes/{id:guid}:apply",async(Guid id,KCChangeApply input,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("Publisher");var p=await Owned(db,a,id);if(p.Status!="Approved" || p.ReviewedBy==null)throw new ApiError(422,"KC_CHANGE_REVIEW_REQUIRED","提案须先通过人工审核。");var items=await Items(db,id);if(input.PreviewHash!=await Hash(db,p,items))throw new ApiError(412,"PREVIEW_CHANGED","范围已变化，请重新预览。");if(input.Confirm!="记录能力变更")throw Invalid("请明确确认记录能力变更。");await Validate(db,a,Input(p,items),id);
            var from=items.Where(i=>i.Side=="From").ToArray();var to=items.Where(i=>i.Side=="To").ToArray();var ids=from.Select(i=>i.KCId).ToArray();foreach(var identity in await db.Set<ContentIdentity>().Where(k=>k.FamilyId==a.FamilyId && ids.Contains(k.Id)).ToArrayAsync())identity.Status="Deprecated";
            foreach(var f in from)foreach(var t in to)db.Add(new KnowledgeMigration{FamilyId=a.FamilyId,ProposalId=id,FromKCId=f.KCId,ToKCId=t.KCId,FromRevisionId=f.ProposedRevisionId,ToRevisionId=t.ProposedRevisionId,MigrationType=p.ProposalType,Weight=t.Weight,EffectiveReleaseId=p.EffectiveReleaseId});
            p.Status="Applied";p.AppliedBy=a.Id;p.AppliedAt=DateTimeOffset.UtcNow;p.Version++;Event(db,p,items,a,"Applied",Notice);return TypedResults.Ok(new{proposal=p,notice=Notice});
        });
    }
}
