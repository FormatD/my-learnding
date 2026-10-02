using Learning;

internal static class MappingSuggestionCases
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static MappingOwner Owner(Catalog c,string type,Guid id,Guid revision)=>MappingSuggestions.Owner(c,new(type,id,revision));
    public static void Policy()
    {
        var c=Content.Fixture();var q=c.Questions[0];var owner=Owner(c,"Question",q.Id,q.RevisionId);var sourceRef=MappingSuggestions.SourceRef(Guid.NewGuid(),owner);var kc=c.Kcs[0];
        var item=new SuggestedMappingItem(kc.Id,kc.RevisionId,"Primary",1,1,"WholeItem",null,1,.99m,[sourceRef]);
        Check(MappingSuggestions.Validate(owner,new("SingleKC",[item]),c.Kcs,sourceRef).Length==0,"single measured KC valid");
        var second=item with{KCId=c.Kcs[1].Id,KCRevisionId=c.Kcs[1].RevisionId,Sequence=2};
        Check(MappingSuggestions.Validate(owner,new("SingleKC",[item with{EvidenceShare=.5m},second with{EvidenceShare=.5m}]),c.Kcs,sourceRef).Length>0,"two KC whole-item inference rejected even if total one");
        Check(MappingSuggestions.Validate(owner,new("NoEvidence",[item]),c.Kcs,sourceRef).Length>0,"high model score cannot override NoEvidence");
        Check(MappingSuggestions.Validate(owner,new("SingleKC",[item with{Role="Context"}]),c.Kcs,sourceRef).Length>0,"context never measured");
        Check(MappingSuggestions.Validate(owner,new("SingleKC",[item with{KCRevisionId=Guid.NewGuid()}]),c.Kcs,sourceRef).Length>0,"unfrozen KC revision rejected");
        Check(MappingSuggestions.Validate(owner,new("SingleKC",[item with{SourceRefs=["PDF invented page 3"]}]),c.Kcs,sourceRef).Length>0,"fabricated provenance rejected");
        Check(MappingSuggestions.Validate(owner,new("SingleKC",[item with{CoverageWeight=1.1m}]),c.Kcs,sourceRef).Length>0,"coverage bounded independently");
        Check(MappingSuggestions.Validate(owner,new("SingleKC",[item with{EvidenceShare=.3333333m}]),c.Kcs,sourceRef).Length>0,"normalized evidence not rounded differently from snapshot");
        Check(MappingSuggestions.SameMapping(new("SingleKC",[item]),new("SingleKC",[item with{ModelScore=null,CoverageWeight=1.000000m}])),"sorting metadata and equivalent decimal scale are not mapping corrections");
        Check(!MappingSuggestions.SameMapping(new("SingleKC",[item]),new("SingleKC",[item with{EvidenceShare=.5m}])),"changed evidence budget is a mapping correction");
        Check(MappingSuggestions.Validate(owner,new("SingleKC",[null!]),c.Kcs,sourceRef).Length>0,"null entry rejected");
        foreach(var teaching in new[]{Owner(c,"Lesson",c.Lessons[0].Id,c.Lessons[0].RevisionId!.Value),Owner(c,"Resource",c.Resources[0].Id,c.Resources[0].RevisionId!.Value)})
        {
            var reference=MappingSuggestions.SourceRef(Guid.NewGuid(),teaching);var coverage=item with{EvidenceMode="None",EvidenceShare=0,SourceRefs=[reference],CoverageWeight=.4m};
            Check(MappingSuggestions.Validate(teaching,new("NoEvidence",[coverage]),c.Kcs,reference).Length==0,"teaching coverage not evidence");
            Check(MappingSuggestions.Validate(teaching,new("SingleKC",[item with{SourceRefs=[reference]}]),c.Kcs,reference).Length>0,"lesson/resource cannot produce evidence");
        }
    }
    public static void ObservedSteps()
    {
        var c=MixedOperationsPack.Create();var q=c.Questions.First(q=>q.Policy=="ObservedSteps");var owner=Owner(c,"Question",q.Id,q.RevisionId);var reference=MappingSuggestions.SourceRef(Guid.NewGuid(),owner);
        var mock=MappingSuggestions.Suggest(owner,c.Kcs,reference);
        Check(mock.Flags.Contains("MockOnly") && mock.Flags.Contains("IndependentStepReviewRequired"),"quality limitations explicit");
        Check(mock.Proposal.Items.Select(i=>i.Step).SequenceEqual(q.Mappings.Select(m=>m.Step)),"mock retains original observed actions; no inferred steps");
        Check(mock.Proposal.Items.Select(i=>i.KCId).SequenceEqual(q.Mappings.Select(m=>m.KCId)),"mock retains independent measured KC mapping");
        Check(MappingSuggestions.Validate(owner,mock.Proposal,c.Kcs,reference).Length==0,"original reviewed step structure valid");
        var over=mock.Proposal with{Items=mock.Proposal.Items.Select(i=>i with{EvidenceShare=.8m}).ToArray()};
        Check(MappingSuggestions.Validate(owner,over,c.Kcs,reference).Length>0,"step evidence budget cannot exceed one");
        var numeric=owner with{QuestionType="Numeric"};
        Check(MappingSuggestions.Validate(numeric,mock.Proposal,c.Kcs,reference).Length>0,"mapping cannot invent manual grading ability on numeric question");
        var manual=MappingSuggestions.Manual(c,owner,c.Kcs,reference);
        Check(manual.Matches.Length==0 && manual.Flags.Contains("ManualSource") && !manual.Flags.Contains("MockOnly"),"manual source never invokes ranking or claims model output");
        Check(manual.Proposal.Items.All(i=>i.ModelScore==null) && manual.Proposal.Items.Select(i=>i.Step).SequenceEqual(q.Mappings.Select(m=>m.Step)),"manual source preserves only explicit independent observation steps");
        var outside=MappingSuggestions.Manual(c,owner,[],reference);
        Check(outside.Proposal.Items.Length==0 && outside.Flags.Contains("OriginalKCOutsideLibrary"),"manual source flags missing original KC without substitute inference");
        var set=new MappingSetRevision{OwnerType="Question",OwnerId=q.Id,OwnerRevisionId=q.RevisionId,OwnerDefinitionHash=Content.Hash(Json.Write(q)),EvidencePolicy=q.Policy};
        var fixedItems=manual.Proposal.Items.Select(i=>new MappingSetItem{KCId=i.KCId,KCRevisionId=i.KCRevisionId,Role=i.Role,EvidenceShare=i.EvidenceShare,EvidenceMode=i.EvidenceMode,Step=i.Step,Sequence=i.Sequence}).ToArray();
        var projected=PublishedMappings.Project(c,q,set,fixedItems);
        Check(projected.Mappings.SequenceEqual(q.Mappings),"normalized step projection retains independently observed budgets");
        fixedItems[0].EvidenceShare+=.1m;
        try{PublishedMappings.Project(c,q,set,fixedItems);throw new Exception("a frozen budget cannot silently drift from the released question");}
        catch(ApiError e){Check(e.Code=="MAPPING_SNAPSHOT_UNKNOWN","mismatched source budget rejected");}
        var missing=MappingSuggestions.Suggest(owner,[],reference);
        Check(missing.Flags.Contains("NoLibraryMatch") && missing.Proposal.Items.Length==0,"missing library does not invent a KC");
    }
    public static void FrozenApply()
    {
        var c=Content.Fixture();var library=c.Kcs;var saved=c with{Kcs=c.Kcs.Select(k=>k with{RevisionId=Guid.NewGuid()}).ToArray()};var before=Json.Write(saved);
        var q=saved.Questions[0];var owner=Owner(saved,"Question",q.Id,q.RevisionId);var reference=MappingSuggestions.SourceRef(Guid.NewGuid(),owner);var kc=library[0];var revision=Guid.NewGuid();
        var item=new SuggestedMappingItem(kc.Id,kc.RevisionId,"Primary",1,1,"WholeItem",null,1,null,[reference]);
        var proposal=new MappingProposal("SingleKC",[item]);var after=MappingSuggestions.Apply(saved,owner,proposal,revision,library);
        Check(Json.Write(saved)==before,"source snapshot remains immutable");
        Check(after.Questions[0].Id==q.Id && after.Questions[0].RevisionId==revision && after.Questions[1].RevisionId==saved.Questions[1].RevisionId,"new owner revision; unrelated question unchanged");
        Check(after.Kcs.Single(k=>k.Id==kc.Id).RevisionId==kc.RevisionId,"mapping pins unchanged definition to frozen published KC revision");
        var changed=saved with{Kcs=saved.Kcs.Select(k=>k.Id==kc.Id?k with{Boundary="different draft boundary"}:k).ToArray()};
        try{MappingSuggestions.Apply(changed,owner,proposal,Guid.NewGuid(),library);throw new Exception("changed KC definition must not be silently replaced");}
        catch(ApiError e){Check(e.Code=="LIBRARY_REVISION_CONFLICT","explicit conflicting library definition");}
        try{Owner(saved,"Question",q.Id,Guid.NewGuid());throw new Exception("stale owner revision must be rejected");}
        catch(ApiError e){Check(e.Code=="OWNER_REVISION_UNKNOWN","owner revision pinned");}
        var emptyIdentity=saved with{Questions=[q with{Id=Guid.Empty}]};
        try{Owner(emptyIdentity,"Question",Guid.Empty,q.RevisionId);throw new Exception("empty stable identity must be rejected");}
        catch(ApiError e){Check(e.Code=="OWNER_REVISION_UNKNOWN","owner stable identity required");}
        var legacy=saved with{Resources=saved.Resources.Select(r=>r with{RevisionId=null}).ToArray()};
        try{Owner(legacy,"Resource",legacy.Resources[0].Id,Guid.NewGuid());throw new Exception("legacy resource revision must not be inferred");}
        catch(ApiError e){Check(e.Code=="OWNER_REVISION_UNKNOWN","legacy source version unknown");}
    }
}
