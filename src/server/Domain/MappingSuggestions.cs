namespace Learning;

public class MappingRun : Row
{
    public Guid SourceDraftId { get; set; }
    public long SourceDraftVersion { get; set; }
    public string SourceTitle { get; set; } = "";
    public string SourcePayload { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public Guid LibraryReleaseId { get; set; }
    public string LibraryHash { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string Provider { get; set; } = "Mock";
    public string Model { get; set; } = Retrieval.Space;
    public string PromptVersion { get; set; } = "mapping-suggestion/1";
    public string Status { get; set; } = "Completed";
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class MappingSuggestion : Row
{
    public Guid RunId { get; set; }
    public string OwnerType { get; set; } = "Question";
    public Guid OwnerId { get; set; }
    public Guid OwnerRevisionId { get; set; }
    public string OwnerTitle { get; set; } = "";
    public string EvidencePolicy { get; set; } = "NoEvidence";
    public string SuggestedItems { get; set; } = "[]";
    public string Matches { get; set; } = "[]";
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelResultPayload { get; set; }
    public string ValidationFlags { get; set; } = "[]";
    public string Status { get; set; } = "Pending";
    public long Version { get; set; } = 1;
}
public class MappingReviewDecision : Row
{
    public Guid SuggestionId { get; set; }
    public string Decision { get; set; } = "Reject";
    public Guid ReviewerId { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = "";
    public string OriginalPayloadHash { get; set; } = "";
    public string CorrectedPayload { get; set; } = "";
    public string CorrectedPayloadHash { get; set; } = "";
    public Guid? CreatedDraftId { get; set; }
}
public class MappingSetRevision : Row
{
    public Guid DraftId { get; set; }
    public Guid? ReviewDecisionId { get; set; }
    public Guid? ContentReviewRecordId { get; set; }
    public string CoverageOrigin { get; set; } = "HumanReviewed";
    public string OwnerType { get; set; } = "Question";
    public Guid OwnerId { get; set; }
    public Guid OwnerRevisionId { get; set; }
    public Guid OriginalOwnerRevisionId { get; set; }
    public string OwnerDefinitionHash { get; set; } = "";
    public int RevisionNo { get; set; }
    public string EvidencePolicy { get; set; } = "NoEvidence";
    public string ReviewStatus { get; set; } = "ReviewedDraft";
}
public class MappingSetItem : Row
{
    public Guid SetRevisionId { get; set; }
    public Guid KCId { get; set; }
    public Guid KCRevisionId { get; set; }
    public string Role { get; set; } = "Primary";
    public decimal CoverageWeight { get; set; } = 1;
    public decimal EvidenceShare { get; set; }
    public string EvidenceMode { get; set; } = "None";
    public string? Step { get; set; }
    public int Sequence { get; set; }
    public decimal? ModelScore { get; set; }
    public string SourceRefs { get; set; } = "[]";
}
public record MappingOwnerSelection(string OwnerType,Guid OwnerId,Guid OwnerRevisionId);
public record SuggestedMappingItem(Guid KCId,Guid KCRevisionId,string Role,decimal CoverageWeight,decimal EvidenceShare,string EvidenceMode,string? Step,int Sequence,decimal? ModelScore,string[] SourceRefs);
public record MappingProposal(string EvidencePolicy,SuggestedMappingItem[] Items);
public record MappingOwner(string OwnerType,Guid Id,Guid RevisionId,string Title,string Text,string EvidencePolicy,string QuestionType,Mapping[] Mappings);

public static class MappingSuggestions
{
    public static bool SameMapping(MappingProposal a,MappingProposal b)=>a.EvidencePolicy==b.EvidencePolicy && a.Items.Length==b.Items.Length &&
        a.Items.OrderBy(i=>i.Sequence).Zip(b.Items.OrderBy(i=>i.Sequence)).All(pair=>
            pair.First.KCId==pair.Second.KCId && pair.First.KCRevisionId==pair.Second.KCRevisionId && pair.First.Role==pair.Second.Role && pair.First.CoverageWeight==pair.Second.CoverageWeight && pair.First.EvidenceShare==pair.Second.EvidenceShare && pair.First.EvidenceMode==pair.Second.EvidenceMode && pair.First.Step==pair.Second.Step && pair.First.Sequence==pair.Second.Sequence && pair.First.SourceRefs.SequenceEqual(pair.Second.SourceRefs));
    public static string Definition(Catalog source,string ownerType,Guid ownerId)=>ownerType switch
    {
        "Question"=>Json.Write(source.Questions.Single(q=>q.Id==ownerId)),
        "Lesson"=>Json.Write(source.Lessons.Single(l=>l.Id==ownerId)),
        "Resource"=>Json.Write(source.Resources.Single(r=>r.Id==ownerId)),
        _=>throw new ApiError(422,"OWNER_REVISION_UNKNOWN","对象类型无效。")
    };
    public static MappingOwner Owner(Catalog source,MappingOwnerSelection selection)
    {
        MappingOwner? owner=selection.OwnerType switch
        {
            "Question"=>source.Questions.Where(q=>q.Id==selection.OwnerId).Select(q=>new MappingOwner("Question",q.Id,q.RevisionId,q.Stem,q.Stem+" "+q.Explanation,q.Policy,q.Type,q.Mappings)).SingleOrDefault(),
            "Lesson"=>source.Lessons.Where(l=>l.Id==selection.OwnerId && l.RevisionId!=null).Select(l=>new MappingOwner("Lesson",l.Id,l.RevisionId!.Value,l.Title,l.Title,"NoEvidence","",[])).SingleOrDefault(),
            "Resource"=>source.Resources.Where(r=>r.Id==selection.OwnerId && r.RevisionId!=null).Select(r=>new MappingOwner("Resource",r.Id,r.RevisionId!.Value,r.Title,r.Title+" "+r.PaperReference,"NoEvidence","",[])).SingleOrDefault(),
            _=>null
        };
        if(owner==null || owner.Id==Guid.Empty || owner.RevisionId==Guid.Empty || owner.RevisionId!=selection.OwnerRevisionId)
            throw new ApiError(422,"OWNER_REVISION_UNKNOWN","对象或修订不在所选草稿中；旧课时/资源请先保存具有独立修订的草稿。");
        return owner;
    }
    public static string SourceRef(Guid draftId,MappingOwner owner)=>$"draft:{draftId}/{owner.OwnerType}:{owner.RevisionId}";
    public static (MappingProposal Proposal,Match[] Matches,string[] Flags) Suggest(MappingOwner owner,KC[] library,string sourceRef)
    {
        var matches=Retrieval.TopK(owner.Text,library);
        var flags=new List<string>{"MockOnly","HumanReviewRequired"};
        if(matches.Length==0)return(new("NoEvidence",[]),matches,[..flags,"NoLibraryMatch"]);
        SuggestedMappingItem Item(KC k,string role,decimal share,string mode,string? step,int sequence)=>new(k.Id,k.RevisionId,role,1,share,mode,step,sequence,matches.FirstOrDefault(m=>m.KCId==k.Id)?.Score,[sourceRef]);
        if(owner.OwnerType=="Question" && owner.EvidencePolicy=="ObservedSteps")
        {
            flags.Add("IndependentStepReviewRequired");
            var available=owner.Mappings.Where(m=>library.Any(k=>k.Id==m.KCId)).ToArray();
            if(available.Length!=owner.Mappings.Length)flags.Add("OriginalKCOutsideLibrary");
            var items=available.Select((m,i)=>Item(library.Single(k=>k.Id==m.KCId),m.Role,m.Share,m.Mode,m.Step,i+1)).ToArray();
            return(new("ObservedSteps",items),matches,flags.ToArray());
        }
        var best=library.Single(k=>k.Id==matches[0].KCId);
        if(owner.OwnerType=="Question")
        {
            var measurable=owner.EvidencePolicy=="SingleKC";
            return(new(measurable?"SingleKC":"NoEvidence",[Item(best,measurable?"Primary":"Context",measurable?1:0,measurable?"WholeItem":"None",null,1)]),matches,flags.ToArray());
        }
        return(new("NoEvidence",[Item(best,"Primary",0,"None",null,1)]),matches,flags.ToArray());
    }
    public static (MappingProposal Proposal,Match[] Matches,string[] Flags) Manual(Catalog source,MappingOwner owner,KC[] library,string sourceRef)
    {
        // Copy explicit source associations only. No ranking, replacement KC or invented steps.
        var mappings=owner.OwnerType=="Question"?owner.Mappings:
            (owner.OwnerType=="Lesson"?source.Lessons.Single(l=>l.Id==owner.Id).KCIds:source.Resources.Single(r=>r.Id==owner.Id).KCIds)
            .Select(id=>new Mapping(id,"Primary",0,"None",null)).ToArray();
        var flags=new List<string>{"ManualSource","HumanReviewRequired","CoverageNeedsReview"};
        if(mappings.Any(m=>!library.Any(k=>k.Id==m.KCId)))flags.Add("OriginalKCOutsideLibrary");
        var items=mappings.Where(m=>library.Any(k=>k.Id==m.KCId)).Select((m,i)=>new SuggestedMappingItem(
            m.KCId,library.Single(k=>k.Id==m.KCId).RevisionId,m.Role,1,m.Share,m.Mode,m.Step,i+1,null,[sourceRef])).ToArray();
        if(owner.EvidencePolicy=="ObservedSteps")flags.Add("IndependentStepReviewRequired");
        return(new(owner.EvidencePolicy,items),[],flags.ToArray());
    }
    public static string[] Validate(MappingOwner owner,MappingProposal proposal,KC[] library,string sourceRef)
    {
        var errors=new List<string>();var items=proposal.Items;
        if(items==null || items.Length>20 || items.Any(i=>i==null))return["每个对象最多20项映射，需要明确提交有效条目数组。"];
        if(items.Any(i=>i.SourceRefs==null || !i.SourceRefs.SequenceEqual(new[]{sourceRef})))errors.Add("引用必须指向本次冻结的对象修订。");
        if(items.Any(i=>!library.Any(k=>k.Id==i.KCId && k.RevisionId==i.KCRevisionId)))errors.Add("能力及修订必须来自本次冻结的正式能力库。");
        if(items.Any(i=>i.CoverageWeight is <0 or >1 || i.EvidenceShare is <0 or >1 || i.ModelScore is <-1 or >1))errors.Add("覆盖/证据份额必须在0到1，模拟排序分数必须在-1到1。");
        if(items.Any(i=>decimal.Round(i.CoverageWeight,6)!=i.CoverageWeight || decimal.Round(i.EvidenceShare,6)!=i.EvidenceShare))errors.Add("覆盖与证据份额最多六位小数，避免正式映射与快照舍入不一致。");
        if(items.Any(i=>i.Role is not "Primary" and not "Secondary" and not "Prerequisite" and not "Context" || i.EvidenceMode is not "None" and not "WholeItem" and not "StepObserved"))errors.Add("映射角色或证据方式无效。");
        if(!items.Select(i=>i.Sequence).Order().SequenceEqual(Enumerable.Range(1,items.Length)))errors.Add("映射顺序需为不重复的连续正整数。");
        if(items.GroupBy(i=>new{i.KCId,i.EvidenceMode,i.Step}).Any(g=>g.Count()>1))errors.Add("同一动作不能重复映射同一能力。");
        if(items.Any(i=>(i.Role is "Prerequisite" or "Context") && (i.EvidenceMode!="None" || i.EvidenceShare!=0)))errors.Add("前置和上下文不能测量。");
        if(items.Any(i=>i.EvidenceMode=="None" && (i.EvidenceShare!=0 || i.Step!=null) || i.EvidenceMode!="None" && i.EvidenceShare<=0))errors.Add("不测量时证据份额为零；可测映射需正份额。");
        if(owner.OwnerType!="Question")
        {
            if(proposal.EvidencePolicy!="NoEvidence" || items.Length==0 || items.Any(i=>i.EvidenceMode!="None" || i.EvidenceShare!=0 || i.Role!="Primary" || i.CoverageWeight<=0))errors.Add("课时/资源至少关联一个覆盖能力，只表示教学覆盖，不能产生作答证据。");
            return errors.ToArray();
        }
        if(proposal.EvidencePolicy is not "SingleKC" and not "NoEvidence" and not "ObservedSteps")errors.Add("题目归因策略无效。");
        if(items.Where(i=>i.EvidenceMode!="None").Sum(i=>i.EvidenceShare)>1)errors.Add("题目测量份额总和不能超过1。");
        if(proposal.EvidencePolicy=="NoEvidence" && items.Any(i=>i.EvidenceMode!="None"))errors.Add("不计证据题不能含测量映射。");
        if(proposal.EvidencePolicy=="SingleKC" && (items.Count(i=>i.EvidenceMode=="WholeItem")!=1 || items.Any(i=>i.EvidenceMode is not "None" and not "WholeItem" || i.Step!=null)))errors.Add("整题测量仅允许一个明确的能力，不推断其他能力。");
        if(proposal.EvidencePolicy=="ObservedSteps" && (owner.QuestionType is not "ShortAnswer" and not "MultiStep" || !items.Any(i=>i.EvidenceMode=="StepObserved") || items.Any(i=>i.EvidenceMode!="None" && (i.EvidenceMode!="StepObserved" || string.IsNullOrWhiteSpace(i.Step) || i.Step.Length>100))))errors.Add("分步测量需可人工判分的题型和明确观察点。");
        return errors.ToArray();
    }
    public static Catalog Apply(Catalog source,MappingOwner owner,MappingProposal proposal,Guid revision,KC[] library)
    {
        var kcs=source.Kcs.ToList();
        foreach(var id in proposal.Items.Select(i=>i.KCId).Distinct())
        {
            var kc=library.Single(k=>k.Id==id);var existing=kcs.SingleOrDefault(k=>k.Id==id);
            if(existing!=null)
            {
                if(Json.Write(existing with{RevisionId=kc.RevisionId})!=Json.Write(kc))throw new ApiError(422,"LIBRARY_REVISION_CONFLICT","草稿中的能力与冻结正式库定义不同，请先核对能力版本后重新准备建议。");
                // A saved draft may have minted an unpublished revision of the unchanged definition.
                // The generated mapping draft pins that definition to the explicit frozen library revision.
                kcs[kcs.FindIndex(k=>k.Id==id)]=kc;
            }
            if(existing==null)
            {
                if(kcs.Any(k=>k.Code==kc.Code))throw new ApiError(422,"IDENTITY_CODE_CONFLICT","草稿能力编码与正式库冲突，请先核对稳定身份。");
                kcs.Add(kc);
            }
        }
        var ids=proposal.Items.OrderBy(i=>i.Sequence).Select(i=>i.KCId).Distinct().ToArray();
        var mappings=proposal.Items.OrderBy(i=>i.Sequence).Select(i=>new Mapping(i.KCId,i.Role,i.EvidenceShare,i.EvidenceMode,i.Step)).ToArray();
        return source with
        {
            Kcs=kcs.ToArray(),
            MappingCoverage=(source.MappingCoverage??[]).Where(r=>r.OwnerType!=owner.OwnerType || r.OwnerId!=owner.Id).Concat(proposal.Items.Select(i=>new MappingCoverage(owner.OwnerType,owner.Id,i.KCId,i.Role,i.EvidenceMode,i.Step,i.EvidenceShare,i.CoverageWeight))).ToArray(),
            Questions=source.Questions.Select(q=>owner.OwnerType=="Question" && q.Id==owner.Id?q with{RevisionId=revision,Policy=proposal.EvidencePolicy,Mappings=mappings}:q).ToArray(),
            Lessons=source.Lessons.Select(l=>owner.OwnerType=="Lesson" && l.Id==owner.Id?l with{RevisionId=revision,KCIds=ids}:l).ToArray(),
            Resources=source.Resources.Select(r=>owner.OwnerType=="Resource" && r.Id==owner.Id?r with{RevisionId=revision,KCIds=ids}:r).ToArray()
        };
    }
}
