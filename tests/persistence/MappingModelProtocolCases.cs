using Learning;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Text;
public static class MappingModelProtocolCases
{
 static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
 public static async Task Run(string mode)
 {
  if(mode=="mapping-model-live")
  {
   var configPath=Environment.GetEnvironmentVariable("MAPPING_LOCAL_CONFIG")??throw new Exception("Explicit local config required");var packPath=Environment.GetEnvironmentVariable("MAPPING_LOCAL_PACK")??throw new Exception("Explicit original practice pack required");var reportPath=Environment.GetEnvironmentVariable("MAPPING_LOCAL_REPORT")??throw new Exception("Explicit private report required");
   var configuration=new ConfigurationBuilder().AddJsonFile(configPath).Build();var config=MappingLocalConfiguration.Current(configuration);config.Validate();var pack=JsonNode.Parse(await File.ReadAllTextAsync(packPath))!;var source=Json.Read<Catalog>(pack["catalog"]!.ToJsonString());
   var q=source.Questions.First(q=>q.Policy=="SingleKC");var l=source.Lessons[0];var r=source.Resources[0];var input=MappingModelProtocol.Capture(source,Guid.NewGuid(),[new("Question",q.Id,q.RevisionId),new("Lesson",l.Id,l.RevisionId!.Value),new("Resource",r.Id,r.RevisionId!.Value)],source.Kcs);
   var key=Environment.GetEnvironmentVariable("MAPPING_LOCAL_KEY")??throw new Exception("Explicit key path required");var watch=System.Diagnostics.Stopwatch.StartNew();
   async Task Save(object value){Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);var pending=reportPath+".tmp";await File.WriteAllTextAsync(pending,Json.Write(value));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(pending,UnixFileMode.UserRead|UnixFileMode.UserWrite);File.Move(pending,reportPath,true);}
   const string scope="Isolated original draft protocol diagnostic; not published library, human gold, or saved household mapping";
   await Save(new{scope,config,input,status="Started",startedAt=DateTimeOffset.UtcNow,response=(BuilderProviderResponse?)null});
   BuilderProviderResponse response;
   try{response=await new LocalMappingModelProvider(config,key).Generate(input,CancellationToken.None);}
   catch(Exception e){await Save(new{scope,config,input,status="NoConfirmedResponse",error=e is ApiError api?api.Code:e is OperationCanceledException?"LOCAL_PROVIDER_CANCELLED_OR_TIMEOUT":"LOCAL_PROVIDER_FAILED",response=(BuilderProviderResponse?)null,elapsedMilliseconds=watch.ElapsedMilliseconds});throw;}
   MappingModelEnvelope? result=null;string? error=null;
   try{if(!response.OutputComplete)throw new ApiError(422,"LOCAL_PROVIDER_OUTPUT_INCOMPLETE","截断响应");result=MappingModelProtocol.Validate(response.Output,input);}catch(ApiError e){error=e.Code;}
   await Save(new{scope,config,input,status="Returned",response,result,error,elapsedMilliseconds=watch.ElapsedMilliseconds});
   Console.WriteLine(Json.Write(new{status=error??"ProtocolValidated",owners=input.Owners.Length,proposed=result?.Results.Count(x=>x.Decision=="Propose"),needsReview=result?.Results.Count(x=>x.Decision=="NeedsReview"),response.Usage,elapsedMilliseconds=watch.ElapsedMilliseconds}));if(error!=null)throw new Exception("Actual response retained; protocol rejected: "+error);return;
  }
  var catalog=Content.Fixture();var question=catalog.Questions[0];var lesson=catalog.Lessons[0] with{RevisionId=Guid.NewGuid()};var resource=catalog.Resources[0] with{RevisionId=Guid.NewGuid()};catalog=catalog with{Lessons=[lesson],Resources=[resource]};var input2=MappingModelProtocol.Capture(catalog,Guid.NewGuid(),[new("Question",question.Id,question.RevisionId),new("Lesson",lesson.Id,lesson.RevisionId!.Value),new("Resource",resource.Id,resource.RevisionId!.Value)],catalog.Kcs);
  var kc=catalog.Kcs[0];MappingModelResult Result(MappingModelOwner owner)=>new(owner.OwnerType,owner.OwnerId,owner.OwnerRevisionId,"Propose","内容直接展示该能力，仍需人工核对。",[owner.Text],new(owner.OwnerType=="Question"?"SingleKC":"NoEvidence",[new(kc.Id,kc.RevisionId,"Primary",1,owner.OwnerType=="Question"?1:0,owner.OwnerType=="Question"?"WholeItem":"None",null,1,null,[owner.SourceRef])]));
  Check(MappingModelProtocol.PromptFor(MappingModelProtocol.InitialPromptHash)==MappingModelProtocol.InitialPrompt && MappingModelProtocol.PromptHash!=MappingModelProtocol.InitialPromptHash,"Frozen initial prompt cannot be reproduced");
  var legacyFormat=JsonNode.Parse(Json.Write(MappingModelProtocol.ResponseFormat(input2,MappingModelProtocol.CoveragePromptHash)))!;
  Check(legacyFormat["json_schema"]!["schema"]!["properties"]!["results"]!["items"]!["properties"]!["proposal"]!["properties"]!["items"]!["items"]!["properties"]!["sourceRefs"]!["items"]!["enum"]==null,"Original request schema silently changed");
  var referenceFormat=JsonNode.Parse(Json.Write(MappingModelProtocol.ResponseFormat(input2,MappingModelProtocol.ReferencePromptHash)))!;
  Check(referenceFormat["json_schema"]!["schema"]!["properties"]!["results"]!["items"]!["properties"]!["proposal"]!["properties"]!["items"]!["items"]!["properties"]!["sourceRefs"]!["items"]!["enum"]!.AsArray().Count==input2.Owners.Length && MappingModelProtocol.PromptFor(MappingModelProtocol.ReferencePromptHash)==MappingModelProtocol.Prompt,"Original global-reference recipe cannot be reproduced");
  Check(JsonNode.Parse(MappingModelProtocol.Schema)!=null,"Protocol schema JSON invalid");
  var valid=Json.Write(new MappingModelEnvelope(MappingModelProtocol.Version,input2.Owners.Select(Result).ToArray()));Check(MappingModelProtocol.Validate(valid,input2).Results.Length==3,"three-owner mapping missing");
  var user=MappingModelProtocol.UserFor(input2);var sentOwner=Json.Read<MappingModelInput>(user).Owners[0];Check(sentOwner.Text.Contains(question.Stem) && sentOwner.Text.Contains(question.Answer) && sentOwner.Text.Contains(question.Explanation) && !user.Contains("mappings") && !user.Contains("evidencePolicy") && !user.Contains("kcIds"),"mapping oracle leaked or problem content omitted");
  void Reject(Action<JsonNode> change,string code){var value=JsonNode.Parse(valid)!;change(value);try{MappingModelProtocol.Validate(value.ToJsonString(),input2);throw new Exception("Invalid model output accepted");}catch(ApiError e){Check(e.Code==code,"Unexpected rejection "+e.Code);}}
  Reject(n=>n["unexpected"]=true,"MAPPING_MODEL_SCHEMA_INVALID");Reject(n=>n["results"]![0]!["ownerRevisionId"]=Guid.NewGuid().ToString(),"MAPPING_MODEL_SOURCE_INVALID");Reject(n=>n["results"]![0]!["evidenceQuotes"]![0]="不存在的引文。","MAPPING_MODEL_SOURCE_INVALID");Reject(n=>n["results"]![0]!["proposal"]!["items"]![0]!["kcRevisionId"]=Guid.NewGuid().ToString(),"MAPPING_MODEL_MAPPING_INVALID");Reject(n=>n["results"]![0]!["proposal"]!["items"]![0]!["sourceRefs"]![0]="another-draft","MAPPING_MODEL_MAPPING_INVALID");Reject(n=>n["results"]![1]=n["results"]![0]!.DeepClone(),"MAPPING_MODEL_SCHEMA_INVALID");Reject(n=>((JsonArray)n["results"]!).RemoveAt(0),"MAPPING_MODEL_SCHEMA_INVALID");
  Reject(n=>n["results"]![1]!["proposal"]!["items"]![0]!["evidenceShare"]=1,"MAPPING_MODEL_MAPPING_INVALID");Reject(n=>n["results"]![0]!["proposal"]!["items"]![0]!["role"]="Prerequisite","MAPPING_MODEL_MAPPING_INVALID");Reject(n=>n["results"]![0]!["proposal"]!["items"]![0]!["modelScore"]=0.99,"MAPPING_MODEL_SCHEMA_INVALID");Reject(n=>n["results"]![0]!["proposal"]!["items"]![0]!["coverageWeight"]=0.1234567,"MAPPING_MODEL_MAPPING_INVALID");
  Reject(n=>n["results"]![0]!["reason"]="Unverified model reasoning","MAPPING_MODEL_SOURCE_INVALID");
  Reject(n=>n["results"]![0]!["proposal"]!["evidencePolicy"]="NoEvidence","MAPPING_MODEL_MAPPING_INVALID");
  Reject(n=>n["results"]![0]!["decision"]="NeedsReview","MAPPING_MODEL_SCHEMA_INVALID");
  var duplicateField=valid.Replace("\"decision\":\"Propose\"","\"decision\":\"Propose\",\"decision\":\"Propose\"",StringComparison.Ordinal);
  try{MappingModelProtocol.Validate(duplicateField,input2);throw new Exception("Duplicate nested field accepted");}catch(ApiError e){Check(e.Code=="MAPPING_MODEL_SCHEMA_INVALID","Wrong duplicate field rejection");}
  try{MappingModelProtocol.Validate(new string(' ',MappingModelProtocol.MaxOutputCharacters+1),input2);throw new Exception("Oversized output accepted");}catch(ApiError e){Check(e.Code=="MAPPING_MODEL_OUTPUT_LIMIT","Wrong output size rejection");}
  try{MappingModelProtocol.ValidateInput(input2 with{Owners=input2.Owners.Select(o=>o with{Text=new string('数',MappingModelProtocol.MaxInputCharacters)}).ToArray()});throw new Exception("Oversized input accepted");}catch(ApiError e){Check(e.Code=="MAPPING_MODEL_INPUT_LIMIT","Wrong input size rejection");}
  var review=JsonNode.Parse(valid)!;review["results"]![1]!["decision"]="NeedsReview";review["results"]![1]!["proposal"]!["items"]=new JsonArray();Check(MappingModelProtocol.Validate(review.ToJsonString(),input2).Results[1].Decision=="NeedsReview","Explicit uncertainty rejected");
  var multi=question with{Type="MultiStep",Mappings=[new(kc.Id,"Primary",1,"StepObserved","只核对明确观察点")]};catalog=catalog with{Questions=[multi]};var steps=MappingModelProtocol.Capture(catalog,Guid.NewGuid(),[new("Question",multi.Id,multi.RevisionId)],catalog.Kcs);var resultStep=Result(steps.Owners[0]) with{Proposal=new("ObservedSteps",[new(kc.Id,kc.RevisionId,"Primary",1,1,"StepObserved","只核对明确观察点",1,null,[steps.Owners[0].SourceRef])])};var stepJson=Json.Write(new MappingModelEnvelope(MappingModelProtocol.Version,[resultStep]));Check(MappingModelProtocol.Validate(stepJson,steps).Results.Length==1,"Authored observable step rejected");
  var invented=resultStep with{Proposal=resultStep.Proposal with{Items=[resultStep.Proposal.Items[0] with{Step="模型虚构的观察点"}]}};try{MappingModelProtocol.Validate(Json.Write(new MappingModelEnvelope(MappingModelProtocol.Version,[invented])),steps);throw new Exception("Invented observed step accepted");}catch(ApiError e){Check(e.Code=="MAPPING_MODEL_MAPPING_INVALID","Wrong step rejection");}
  if(mode=="mapping-model-transport")await Transport(input2,valid);
  Console.WriteLine("PASS strict three-owner mapping, native Chinese content without prior KC associations; missing/duplicate owners, quote/revision/ref drift, teaching evidence, prerequisites, invented confidence/steps and precision rejected; explicit uncertainty retained");
 }
 static async Task Transport(MappingModelInput input,string valid)
 {
  var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
  var keyPath=Path.Combine(Path.GetTempPath(),"learning-mapping-controlled-key-"+Guid.NewGuid());await File.WriteAllTextAsync(keyPath,"controlled-local-test-only");if(!OperatingSystem.IsWindows())File.SetUnixFileMode(keyPath,UnixFileMode.UserRead|UnixFileMode.UserWrite);
  try
  {
   var frozen=new MappingLocalConfiguration("mapping-local/1",$"http://127.0.0.1:{port}/v1","controlled-frozen-model",1024,10000,MappingModelProtocol.PromptHash);var provider=new LocalMappingModelProvider(frozen,keyPath);
   async Task<BuilderProviderResponse> Call(string model,string finish)
   {
    var response=provider.Generate(input,CancellationToken.None);var request=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));using var reader=new StreamReader(request.Request.InputStream);var body=JsonNode.Parse(await reader.ReadToEndAsync())!;
    Check(request.Request.Url!.AbsolutePath=="/v1/chat/completions" && body["model"]!.GetValue<string>()==frozen.Model && body["max_tokens"]!.GetValue<int>()==1024 && body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>(),"Frozen transport or strict schema lost");
    var branches=body["response_format"]!["json_schema"]!["schema"]!["properties"]!["results"]!["items"]!["anyOf"]!.AsArray();Check(branches.Count==input.Owners.Length,"Scoped owner schema missing");
    foreach(var captured in input.Owners)
    {
     var branch=branches.Single(b=>b!["properties"]!["ownerId"]!["const"]!.GetValue<string>()==captured.OwnerId.ToString("D"))!;var props=branch["properties"]!;
     Check(props["proposal"]!["properties"]!["items"]!["items"]!["properties"]!["sourceRefs"]!["items"]!["const"]!.GetValue<string>()==captured.SourceRef,"Scoped source reference missing");
     if(captured.OwnerType!="Question")Check(props["proposal"]!["properties"]!["evidencePolicy"]!["const"]!.GetValue<string>()=="NoEvidence","Teaching evidence policy not constrained");
    }
    var user=body["messages"]![1]!["content"]!.GetValue<string>();Check(Json.Read<MappingModelInput>(user).Owners[0].Text==input.Owners[0].Text && user.Contains("题面") && !user.Contains("\\u9898"),"Native Chinese inner JSON lost");
    var bytes=Encoding.UTF8.GetBytes(Json.Write(new{model,choices=new[]{new{finish_reason=finish,message=new{content=valid}}},usage=new{completion_tokens=1024}}));request.Response.ContentLength64=bytes.Length;request.Response.ContentType="application/json";await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();return await response;
   }
   var truncated=await Call(frozen.Model,"length");Check(!truncated.OutputComplete && truncated.Usage?.InputTokens==null && truncated.Usage?.OutputTokens==1024 && truncated.Output==valid,"Truncation erased real returned output/usage or invented missing tokens");
   try{await Call("different-model","stop");throw new Exception("Different model accepted");}catch(ApiError e){Check(e.Code=="LOCAL_PROVIDER_MODEL_MISMATCH","Wrong model mismatch rejection");}
   try{(frozen with{Endpoint="https://example.com/v1"}).Validate();throw new Exception("External endpoint accepted");}catch(ApiError e){Check(e.Code=="MAPPING_LOCAL_CONFIGURATION_INVALID","Wrong external transport rejection");}
   try{(frozen with{PromptHash="changed-prompt"}).Validate();throw new Exception("Unknown prompt accepted");}catch(ApiError e){Check(e.Code=="MAPPING_LOCAL_CONFIGURATION_INVALID","Wrong prompt mismatch rejection");}
   var waiting=new LocalMappingModelProvider(frozen with{TimeoutMilliseconds=1000},keyPath).Generate(input,CancellationToken.None);var unanswered=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
   try{await waiting;throw new Exception("Unanswered local completion invented a response");}catch(OperationCanceledException){}finally{unanswered.Response.Close();}
   if(!OperatingSystem.IsWindows())
   {
    File.SetUnixFileMode(keyPath,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead);
    try{await provider.Generate(input,CancellationToken.None);throw new Exception("Non-private key accepted");}catch(ApiError e){Check(e.Code=="LOCAL_PROVIDER_KEY_NOT_PRIVATE","Wrong private key rejection");}
   }
   Console.WriteLine("PASS real controlled local HTTP transport pins model/config, sends native Chinese strict schema, retains truncated response and actual/missing usage, rejects changed model/prompt/external endpoint; no real model calls");
  }
  finally{File.Delete(keyPath);}
 }

}
