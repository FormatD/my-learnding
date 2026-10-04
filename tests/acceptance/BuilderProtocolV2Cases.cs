using Learning;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
public static class BuilderProtocolV2Cases
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action,string code){try{action();throw new Exception("expected "+code);}catch(ApiError e){Check(e.Code==code,"wrong rejection "+e.Code);}}
    public static void Run()
    {
        var fragment=new BuilderFragment(Guid.NewGuid(),"选择运算顺序并表达思路。");var candidate=new BuilderCandidateOutput("受控类型输出","MATH","Strategy",3,3,"独立选择运算顺序","不含建模",[fragment.Id],[fragment.Text],.5m);
        Check(Content.Hash(BuilderProtocol.Schema)!=Content.Hash(BuilderProtocol.SchemaV2),"schema versions share hash");
        foreach(var type in new[]{"Concept","Procedure","Representation","Strategy","Application","Expression","Misconception"})
        {
            var text=Json.Write(new BuilderEnvelope("kc-candidate/2",[candidate with{KcType=type}]));Check(BuilderProtocol.Validate(text,[fragment],version:"kc-candidate/2").Candidates.Single().KcType==type,"v2 type rejected");Reject(()=>BuilderProtocol.Validate(text,[fragment]),"BUILDER_SCHEMA_INVALID");
        }
        var valid=Json.Write(new BuilderEnvelope("kc-candidate/2",[candidate]));var extra=JsonNode.Parse(valid)!;extra["candidates"]![0]!["unknown"]=true;Reject(()=>BuilderProtocol.Validate(extra.ToJsonString(),[fragment],version:"kc-candidate/2"),"BUILDER_SCHEMA_INVALID");
        Reject(()=>BuilderProtocol.Validate(Json.Write(new BuilderEnvelope("kc-candidate/2",[candidate with{SupportingQuotes=["不存在的引文"]}])),[fragment],version:"kc-candidate/2"),"BUILDER_SOURCE_INVALID");Reject(()=>BuilderProtocol.Validate(valid,[fragment],version:"kc-candidate/3"),"BUILDER_SCHEMA_INVALID");
        var config=BuilderConfiguration.Current(new ConfigurationBuilder().Build());Check(config.Version=="builder-config/3" && config.PromptVersion=="kc-candidate/2" && config.SchemaHash==Content.Hash(BuilderProtocol.SchemaV2),"new configuration not fixed");
        var payload=Json.Write(config);var run=new BuilderRun{InputVersion="builder-input/4",PromptVersion=config.PromptVersion,ModelConfigPayload=payload,ModelConfigHash=Content.Hash(payload)};Check(BuilderConfiguration.Resolve(run)==config.Limits,"new limits not recoverable");
        foreach(var oldVersion in new[]{"builder-config/1","builder-config/2"})
        {
            var old=config with{Version=oldVersion,PromptVersion="kc-candidate/1",SchemaHash=Content.Hash(BuilderProtocol.Schema),Retrieval=oldVersion=="builder-config/1"?null:config.Retrieval};var stored=Json.Write(old);run.PromptVersion=old.PromptVersion;run.InputVersion=oldVersion=="builder-config/1"?"builder-input/3":"builder-input/4";run.ModelConfigPayload=stored;run.ModelConfigHash=Content.Hash(stored);Check(BuilderConfiguration.Resolve(run)==old.Limits && run.ModelConfigPayload==stored,"old configuration replaced");
        }
        run.PromptVersion=config.PromptVersion;run.InputVersion="builder-input/4";run.ModelConfigPayload=payload;run.ModelConfigHash=Content.Hash(payload);var wrong=config with{SchemaHash=Content.Hash(BuilderProtocol.Schema)};run.ModelConfigPayload=Json.Write(wrong);run.ModelConfigHash=Content.Hash(run.ModelConfigPayload);Reject(()=>BuilderConfiguration.Resolve(run),"RUN_CONFIGURATION_UNKNOWN");
        foreach(var version in new[]{"kc-candidate/1","kc-candidate/2"})
        {
            var provider=new MockBuilderCandidateProvider();var result=BuilderProtocol.Run(provider,[fragment],new BuilderLimits(),CancellationToken.None,version).GetAwaiter().GetResult();Check(result.Output.SchemaVersion==version && result.Calls==1,"mock crossed saved schema version");
        }
        var repair=new Controlled("{}",valid);var repaired=BuilderProtocol.Run(repair,[fragment],new BuilderLimits(),CancellationToken.None,"kc-candidate/2").GetAwaiter().GetResult();Check(repaired.Repaired && repair.Requests.Count==2 && repair.Requests.All(r=>r.Schema==BuilderProtocol.SchemaV2) && repair.Requests[1].ValidationCode=="BUILDER_SCHEMA_INVALID","repair switched version");
    }
    sealed class Controlled(params string[] outputs):IBuilderCandidateProvider
    {
        public List<BuilderProviderRequest> Requests{get;}=[];
        public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
        public Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct){Requests.Add(request);return Task.FromResult(new BuilderProviderResponse(outputs[Requests.Count-1]));}
    }
}
