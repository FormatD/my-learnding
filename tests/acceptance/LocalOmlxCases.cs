using Learning;
using Microsoft.Extensions.Configuration;
static class LocalOmlxCases
{
 public static void Run(){
  BuilderCallTracking.ValidateUsage(new(100,50,0,null,"LocalMeasured"));
  foreach(var usage in new[]{new BuilderUsage(-1,1,0,null,"LocalMeasured"),new BuilderUsage(1,1,1,null,"LocalMeasured"),new BuilderUsage(1,1,0,"USD","LocalMeasured")}){try{BuilderCallTracking.ValidateUsage(usage);throw new Exception("Invalid local usage accepted");}catch(ApiError e)when(e.Code=="BUILDER_USAGE_INVALID"){} }
  var usageState=BuilderBudget.Calculate([new BuilderCall{Status="Returned",BudgetDay=DateOnly.FromDateTime(DateTime.UtcNow),BillingStatus="LocalMeasured",BudgetState="Settled",ChargedCost=0,InputTokens=100,OutputTokens=50}],[],DateOnly.FromDateTime(DateTime.UtcNow));if(usageState.ActiveCalls!=0||usageState.UnresolvedCalls!=0||usageState.CostCommitted!=0||usageState.TokensCommitted!=0)throw new Exception("Local usage became external billing");
  var settings=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Omlx:Model","local-model"},{"Omlx:Endpoint","http://127.0.0.1:8000/v1"}}).Build();
  var mock=BuilderConfiguration.Current(settings);if(mock.Provider!="Mock"||Json.Write(mock).Contains("localTransport"))throw new Exception("Legacy mock configuration changed");
  var local=BuilderConfiguration.Current(settings,"LocalOmlx");var payload=Json.Write(local);var run=new BuilderRun{Provider=local.Provider,Model=local.Model,PromptVersion=local.PromptVersion,InputVersion="builder-input/4",ModelConfigPayload=payload,ModelConfigHash=Content.Hash(payload)};
  if(BuilderConfiguration.ResolveLocal(run)?.Endpoint!="http://127.0.0.1:8000/v1"||BuilderConfiguration.Resolve(run).TimeoutMilliseconds!=600000)throw new Exception("Frozen transport lost");
  if(LocalOmlxProvider.LegacyPromptHash!="be2afc8ac9f83fd68676084455af21d5376ffad04bcd9614a9fe14537528461f")throw new Exception("Original prompt bytes changed");
  if(LocalOmlxProvider.Version2PromptHash!="1346946d9a69fe671a97b5aed22bc518b8c62c493dfe3c4a6ff5af619a2e2dd4")throw new Exception("Version 2 prompt bytes changed");
  if(local.LocalTransport!.MaxOutputTokens!=4096||mock.Limits.TimeoutMilliseconds!=30000)throw new Exception("New local defaults or mock limits changed unexpectedly");
  var legacy=local with{Limits=local.Limits with{TimeoutMilliseconds=120000},LocalTransport=local.LocalTransport! with{MaxOutputTokens=1536,PromptHash=LocalOmlxProvider.LegacyPromptHash}};run.ModelConfigPayload=Json.Write(legacy);run.ModelConfigHash=Content.Hash(run.ModelConfigPayload);
  if(BuilderConfiguration.Resolve(run).TimeoutMilliseconds!=120000||BuilderConfiguration.ResolveLocal(run)?.MaxOutputTokens!=1536||BuilderConfiguration.ResolveLocal(run)?.PromptHash!=LocalOmlxProvider.LegacyPromptHash||LocalOmlxProvider.PromptFor(LocalOmlxProvider.LegacyPromptHash)!=LocalOmlxProvider.LegacyPrompt)throw new Exception("Original local task adopted current limits or prompt");
  var previous=legacy with{LocalTransport=legacy.LocalTransport! with{PromptHash=LocalOmlxProvider.Version2PromptHash}};run.ModelConfigPayload=Json.Write(previous);run.ModelConfigHash=Content.Hash(run.ModelConfigPayload);
  if(BuilderConfiguration.Resolve(run).TimeoutMilliseconds!=120000||BuilderConfiguration.ResolveLocal(run)?.PromptHash!=LocalOmlxProvider.Version2PromptHash)throw new Exception("Version 2 task no longer resolves its frozen configuration");
  var excessive=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Omlx:Model","local-model"},{"Omlx:TimeoutMilliseconds","600001"}}).Build();
  try{BuilderConfiguration.Current(excessive,"LocalOmlx");throw new Exception("Unbounded local timeout accepted");}catch(ApiError e)when(e.Code=="BUILDER_CONFIGURATION_INVALID"){}
  var fragment=new BuilderFragment(Guid.NewGuid(),"余数必须小于除数。");var request=new BuilderProviderRequest([fragment],BuilderProtocol.SchemaV2);
  var originalUser=Json.Write(new{fragments=request.Fragments,invalidOutput=request.InvalidOutput,validationCode=request.ValidationCode});
  foreach(var hash in new[]{LocalOmlxProvider.LegacyPromptHash,LocalOmlxProvider.Version2PromptHash}){
   if(LocalOmlxProvider.UserFor(request,hash)!=originalUser)throw new Exception("Frozen old user-message encoding changed");
  }
  var nativeUser=LocalOmlxProvider.UserFor(request,LocalOmlxProvider.PromptHash);
  if(!nativeUser.Contains(fragment.Text)||nativeUser.Contains("\\u4F59",StringComparison.OrdinalIgnoreCase))throw new Exception("New local message still encodes Chinese as escape sequences");
  using(var parsed=System.Text.Json.JsonDocument.Parse(nativeUser))if(parsed.RootElement.GetProperty("fragments")[0].GetProperty("text").GetString()!=fragment.Text)throw new Exception("Native message changed source text");
  var special=new BuilderProviderRequest([new(fragment.Id,"中文\n\"引文\"\\原文 <script> 保留符号÷和余数🧮")],BuilderProtocol.SchemaV2,"{\"候选\":\"待修复\"}","BUILDER_SOURCE_INVALID");
  using(var parsed=System.Text.Json.JsonDocument.Parse(LocalOmlxProvider.UserFor(special,LocalOmlxProvider.PromptHash))){var root=parsed.RootElement;if(root.GetProperty("fragments")[0].GetProperty("text").GetString()!=special.Fragments[0].Text||root.GetProperty("invalidOutput").GetString()!=special.InvalidOutput||root.GetProperty("validationCode").GetString()!=special.ValidationCode)throw new Exception("Native input or repair text did not round-trip exactly");}
  if(Json.Write(LocalOmlxProvider.ResponseFormat(request,LocalOmlxProvider.Version2PromptHash))!=Json.Write(LocalOmlxProvider.ResponseFormat(request,LocalOmlxProvider.PromptHash)))throw new Exception("Native text profile weakened frozen schema constraints");
  using(var format=System.Text.Json.JsonDocument.Parse(Json.Write(LocalOmlxProvider.ResponseFormat(request,LocalOmlxProvider.PromptHash)))){
   var root=format.RootElement;if(root.GetProperty("type").GetString()!="json_schema"||!root.GetProperty("json_schema").GetProperty("strict").GetBoolean())throw new Exception("New local request is not strict schema output");
   var list=root.GetProperty("json_schema").GetProperty("schema").GetProperty("properties").GetProperty("candidates");var fields=list.GetProperty("items").GetProperty("properties");
   if(list.GetProperty("maxItems").GetInt32()!=3||fields.GetProperty("sourceChunkIds").GetProperty("maxItems").GetInt32()!=1||fields.GetProperty("sourceChunkIds").GetProperty("items").GetProperty("enum")[0].GetString()!=fragment.Id.ToString("D"))throw new Exception("Original fragments not constrained in schema");
  }
  if(Json.Write(LocalOmlxProvider.ResponseFormat(request,LocalOmlxProvider.LegacyPromptHash))!="{\"type\":\"json_object\"}"||BuilderProtocol.SchemaV2.Contains("pattern"))throw new Exception("Legacy request or shared schema changed");
  foreach(var endpoint in new[]{"http://example.com/v1","http://localhost/v1","http://127.0.0.1/v1?secret=x","http://user@127.0.0.1/v1","http://127.0.0.1/other","https://127.0.0.1/v1"}){
   try{new BuilderLocalTransport(endpoint,1536,LocalOmlxProvider.PromptHash).Validate();throw new Exception("External/ambiguous transport accepted");}catch(ApiError e)when(e.Code=="LOCAL_PROVIDER_INVALID"){}
  }
  var changed=local with{LocalTransport=local.LocalTransport! with{PromptHash=new string('0',64)}};run.ModelConfigPayload=Json.Write(changed);run.ModelConfigHash=Content.Hash(run.ModelConfigPayload);
  try{BuilderConfiguration.ResolveLocal(run);throw new Exception("Changed original prompt accepted");}catch(ApiError e)when(e.Code=="LOCAL_PROVIDER_INVALID"){}
  run.ModelConfigPayload=payload;run.ModelConfigHash=new string('0',64);
  try{BuilderConfiguration.ResolveLocal(run);throw new Exception("Corrupt original configuration accepted");}catch(ApiError e)when(e.Code=="RUN_CONFIGURATION_UNKNOWN"){}
 }
}
