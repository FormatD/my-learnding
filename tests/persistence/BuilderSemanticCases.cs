using Learning;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
public static class BuilderSemanticCases
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action,string code){try{action();throw new Exception("Invalid semantic output accepted: "+code);}catch(ApiError e){Check(e.Code==code,"Wrong rejection "+e.Code+", expected "+code);}}
    public static async Task Run(bool live=false)
    {
        var fragment=new BuilderFragment(Guid.NewGuid(),"明确观察：独立计算20除以6的商与余数，余数须小于除数。名称相近不代表同一能力。");
        var candidate=new BuilderCandidateOutput("有余数除法","MATH","Procedure",2,2,"独立计算整数除法的商与余数","余数小于除数，不涉及应用题建模",[fragment.Id],["独立计算20除以6的商与余数"],.99m);
        var kc=new KC(Guid.NewGuid(),Guid.NewGuid(),"SEM1","求整数除法商余数",candidate.MeasurableBehavior,candidate.Boundary,Subject:"MATH",GradeMin:3,GradeMax:3);
        var fixture=Content.Fixture() with{Kcs=[kc],Questions=[],Lessons=[],Resources=[]};var match=new Match(kc.Id,kc.Name,kc.Behavior,kc.Boundary,.999m,Retrieval.Space);
        var input=BuilderSemanticProtocol.Capture(Guid.NewGuid(),candidate,[fragment],fixture,[match]);
        var user=BuilderSemanticProtocol.UserFor(input);Check(user.Contains("有余数除法") && !user.Contains("modelScore") && !user.Contains("0.999") && !user.Contains("suggestedAction"),"Native definition missing or score/prior decision oracle leaked");
        var linked=new BuilderSemanticResult(BuilderSemanticProtocol.Version,input.CandidateId,"LinkExisting",kc.Id,kc.RevisionId,"两者可测行为与边界一致，跨年级不妨碍同一能力关联，仍待人工核对。",[new(fragment.Id,candidate.SupportingQuotes[0])],[kc.Behavior]);
        if(live){await Live(input);return;}
        var legacy=JsonNode.Parse(Json.Write(BuilderSemanticProtocol.ResponseFormat(input,BuilderSemanticProtocol.InitialPromptHash)))!;Check(legacy["json_schema"]!["schema"]!["required"]!.AsArray().Count==8 && legacy["json_schema"]!["schema"]!["anyOf"]!.AsArray().Count==2,"Original rejected grammar recipe not reproducible");
        var scoped=JsonNode.Parse(Json.Write(BuilderSemanticProtocol.ResponseFormat(input)))!["json_schema"]!["schema"]!["anyOf"]!.AsArray();Check(scoped.Count==4 && scoped.All(b=>b!["required"]!.AsArray().Count==8 && !b["additionalProperties"]!.GetValue<bool>() && b["properties"]!["candidateId"]!["const"]!.GetValue<string>()==input.CandidateId.ToString("D")),"Full scoped branch lost required fields");
        var output=Json.Write(linked);Check(BuilderSemanticProtocol.Validate(output,input).KCId==kc.Id,"Valid cross-grade link rejected");
        foreach(var decision in new[]{"CreateDraft","Reject","NeedsReview"})Check(BuilderSemanticProtocol.Validate(Json.Write(linked with{Decision=decision,KCId=null,KCRevisionId=null,DefinitionQuotes=[]}),input).Decision==decision,"Four semantic decisions unavailable");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{CandidateId=Guid.NewGuid()}),input),"BUILDER_SEMANTIC_SCHEMA_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{KCRevisionId=Guid.NewGuid()}),input),"BUILDER_SEMANTIC_TARGET_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{KCId=Guid.NewGuid()}),input),"BUILDER_SEMANTIC_TARGET_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{DefinitionQuotes=["模型补充而不存在的边界"]}),input),"BUILDER_SEMANTIC_TARGET_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{CandidateQuotes=[new(Guid.NewGuid(),candidate.SupportingQuotes[0])]}),input),"BUILDER_SEMANTIC_SOURCE_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{CandidateQuotes=[new(fragment.Id,"改写的引文")]}),input),"BUILDER_SEMANTIC_SOURCE_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{CandidateQuotes=[]}),input),"BUILDER_SEMANTIC_SOURCE_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{Decision="CreateDraft",KCId=null,KCRevisionId=null}),input),"BUILDER_SEMANTIC_TARGET_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{Decision="NeedsReview"}),input),"BUILDER_SEMANTIC_TARGET_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(Json.Write(linked with{Reason="Unsupported English reason"}),input),"BUILDER_SEMANTIC_SCHEMA_INVALID");
        Reject(()=>BuilderSemanticProtocol.Validate(output.Replace("\"decision\":\"LinkExisting\"","\"decision\":\"LinkExisting\",\"decision\":\"LinkExisting\"",StringComparison.Ordinal),input),"BUILDER_SEMANTIC_SCHEMA_INVALID");
        var scored=JsonNode.Parse(output)!;scored["modelScore"]=.99;Reject(()=>BuilderSemanticProtocol.Validate(scored.ToJsonString(),input),"BUILDER_SEMANTIC_SCHEMA_INVALID");
        var nested=JsonNode.Parse(output)!;nested["candidateQuotes"]![0]!["extra"]=true;Reject(()=>BuilderSemanticProtocol.Validate(nested.ToJsonString(),input),"BUILDER_SEMANTIC_SCHEMA_INVALID");
        Reject(()=>BuilderSemanticProtocol.ValidateInput(input with{Fragments=[fragment with{Text=new string('数',24001)}]}),"BUILDER_SEMANTIC_INPUT_LIMIT");
        Reject(()=>BuilderSemanticProtocol.Capture(input.CandidateId,candidate,[fragment],fixture,[match with{Behavior="不同的修订行为"}]),"BUILDER_SEMANTIC_INPUT_INVALID");
        Reject(()=>BuilderSemanticProtocol.ValidateInput(input with{Matches=[input.Matches[0] with{Definition=kc with{Subject="LANGUAGE"}}]}),"BUILDER_SEMANTIC_INPUT_INVALID");
        Reject(()=>BuilderSemanticProtocol.ValidateInput(input with{Matches=[input.Matches[0] with{Definition=kc with{Type="Concept"}}]}),"BUILDER_SEMANTIC_INPUT_INVALID");
        var uncertain=linked with{Decision="NeedsReview",KCId=null,KCRevisionId=null,CandidateQuotes=[],DefinitionQuotes=[]};Check(BuilderSemanticProtocol.Validate(Json.Write(uncertain),input with{Matches=[]}).Decision=="NeedsReview","Empty recall invented link or prevents explicit uncertainty");
        await Transport(input,output);
        Console.WriteLine("PASS four semantic decisions, frozen candidate/paired target revision, exact source/definition quotes, score omission, duplicate/unknown fields rejected, cross-grade kept and wrong subject/type blocked; controlled HTTP preserves actual/unknown usage and truncation without real model calls");
    }
    static async Task Live(BuilderSemanticInput input)
    {
        var configPath=Environment.GetEnvironmentVariable("SEMANTIC_LOCAL_CONFIG")??throw new Exception("Explicit local config required");var key=Environment.GetEnvironmentVariable("SEMANTIC_LOCAL_KEY")??throw new Exception("Explicit local key path required");var path=Environment.GetEnvironmentVariable("SEMANTIC_LOCAL_REPORT")??throw new Exception("Explicit private report required");
        var config=new ConfigurationBuilder().AddJsonFile(configPath).Build();var frozen=new BuilderSemanticLocalConfiguration("semantic-local/1",config["Omlx:Endpoint"]??"http://127.0.0.1:8000/v1",config["Omlx:Model"]??"",config.GetValue<int?>("Omlx:MaxOutputTokens")??4096,config.GetValue<int?>("Omlx:TimeoutMilliseconds")??600000,BuilderSemanticProtocol.PromptHash);frozen.Validate();
        const string scope="Actual local model on explicitly constructed mathematical workflow fixture; not extracted textbook candidate, formal published library, human review, independent gold, or semantic accuracy evaluation";
        var watch=System.Diagnostics.Stopwatch.StartNew();
        async Task Save(object value){Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+".tmp";var options=new FileStreamOptions{Mode=FileMode.Create,Access=FileAccess.Write,Share=FileShare.None};if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;await using(var stream=new FileStream(temp,options)){using var writer=new StreamWriter(stream);await writer.WriteAsync(Json.Write(value));}File.Move(temp,path,true);}
        await Save(new{scope,frozen,input,status="Started",startedAt=DateTimeOffset.UtcNow});BuilderProviderResponse response;
        try{response=await new LocalBuilderSemanticProvider(frozen,key).Generate(input,CancellationToken.None);}
        catch(Exception e){await Save(new{scope,frozen,input,status="NoConfirmedResponse",error=e is ApiError api?api.Code:e is OperationCanceledException?"LOCAL_PROVIDER_CANCELLED_OR_TIMEOUT":"LOCAL_PROVIDER_FAILED",elapsedMilliseconds=watch.ElapsedMilliseconds});throw;}
        BuilderSemanticResult? result=null;string? error=null;try{if(!response.OutputComplete)throw new ApiError(422,"LOCAL_PROVIDER_OUTPUT_INCOMPLETE","Truncated physical response");result=BuilderSemanticProtocol.Validate(response.Output,input);}catch(ApiError e){error=e.Code;}
        await Save(new{scope,frozen,input,status="Returned",response,result,error,elapsedMilliseconds=watch.ElapsedMilliseconds});Console.WriteLine(Json.Write(new{status=error??"ProtocolValidated",decision=result?.Decision,response.Usage,elapsedMilliseconds=watch.ElapsedMilliseconds}));if(error!=null)throw new Exception("Actual response retained; semantic protocol rejected: "+error);
    }
    static async Task Transport(BuilderSemanticInput input,string output)
    {
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        var key=Path.Combine(Path.GetTempPath(),"learning-semantic-controlled-key-"+Guid.NewGuid());await File.WriteAllTextAsync(key,"test-only-key");if(!OperatingSystem.IsWindows())File.SetUnixFileMode(key,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        var frozen=new BuilderSemanticLocalConfiguration("semantic-local/1",$"http://127.0.0.1:{port}/v1","controlled-model",1024,10000,BuilderSemanticProtocol.PromptHash);
        try
        {
            async Task<BuilderProviderResponse> Call(string model,string finish)
            {
                var pending=new LocalBuilderSemanticProvider(frozen,key).Generate(input,CancellationToken.None);var context=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));using var reader=new StreamReader(context.Request.InputStream);var sent=JsonNode.Parse(await reader.ReadToEndAsync())!;
                Check(sent["model"]!.GetValue<string>()==frozen.Model && sent["max_tokens"]!.GetValue<int>()==1024 && sent["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>() && context.Request.Url!.AbsolutePath=="/v1/chat/completions","Frozen model/schema/limits lost");
                var schema=sent["response_format"]!["json_schema"]!["schema"]!;Check(schema["anyOf"]![0]!["properties"]!["candidateId"]!["const"]!.GetValue<string>()==input.CandidateId.ToString("D"),"Candidate identity not constrained");
                var targets=schema["anyOf"]!.AsArray();Check(targets.Count==input.Matches.Length+3 && targets[0]!["properties"]!["kcId"]!["const"]!.GetValue<string>()==input.Matches[0].Definition.Id.ToString("D") && targets[0]!["properties"]!["kcRevisionId"]!["const"]!.GetValue<string>()==input.Matches[0].Definition.RevisionId.ToString("D"),"Paired frozen target schema lost");
                var user=sent["messages"]![1]!["content"]!.GetValue<string>();Check(user.Contains("可测") || user.Contains("Measurable") || user.Contains("measurableBehavior"),"Definition omitted");Check(Json.Read<BuilderSemanticInput>(user).Candidate==input.Candidate && !user.Contains("modelScore"),"Native user definition changed or score leaked");
                var body=Json.Write(new{model,choices=new[]{new{message=new{content=output},finish_reason=finish}},usage=new{completion_tokens=1024}});var bytes=Encoding.UTF8.GetBytes(body);context.Response.ContentLength64=bytes.Length;await context.Response.OutputStream.WriteAsync(bytes);context.Response.Close();return await pending;
            }
            var truncated=await Call(frozen.Model,"length");Check(!truncated.OutputComplete && truncated.Output==output && truncated.Usage is {InputTokens:null,OutputTokens:1024},"Truncated real facts lost or missing usage fabricated");
            var complete=await Call(frozen.Model,"stop");Check(complete.OutputComplete && BuilderSemanticProtocol.Validate(complete.Output,input).Decision=="LinkExisting","Complete response rejected");
            try{await Call("different-model","stop");throw new Exception("Different model accepted");}catch(ApiError e){Check(e.Code=="LOCAL_PROVIDER_MODEL_MISMATCH","Wrong mismatch error");}
            Reject(()=>(frozen with{Endpoint="https://example.com/v1"}).Validate(),"BUILDER_SEMANTIC_CONFIGURATION_INVALID");Reject(()=>(frozen with{PromptHash="changed"}).Validate(),"BUILDER_SEMANTIC_CONFIGURATION_INVALID");
            var waiting=new LocalBuilderSemanticProvider(frozen with{TimeoutMilliseconds=100},key).Generate(input,CancellationToken.None);var held=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));try{await waiting;throw new Exception("Unknown response invented");}catch(OperationCanceledException){}finally{held.Response.Close();}
            if(!OperatingSystem.IsWindows()){File.SetUnixFileMode(key,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead);try{await new LocalBuilderSemanticProvider(frozen,key).Generate(input,CancellationToken.None);throw new Exception("Nonprivate key accepted");}catch(ApiError e){Check(e.Code=="LOCAL_PROVIDER_KEY_NOT_PRIVATE","Wrong private key gate");}}
        }
        finally{File.Delete(key);}
    }
}
