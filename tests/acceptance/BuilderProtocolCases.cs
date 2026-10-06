using Learning;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;

public static class BuilderProtocolCases
{
    static BuilderFragment[] Fragments()=>[new(Guid.NewGuid(),"先乘除后加减，含括号时先算括号内。")];
    static string Valid(BuilderFragment[] fragments)=>new MockBuilderCandidateProvider().Generate(new(fragments,BuilderProtocol.Schema),CancellationToken.None).GetAwaiter().GetResult().Output;
    static void Assert(bool value){if(!value)throw new Exception("Builder protocol invariant failed");}
    static void Reject(Action action,string code){try{action();throw new Exception("expected rejection "+code);}catch(ApiError ex){Assert(ex.Code==code);}}
    public static void Validation()
    {
        var f=Fragments();var valid=Valid(f);Assert(BuilderProtocol.Validate(valid,f).Candidates.Single().SupportingQuotes.Single()==f[0].Text);
        foreach(var change in new Action<JsonObject>[] {
            x=>x["extra"]=true,x=>x["gradeMin"]=13,x=>x["subject"]="SCIENCE",x=>x["modelScore"]=1.1m,x=>x["kcType"]="Other",x=>x.Remove("boundary"),x=>x["name"]=" ",x=>x["gradeMax"]="3",x=>x["measurableBehavior"]=new string('a',4001)})
        {
            var root=JsonNode.Parse(valid)!.AsObject();change(root["candidates"]![0]!.AsObject());Reject(()=>BuilderProtocol.Validate(root.ToJsonString(),f),"BUILDER_SCHEMA_INVALID");
        }
        foreach(var change in new Action<JsonObject>[] {
            x=>x["sourceChunkIds"]=new JsonArray(Guid.NewGuid().ToString()),x=>x["sourceChunkIds"]=new JsonArray("chunk-01"),x=>x["supportingQuotes"]=new JsonArray("来源中不存在的引用"),x=>{x["sourceChunkIds"]=new JsonArray(f[0].Id.ToString(),f[0].Id.ToString());x["supportingQuotes"]=new JsonArray(f[0].Text,f[0].Text);},x=>x["supportingQuotes"]=new JsonArray(f[0].Text,f[0].Text)})
        {
            var root=JsonNode.Parse(valid)!.AsObject();change(root["candidates"]![0]!.AsObject());Reject(()=>BuilderProtocol.Validate(root.ToJsonString(),f),"BUILDER_SOURCE_INVALID");
        }
        Reject(()=>BuilderProtocol.Validate(valid.Replace("\"schemaVersion\":","\"schemaVersion\":\"kc-candidate/1\",\"schemaVersion\":"),f),"BUILDER_SCHEMA_INVALID");
        Reject(()=>BuilderProtocol.Validate("```json\n"+valid+"\n```",f),"BUILDER_SCHEMA_INVALID");
        Reject(()=>BuilderProtocol.Validate(new string('x',BuilderProtocol.MaxOutputCharacters+1),f),"BUILDER_OUTPUT_LIMIT");
        var unicode=new[]{new BuilderFragment(Guid.NewGuid(),new string('中',79)+"😀后续")};Assert(BuilderProtocol.Validate(Valid(unicode),unicode).Candidates.Single().SupportingQuotes.Single()==new string('中',79));
        var empty=JsonNode.Parse(valid)!.AsObject();empty["candidates"]=new JsonArray();Assert(BuilderProtocol.Validate(empty.ToJsonString(),f).Candidates.Length==0);
    }
    public static void Control()
    {
        var f=Fragments();var valid=Valid(f);var provider=new Fake("{}",valid);var result=BuilderProtocol.Run(provider,f,TimeSpan.FromSeconds(1),CancellationToken.None).GetAwaiter().GetResult();Assert(result.Calls==2 && result.Repaired && provider.Requests[1].InvalidOutput=="{}" && provider.Requests[1].ValidationCode=="BUILDER_SCHEMA_INVALID");
        var partial=new Incomplete(valid);Reject(()=>BuilderProtocol.Run(partial,f,TimeSpan.FromSeconds(1),CancellationToken.None).GetAwaiter().GetResult(),"LOCAL_PROVIDER_OUTPUT_INCOMPLETE");Assert(partial.Calls==1 && partial.Response.Usage!.OutputTokens==1536);
        provider=new Fake("{}","{}",valid);Reject(()=>BuilderProtocol.Run(provider,f,TimeSpan.FromSeconds(1),CancellationToken.None).GetAwaiter().GetResult(),"BUILDER_NEEDS_REPAIR");Assert(provider.Requests.Count==2);
        provider=new Fake(new string('x',BuilderProtocol.MaxOutputCharacters+1),valid);Reject(()=>BuilderProtocol.Run(provider,f,TimeSpan.FromSeconds(1),CancellationToken.None).GetAwaiter().GetResult(),"BUILDER_OUTPUT_LIMIT");Assert(provider.Requests.Count==1);
        var slow=new Slow();Reject(()=>BuilderProtocol.Run(slow,f,TimeSpan.FromMilliseconds(20),CancellationToken.None).GetAwaiter().GetResult(),"BUILDER_TIMEOUT");Assert(slow.Calls==1);
        using var canceled=new CancellationTokenSource();canceled.Cancel();try{BuilderProtocol.Run(slow,f,TimeSpan.FromSeconds(1),canceled.Token).GetAwaiter().GetResult();throw new Exception("cancellation swallowed");}catch(OperationCanceledException){}
        provider=new Fake(valid);Reject(()=>BuilderProtocol.Run(provider,[new(f[0].Id,new string('a',BuilderProtocol.MaxInputCharacters+1))],TimeSpan.FromSeconds(1),CancellationToken.None).GetAwaiter().GetResult(),"BUILDER_INPUT_LIMIT");Assert(provider.Requests.Count==0);
        Reject(()=>BuilderProtocol.ValidateInput(Enumerable.Range(0,101).Select(_=>new BuilderFragment(Guid.NewGuid(),"片段")).ToArray()),"BUILDER_INPUT_LIMIT");Reject(()=>BuilderProtocol.ValidateInput([f[0],f[0]]),"BUILDER_INPUT_LIMIT");
    }
    public static void Configuration()
    {
        var original=BuilderConfiguration.Current(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Builder:MaxFragments","1"},{"Builder:RepairAttempts","0"}}).Build());
        var changed=BuilderConfiguration.Current(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Builder:MaxFragments","2"}}).Build());
        Assert(Json.Write(original)!=Json.Write(changed));var payload=Json.Write(original);var run=new BuilderRun{PromptVersion=original.PromptVersion,InputVersion="builder-input/4",ModelConfigPayload=payload,ModelConfigHash=Content.Hash(payload)};var limits=BuilderConfiguration.Resolve(run);Assert(limits.MaxFragments==1 && limits.RepairAttempts==0);
        var fragments=Fragments();var provider=new Fake("{}",Valid(fragments));Reject(()=>BuilderProtocol.Run(provider,fragments,limits,CancellationToken.None).GetAwaiter().GetResult(),"BUILDER_NEEDS_REPAIR");Assert(provider.Requests.Count==1);
        Reject(()=>BuilderProtocol.Run(new Fake(Valid(fragments)),[fragments[0],new(Guid.NewGuid(),"第二片段")],limits,CancellationToken.None).GetAwaiter().GetResult(),"BUILDER_INPUT_LIMIT");
        run.ModelConfigPayload=Json.Write(changed);Reject(()=>BuilderConfiguration.Resolve(run),"RUN_CONFIGURATION_UNKNOWN");run.ModelConfigHash=Content.Hash(run.ModelConfigPayload);Assert(BuilderConfiguration.Resolve(run).MaxFragments==2);
        run.Model="different";Reject(()=>BuilderConfiguration.Resolve(run),"RUN_CONFIGURATION_UNKNOWN");run.Model="fixture/1";run.ModelConfigPayload=null;Reject(()=>BuilderConfiguration.Resolve(run),"RUN_CONFIGURATION_UNKNOWN");run.ModelConfigHash=null;Reject(()=>BuilderConfiguration.Resolve(run),"RUN_CONFIGURATION_UNKNOWN");run.PromptVersion="kc-candidate/1";run.InputVersion="builder-input/2";Assert(BuilderConfiguration.Resolve(run).MaxFragments==100 && run.ModelConfigPayload==null);
        run.ModelConfigPayload=payload.Replace("\"schemaHash\":", "\"unknown\":0,\"schemaHash\":");run.ModelConfigHash=Content.Hash(run.ModelConfigPayload);Reject(()=>BuilderConfiguration.Resolve(run),"RUN_CONFIGURATION_UNKNOWN");
        Reject(()=>new BuilderLimits(RepairAttempts:2).Validate(),"BUILDER_CONFIGURATION_INVALID");Reject(()=>new BuilderLimits(TimeoutMilliseconds:600001).Validate(),"BUILDER_CONFIGURATION_INVALID");
    }
    sealed class Fake(params string[] outputs):IBuilderCandidateProvider
    {
        public List<BuilderProviderRequest> Requests {get;}=[];
        public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
        public Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct){ct.ThrowIfCancellationRequested();Requests.Add(request);return Task.FromResult(new BuilderProviderResponse(outputs[Requests.Count-1]));}
    }
    sealed class Slow:IBuilderCandidateProvider
    {
        public int Calls;
        public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
        public async Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct){Calls++;await Task.Delay(Timeout.Infinite,ct);return new("{}");}
    }
    sealed class Incomplete(string valid):IBuilderCandidateProvider
    {
        public int Calls;
        public BuilderProviderResponse Response=new(valid,new(100,1536,0,null,"LocalMeasured"),false);
        public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
        public Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct){Calls++;return Task.FromResult(Response);}
    }
}
