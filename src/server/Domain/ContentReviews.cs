using Microsoft.EntityFrameworkCore;

namespace Learning;
public class ContentReviewRecord : Row
{
    public Guid DraftId { get; set; }
    public long DraftVersion { get; set; }
    public string SourceTitle { get; set; } = "";
    public string SourcePayload { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public Guid ReviewerId { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Scope { get; set; } = "CatalogSnapshot";
    public string Reason { get; set; } = "";
    public string ReasonSource { get; set; } = "CommandConfirmation";
    public Guid? PublishedReleaseId { get; set; }
    public string? PublishedMappingVersion { get; set; }
}
public record ContentReviewInput(long ExpectedDraftVersion,string? Reason=null);
public static class ContentReviews
{
    public static ContentReviewRecord Capture(ContentDraft draft,Actor actor,ContentReviewInput? input)
    {
        if(input?.ExpectedDraftVersion is <=0 || input?.Reason?.Length>4000)throw new ApiError(422,"INVALID_REVIEW","草稿版本须为正整数，审核备注最多4000字。");
        if(input?.ExpectedDraftVersion!=null && input.ExpectedDraftVersion!=draft.Version)throw new ApiError(412,"DRAFT_CHANGED","草稿已变化，请刷新并重新核对答案和映射。");
        var reason=input?.Reason?.Trim();
        return new(){FamilyId=actor.FamilyId,DraftId=draft.Id,DraftVersion=draft.Version,SourceTitle=draft.Title,SourcePayload=draft.Payload,PayloadHash=Content.Hash(draft.Payload),ReviewerId=actor.Id,
            Reason=string.IsNullOrWhiteSpace(reason)?"审核者执行整份内容审核确认。":reason,ReasonSource=string.IsNullOrWhiteSpace(reason)?"CommandConfirmation":"UserProvided"};
    }
    public static async Task<ContentDraft> Approve(Database db,Actor actor,ContentDraft draft,ContentReviewInput? input)
    {
        if(draft.Status=="Published")throw new ApiError(409,"IMMUTABLE","版本已发布。");
        var review=Capture(draft,actor,input);
        await CoverageEditing.ValidateSources(db,actor.FamilyId,Json.Read<Catalog>(draft.Payload));
        await CatalogDirectory.ValidateSources(db,actor.FamilyId,Json.Read<Catalog>(draft.Payload));var errors=Content.Validate(Json.Read<Catalog>(draft.Payload));
        if(errors.Length>0)throw new ApiError(422,"CONTENT_INVALID",string.Join("；",errors));
        db.Add(review);draft.Status="Approved";draft.ReviewedBy=actor.Id;
        db.Audits.Add(new(){FamilyId=actor.FamilyId,ActorId=actor.Id,Action="ContentSnapshotReviewed",Details=Json.Write(new{review.Id,review.DraftId,review.DraftVersion,review.PayloadHash,review.Scope,review.Reason,review.ReasonSource})});
        return draft;
    }
    public static async Task<ContentReviewRecord> ForPublish(Database db,ContentDraft draft)
    {
        var hash=Content.Hash(draft.Payload);
        var record=await db.Set<ContentReviewRecord>().Where(r=>r.FamilyId==draft.FamilyId && r.DraftId==draft.Id && r.DraftVersion==draft.Version && r.ReviewerId==draft.ReviewedBy && r.PayloadHash==hash && r.PublishedReleaseId==null)
            .OrderByDescending(r=>r.ReviewedAt).ThenByDescending(r=>r.Id).FirstOrDefaultAsync();
        if(record==null || record.SourcePayload!=draft.Payload || record.SourceTitle!=draft.Title)throw new ApiError(422,"REVIEW_SNAPSHOT_REQUIRED","当前草稿没有完整审核快照，请重新审核后发布。");
        return record;
    }
}
