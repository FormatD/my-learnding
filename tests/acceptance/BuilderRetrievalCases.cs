using Learning;
using Microsoft.Extensions.Configuration;
public static class BuilderRetrievalCases
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run()
    {
        var configuration=BuilderConfiguration.Current(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Builder:RetrievalTopK","2"}}).Build());var fixedSettings=configuration.Retrieval!;
        var id1=Guid.Parse("00000000-0000-0000-0000-000000000001");var id2=Guid.Parse("00000000-0000-0000-0000-000000000002");
        var a=new KC(id1,Guid.NewGuid(),"MATH.G3.A","计算顺序","独立计算","不含建模","Procedure");var b=a with{Id=id2,RevisionId=Guid.NewGuid()};var unrelated=a with{Id=Guid.NewGuid(),Name="应用建模",Type="Application"};
        var candidate=new BuilderCandidateOutput("应用建模","MATH","Procedure",1,12,"独立计算","不含建模",[Guid.NewGuid()],["真实片段"],0);
        var found=Retrieval.Candidates(candidate,[unrelated,b,a],fixedSettings);Check(found.Select(m=>m.KCId).SequenceEqual(new[]{id1,id2}),"incompatible type or unstable equal-score order");Check(found.All(m=>m.EmbeddingSpace==Retrieval.Space),"wrong frozen space");Check(Retrieval.Candidates(candidate,[unrelated],fixedSettings).Length==0,"empty same-type library invented a match");
        var payload=Json.Write(configuration);var run=new BuilderRun{InputVersion="builder-input/4",ModelConfigPayload=payload,ModelConfigHash=Content.Hash(payload)};var settings=BuilderConfiguration.ResolveRetrieval(run)!;Check(settings.TopK==2 && Retrieval.Candidates(candidate,[a,b],settings).Length==2,"saved retrieval setting not used");
        var changed=BuilderConfiguration.Current(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Builder:RetrievalTopK","1"}}).Build());Check(Json.Write(changed)!=payload && BuilderConfiguration.ResolveRetrieval(run)!.TopK==2,"current setting replaced old run");
        var legacy=new BuilderModelConfiguration("builder-config/1","Mock","fixture/1","kc-candidate/1",Content.Hash(BuilderProtocol.Schema),new BuilderLimits());var original=Json.Write(legacy);Check(!original.Contains("retrieval"),"legacy serialization invented retrieval fields");run.InputVersion="builder-input/3";run.ModelConfigPayload=original;run.ModelConfigHash=Content.Hash(original);Check(BuilderConfiguration.ResolveRetrieval(run)==null && run.ModelConfigPayload==original,"legacy frozen config reinterpreted or changed");
        run.InputVersion="builder-input/4";try{BuilderConfiguration.ResolveRetrieval(run);throw new Exception("modern run accepted old config");}catch(ApiError e){Check(e.Code=="RUN_CONFIGURATION_UNKNOWN","wrong invalid config response");}
        foreach(var count in new[]{0,51}){try{(fixedSettings with{TopK=count}).Validate();throw new Exception("invalid limit accepted");}catch(ApiError e){Check(e.Code=="BUILDER_CONFIGURATION_INVALID","wrong limit response");}}
    }
}
