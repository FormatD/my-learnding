using Learning;
using Microsoft.Extensions.Configuration;
public static class BuilderRetrievalCases
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run()
    {
        var configuration=BuilderConfiguration.Current(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Builder:RetrievalTopK","2"}}).Build());var fixedSettings=configuration.Retrieval!;
        var id1=Guid.Parse("00000000-0000-0000-0000-000000000001");var id2=Guid.Parse("00000000-0000-0000-0000-000000000002");
        var a=new KC(id1,Guid.NewGuid(),"MATH.G3.A","计算顺序","独立计算","不含建模","Procedure",Subject:"MATH",GradeMin:3,GradeMax:3);var b=a with{Id=id2,RevisionId=Guid.NewGuid()};var unrelated=a with{Id=Guid.NewGuid(),Name="应用建模",Type="Application"};
        var candidate=new BuilderCandidateOutput("应用建模","MATH","Procedure",1,12,"独立计算","不含建模",[Guid.NewGuid()],["真实片段"],0);
        var found=Retrieval.Candidates(candidate,[unrelated,b,a],fixedSettings);Check(found.Select(m=>m.KCId).SequenceEqual(new[]{id1,id2}),"incompatible type or unstable equal-score order");Check(found.All(m=>m.EmbeddingSpace==Retrieval.Space),"wrong frozen space");Check(Retrieval.Candidates(candidate,[unrelated],fixedSettings).Length==0,"empty same-type library invented a match");
        var alias=new BuilderAliasSnapshot(Guid.NewGuid(),id2,"  应用建模  ","应用建模",Guid.NewGuid(),Guid.NewGuid());
        var aliases=fixedSettings with{Aliases=[alias]};var aliased=Retrieval.Candidates(candidate,[a,b,unrelated],aliases);Check(aliased[0].KCId==id2 && aliased[0].MatchedAliases!.Single()==alias,"approved exact alias not prioritized or source lost");
        Check(Retrieval.Candidates(candidate with{Name="其他名称"},[a,b],aliases).All(m=>m.MatchedAliases!.Length==0),"partial or unrelated name matched alias");
        var oldProfile=fixedSettings with{Version="builder-retrieval/1",SubjectPolicy=null,GradePolicy=null,Aliases=null,KeywordPolicy=null,FusionPolicy=null};Check(!Json.Write(oldProfile).Contains("aliases") && Retrieval.Candidates(candidate,[b,a],oldProfile)[0].KCId==id1,"old retrieval profile gained alias ranking");
        Check(!Json.Write(Retrieval.Candidates(candidate,[a],oldProfile)[0]).Contains("matchedAliases"),"legacy match serialization changed");
        Check(BuilderLexicalRetrieval.Terms(" ＡＢＣ １２ 混合，运算 ").SequenceEqual(new[]{"abc","12","混合","运算"}),"normalization, word boundary or CJK punctuation terms incorrect");Check(BuilderLexicalRetrieval.Terms("𠀀𠀁").Single()=="𠀀𠀁","Unicode scalar pair split");
        var lexicalCandidate=candidate with{Name="混合运算",MeasurableBehavior="",Boundary=""};var lexicalMatches=new[]{new Match(id1,"XYZ","","",1,Retrieval.Space,[]),new Match(id2,"混合运算","","",0,Retrieval.Space,[])};
        var fusion=BuilderLexicalRetrieval.Fuse(lexicalCandidate,lexicalMatches,2);Check(fusion[0].KCId==id2 && fusion[0].Score==0 && fusion[0].RetrievalEvidence!.MatchedTerms.SequenceEqual(new[]{"合运","混合","运算"}) && fusion[0].RetrievalEvidence!.KeywordRank==1 && fusion[0].RetrievalEvidence!.VectorRank==2,"lexical recall, original score or fusion evidence incorrect");
        Check(fusion[0].RetrievalEvidence!.FusionScore==1m/61+1m/62 && fusion[1].RetrievalEvidence!.KeywordRank==null && fusion[1].RetrievalEvidence!.KeywordScore==0,"fusion manufactured keyword hit or probability");
        Check(Math.Abs((double)fusion[0].RetrievalEvidence!.KeywordScore-3*Math.Log(2)*2.2/2.65)<1e-12,"BM25 frequency/length/positive IDF calculation incorrect");
        var none=BuilderLexicalRetrieval.Fuse(lexicalCandidate with{Name="无关"},lexicalMatches,1);Check(none.Single().KCId==id1 && none[0].RetrievalEvidence!.MatchedTerms.Length==0,"no-hit query failed vector fallback");
        var aliasOnly=lexicalMatches[0] with{Score=-1,MatchedAliases=[alias with{KCId=id1}]};Check(BuilderLexicalRetrieval.Fuse(lexicalCandidate,[aliasOnly,lexicalMatches[1]],1)[0].KCId==id1,"alias outside both top lists was lost");
        var profileTwo=fixedSettings with{Version="builder-retrieval/2",SubjectPolicy=null,GradePolicy=null,KeywordPolicy=null,FusionPolicy=null};Check(Retrieval.Candidates(candidate,[a,b],profileTwo).All(m=>m.RetrievalEvidence==null) && !Json.Write(profileTwo).Contains("keywordPolicy"),"old alias profile gained lexical evidence");
        var payload=Json.Write(configuration);var run=new BuilderRun{InputVersion="builder-input/4",ModelConfigPayload=payload,ModelConfigHash=Content.Hash(payload)};var settings=BuilderConfiguration.ResolveRetrieval(run)!;Check(settings.TopK==2 && Retrieval.Candidates(candidate,[a,b],settings).Length==2,"saved retrieval setting not used");
        var changed=BuilderConfiguration.Current(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Builder:RetrievalTopK","1"}}).Build());Check(Json.Write(changed)!=payload && BuilderConfiguration.ResolveRetrieval(run)!.TopK==2,"current setting replaced old run");
        var legacy=new BuilderModelConfiguration("builder-config/1","Mock","fixture/1","kc-candidate/1",Content.Hash(BuilderProtocol.Schema),new BuilderLimits());var original=Json.Write(legacy);Check(!original.Contains("retrieval"),"legacy serialization invented retrieval fields");run.InputVersion="builder-input/3";run.ModelConfigPayload=original;run.ModelConfigHash=Content.Hash(original);Check(BuilderConfiguration.ResolveRetrieval(run)==null && run.ModelConfigPayload==original,"legacy frozen config reinterpreted or changed");
        run.InputVersion="builder-input/4";try{BuilderConfiguration.ResolveRetrieval(run);throw new Exception("modern run accepted old config");}catch(ApiError e){Check(e.Code=="RUN_CONFIGURATION_UNKNOWN","wrong invalid config response");}
        foreach(var invalid in new[]{fixedSettings with{KeywordPolicy=null},fixedSettings with{FusionPolicy="unrecorded"},profileTwo with{KeywordPolicy=BuilderLexicalRetrieval.KeywordPolicy}}){try{invalid.Validate();throw new Exception("unknown frozen lexical policy accepted");}catch(ApiError e){Check(e.Code=="BUILDER_CONFIGURATION_INVALID","wrong lexical policy error");}}
        foreach(var count in new[]{0,51}){try{(fixedSettings with{TopK=count}).Validate();throw new Exception("invalid limit accepted");}catch(ApiError e){Check(e.Code=="BUILDER_CONFIGURATION_INVALID","wrong limit response");}}
    }
}
