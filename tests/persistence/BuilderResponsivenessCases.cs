using Learning;
using Microsoft.EntityFrameworkCore;
public static class BuilderResponsivenessCases
{
 public static async Task Run(Database db,string mode)
 {
  await db.Database.MigrateAsync();var family=new Family();var source=new Source{FamilyId=family.Id,Title="受控慢模型边界",Text="余数必须小于除数。",Hash=Content.Hash("余数必须小于除数。")};var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,InputHash="controlled-slow-builder"};var chunk=new Chunk{FamilyId=family.Id,SourceId=source.Id,Locator="段落1",Text=source.Text};db.AddRange(family,source,run,chunk);await db.SaveChangesAsync();
  Database Open()=>new(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()).Options);
  var provider=new PausedProvider();await using var worker=Open();var work=Builder.ProcessOne(worker,CancellationToken.None,provider:provider);await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
  bool blocked=false;
  try{
   await using var writing=Open();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(2));await using var tx=await writing.Database.BeginTransactionAsync(deadline.Token);await writing.Lock(family.Id,deadline.Token);
   if(mode=="builder-responsiveness-changed-source")await writing.Chunks.Where(c=>c.Id==chunk.Id).ExecuteUpdateAsync(u=>u.SetProperty(c=>c.Text,"输入被另一项编辑修改。"),deadline.Token);
   writing.Audits.Add(new(){FamilyId=family.Id,Action="ControlledConcurrentWrite",Details="Slow builder has not returned"});await writing.SaveChangesAsync(deadline.Token);await tx.CommitAsync(deadline.Token);
  }catch(OperationCanceledException){blocked=true;}
  finally{provider.Release.TrySetResult();}
  await work.WaitAsync(TimeSpan.FromSeconds(15));
  if(mode=="builder-responsiveness-before") {if(!blocked)throw new Exception("Original lock regression did not reproduce");Console.WriteLine("CONFIRMED: actual provider wait holds family lock and prevents another family write for 2 seconds");return;}
  if(blocked)throw new Exception("Slow provider still holds family write lock");
  db.ChangeTracker.Clear();
  if(mode=="builder-responsiveness-changed-source")
  {
   var changed=await db.BuilderRuns.SingleAsync(r=>r.Id==run.Id);
   if(changed.Status!="Failed" || changed.Error!="JOB_INPUT_SNAPSHOT_CHANGED" || await db.Candidates.AnyAsync(c=>c.RunId==run.Id) || !await db.Set<BuilderCall>().AnyAsync(c=>c.RunId==run.Id && c.Status=="Returned"))throw new Exception("Changed source result committed or actual returned call erased");
   Console.WriteLine("PASS source changes during model wait discard candidates while preserving the actual returned call ledger");return;
  }
  if((await db.BuilderRuns.SingleAsync(r=>r.Id==run.Id)).Status!="Completed"||await db.Candidates.CountAsync(c=>c.RunId==run.Id)!=1||!await db.Audits.AnyAsync(a=>a.Action=="ControlledConcurrentWrite"))throw new Exception("Concurrent write or complete candidate result missing");
  Console.WriteLine("PASS provider waits outside family transaction; concurrent actual write commits before provider returns; result remains atomic");
 }
 sealed class PausedProvider:IBuilderCandidateProvider
 {
  public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
  public async Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct){Started.TrySetResult();await Release.Task.WaitAsync(ct);return await new MockBuilderCandidateProvider().Generate(request,ct);}
 }
}
