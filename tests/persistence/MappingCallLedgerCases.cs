using Learning;
using Microsoft.EntityFrameworkCore;
public static class MappingCallLedgerCases
{
 static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
 public static async Task Run(Database db)
 {
  await db.Database.MigrateAsync();var family=new Family();var account=new Account{FamilyId=family.Id,UserName="mapping-ledger-"+Guid.NewGuid()};var catalog=Content.Fixture();var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var draft=new ContentDraft{FamilyId=family.Id,Payload=Json.Write(catalog),Title="受控账本来源"};
  db.AddRange(family,account,new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles="Parent,ContentEditor"},release,draft);await db.SaveChangesAsync();family.OwnerAccountId=account.Id;await Publishing.Register(db,release);await db.SaveChangesAsync();
  var actor=new Actor(Guid.NewGuid(),family.Id,account.Id,null,"Parent","Parent,ContentEditor");var question=catalog.Questions[0];var prep=await MappingJobs.Enqueue(db,actor,new(draft.Id,release.Id,[new("Question",question.Id,question.RevisionId)]));var source=new Source{FamilyId=family.Id,Title="建库并发",Text="余数必须小于除数。",Hash="controlled-source"};var builderRun=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,InputHash="controlled-builder"};db.AddRange(source,builderRun);await db.SaveChangesAsync();
  await using var lease=await BackgroundJobs.Claim(db,CancellationToken.None,[MappingJobs.Type]);Check(lease!=null,"mapping lease missing");var input=MappingModelProtocol.Capture(catalog,draft.Id,[new("Question",question.Id,question.RevisionId)],catalog.Kcs);var config=new MappingLocalConfiguration("mapping-local/1","http://127.0.0.1:8000/v1","controlled-local",1024,10000,MappingModelProtocol.PromptHash);
  var paused=new PausedMapping();var tracker=new MappingModelCallTracking(db,prep,config,lease!,paused);var generating=tracker.Generate(input,CancellationToken.None);await paused.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
  var day=DateOnly.FromDateTime(DateTime.UtcNow);Check((await BuilderBudget.State(db,family.Id,day)).ActiveCalls==1,"mapping start not in shared budget");
  await using(var writing=BackgroundJobs.Open(db))using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(2)))await using(var tx=await writing.Database.BeginTransactionAsync(deadline.Token)){await writing.Lock(family.Id,deadline.Token);writing.Audits.Add(new(){FamilyId=family.Id,Action="ConcurrentMappingWrite",Details="actual provider remains paused"});await writing.SaveChangesAsync(deadline.Token);await tx.CommitAsync(deadline.Token);}
  await using(var reserve=BackgroundJobs.Open(db)){var denied=await BuilderBudget.Reserve(reserve,new BuilderCall{FamilyId=family.Id,RunId=builderRun.Id,ExecutionId=Guid.NewGuid(),AttemptNumber=1,CallNumber=1},BuilderQuote.Local,CancellationToken.None);Check(denied=="BUILDER_CONCURRENCY_LIMIT","builder bypassed active mapping slot");}
  paused.Release.TrySetResult();var response=await generating;Check(!response.OutputComplete,"controlled truncation lost");
  await using(var output=BackgroundJobs.Open(db))await using(var tx=await output.Database.BeginTransactionAsync()){output.Audits.Add(new(){FamilyId=family.Id,Action="UncommittedMappingResult",Details="controlled rollback"});await output.SaveChangesAsync();await tx.RollbackAsync();}
  db.ChangeTracker.Clear();var returned=await db.Set<MappingModelCall>().SingleAsync();Check(returned.Status=="Returned" && returned.BudgetState=="Settled" && returned.InputTokens==11 && returned.OutputTokens==7 && returned.ChargedCost==0 && returned.ErrorCode=="LOCAL_PROVIDER_OUTPUT_INCOMPLETE" && returned.OutputHash==Content.Hash(response.Output) && !await db.Audits.AnyAsync(a=>a.Action=="UncommittedMappingResult"),"result rollback erased returned ledger or invented usage");
  Check((await BuilderBudget.State(db,family.Id,day)).ActiveCalls==0,"returned local mapping still holds slot");
  var failed=new MappingModelCallTracking(db,prep,config,lease!,new FailingMapping());try{await failed.Generate(input,CancellationToken.None);throw new Exception("provider failure accepted");}catch(IOException){}
  db.ChangeTracker.Clear();var unknown=await db.Set<MappingModelCall>().SingleAsync(c=>c.Status=="Failed");var bytes=Json.Write(unknown);Check(unknown.InputTokens==null && unknown.OutputTokens==null && unknown.ChargedCost==null && unknown.OutputHash==null && unknown.BudgetState=="Unresolved" && (await BuilderBudget.State(db,family.Id,day)).ActiveCalls==1,"unknown mapping automatically released or invented zero usage");
  await using(var reserve=BackgroundJobs.Open(db))Check(await BuilderBudget.Reserve(reserve,new BuilderCall{FamilyId=family.Id,RunId=builderRun.Id,ExecutionId=Guid.NewGuid(),AttemptNumber=1,CallNumber=1},BuilderQuote.Local,CancellationToken.None)=="BUILDER_CONCURRENCY_LIMIT","builder bypassed unknown mapping");
  db.Add(new MappingCallReconciliation{FamilyId=family.Id,CallId=unknown.Id,ActorId=account.Id,Reason="受控提供者已实际抛错结束",ReceiptReference="Observed controlled IOException"});await db.SaveChangesAsync();Check((await BuilderBudget.State(db,family.Id,day)).ActiveCalls==0 && Json.Write(await db.Set<MappingModelCall>().AsNoTracking().SingleAsync(c=>c.Id==unknown.Id))==bytes,"explicit local termination rewrote original call");
  var builder=new PausedBuilder();var build=new BuilderCallTracking(db,builderRun,1,builder).Generate(new([new(Guid.NewGuid(),source.Text)],BuilderProtocol.Schema),CancellationToken.None);await builder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));var deniedProvider=new PausedMapping();
  try{await new MappingModelCallTracking(db,prep,config,lease!,deniedProvider).Generate(input,CancellationToken.None);throw new Exception("mapping bypassed builder slot");}catch(ApiError e){Check(e.Code=="BUILDER_CONCURRENCY_LIMIT" && !deniedProvider.Started.Task.IsCompleted,"denied mapping still called provider");}
  builder.Release.TrySetResult();await build;
  await using(var tx=await db.Database.BeginTransactionAsync()){await lease!.Finish(db,"Succeeded",null,null,CancellationToken.None);await tx.CommitAsync();}
  var calls=await db.Set<MappingModelCall>().AsNoTracking().ToArrayAsync();Check(calls.Length==3 && calls.Select(c=>c.ExecutionId).Distinct().Count()==3 && !await db.Set<MappingRun>().AnyAsync() && !await db.Set<MappingSuggestion>().AnyAsync(),"physical identity lost or ledger fabricated suggestions");
  var foreign=new Family();db.Add(foreign);await db.SaveChangesAsync();
  try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MappingModelCall\" SET \"FamilyId\"={foreign.Id} WHERE \"Id\"={returned.Id}");throw new Exception("cross-family preparation reference accepted");}catch(Npgsql.PostgresException e){Check(e.SqlState=="23503","wrong cross-family FK failure");}
  try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MappingModelCall\" SET \"Currency\"='USD' WHERE \"Id\"={returned.Id}");throw new Exception("external currency accepted on local mapping");}catch(Npgsql.PostgresException e){Check(e.SqlState=="23514","wrong local currency constraint failure");}
  Check((await db.Set<MappingModelCall>().AsNoTracking().SingleAsync(c=>c.Id==returned.Id)).FamilyId==family.Id,"constraint rejection changed original row");
  Console.WriteLine("PASS real PostgreSQL independent mapping Started/Returned/Failed/Denied facts, truncated actual usage and rollback survival, same-family write during wait, two-way Builder/mapping concurrency, unknown hold and explicit termination preserve original record; controlled providers only");
  await db.Families.Where(f=>f.Id==family.Id).ExecuteDeleteAsync();Check(!await db.Set<MappingModelCall>().AnyAsync() && !await db.Set<MappingCallReconciliation>().AnyAsync() && !await db.Set<BuilderCall>().AnyAsync(),"family deletion retained private call facts");Console.WriteLine("PASS family deletion cascades both physical ledgers and reconciliations");
 }
 sealed class PausedMapping:IMappingModelProvider
 {
  public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public async Task<BuilderProviderResponse> Generate(MappingModelInput input,CancellationToken ct){Started.TrySetResult();await Release.Task.WaitAsync(ct);return new("controlled truncated output",new(11,7,0,null,"LocalMeasured"),false);}
 }
 sealed class FailingMapping:IMappingModelProvider{public Task<BuilderProviderResponse> Generate(MappingModelInput input,CancellationToken ct)=>throw new IOException("Controlled failure, no confirmed response");}
 sealed class PausedBuilder:IBuilderCandidateProvider
 {
  public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
  public async Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct){Started.TrySetResult();await Release.Task.WaitAsync(ct);return await new MockBuilderCandidateProvider().Generate(request,ct);}
 }
}
