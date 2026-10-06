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
  if(BuilderConfiguration.ResolveLocal(run)?.Endpoint!="http://127.0.0.1:8000/v1"||BuilderConfiguration.Resolve(run).TimeoutMilliseconds!=120000)throw new Exception("Frozen transport lost");
  foreach(var endpoint in new[]{"http://example.com/v1","http://localhost/v1","http://127.0.0.1/v1?secret=x","http://user@127.0.0.1/v1","http://127.0.0.1/other","https://127.0.0.1/v1"}){
   try{new BuilderLocalTransport(endpoint,1536,LocalOmlxProvider.PromptHash).Validate();throw new Exception("External/ambiguous transport accepted");}catch(ApiError e)when(e.Code=="LOCAL_PROVIDER_INVALID"){}
  }
  var changed=local with{LocalTransport=local.LocalTransport! with{PromptHash=new string('0',64)}};run.ModelConfigPayload=Json.Write(changed);run.ModelConfigHash=Content.Hash(run.ModelConfigPayload);
  try{BuilderConfiguration.ResolveLocal(run);throw new Exception("Changed original prompt accepted");}catch(ApiError e)when(e.Code=="LOCAL_PROVIDER_INVALID"){}
  run.ModelConfigPayload=payload;run.ModelConfigHash=new string('0',64);
  try{BuilderConfiguration.ResolveLocal(run);throw new Exception("Corrupt original configuration accepted");}catch(ApiError e)when(e.Code=="RUN_CONFIGURATION_UNKNOWN"){}
 }
}
