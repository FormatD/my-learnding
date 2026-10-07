using Learning;
using Microsoft.EntityFrameworkCore;
public static class MappingCallCrashCases
{
 static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
 public static async Task Run(Database db,string mode)
 {
  if(mode=="mapping-call-crash-seed")
  {
   await db.Database.MigrateAsync();var family=new Family();var account=new Account{FamilyId=family.Id,UserName="mapping-crash-"+Guid.NewGuid()};var catalog=Content.Fixture();var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var draft=new ContentDraft{FamilyId=family.Id,Title="受控真实中断",Payload=Json.Write(catalog)};db.AddRange(family,account,new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles="Parent,ContentEditor"},release,draft);await db.SaveChangesAsync();var q=catalog.Questions[0];await MappingJobs.Enqueue(db,new Actor(Guid.NewGuid(),family.Id,account.Id,null,"Parent","Parent,ContentEditor"),new(draft.Id,release.Id,[new("Question",q.Id,q.RevisionId)]));await db.SaveChangesAsync();await db.Set<BackgroundJob>().ExecuteUpdateAsync(j=>j.SetProperty(x=>x.LeaseSeconds,3).SetProperty(x=>x.HeartbeatSeconds,1));return;
  }
  var prep=await db.Set<MappingPreparation>().SingleAsync();var f=Json.Read<MappingFrozenInput>(prep.Snapshot);var input=MappingModelProtocol.Capture(Json.Read<Catalog>(f.SourcePayload),f.SourceDraftId,f.Selections,Json.Read<Catalog>(f.LibraryPayload).Kcs);var config=new MappingLocalConfiguration("mapping-local/1","http://127.0.0.1:8000/v1","controlled-local",1024,10000,MappingModelProtocol.PromptHash);
  await using var lease=await BackgroundJobs.Claim(db,CancellationToken.None,[MappingJobs.Type]);Check(lease!=null,"real expired lease not claimed");
  if(mode=="mapping-call-crash") {await new MappingModelCallTracking(db,prep,config,lease!,new Paused()).Generate(input,CancellationToken.None);throw new Exception("paused worker unexpectedly returned");}
  var original=await db.Set<MappingModelCall>().AsNoTracking().SingleAsync();var bytes=Json.Write(original);Check(original.Status=="Started" && original.FinishedAt==null && original.InputTokens==null && original.OutputTokens==null && original.ChargedCost==null && original.OutputHash==null,"killed call invented a completion");var provider=new Returned();var tracker=new MappingModelCallTracking(db,prep,config,lease!,provider);
  try{await tracker.Generate(input,CancellationToken.None);throw new Exception("unknown call slot automatically released after process death");}catch(ApiError e){Check(e.Code=="BUILDER_CONCURRENCY_LIMIT" && provider.Calls==0,"denied recovery still called provider");}
  var accountRow=await db.Accounts.SingleAsync();db.Add(new MappingCallReconciliation{FamilyId=prep.FamilyId,CallId=original.Id,ActorId=accountRow.Id,Reason="隔离测试已观察原受控提供者进程SIGKILL",ReceiptReference="Observed actual worker SIGKILL"});await db.SaveChangesAsync();await tracker.Generate(input,CancellationToken.None);
  var calls=await db.Set<MappingModelCall>().AsNoTracking().OrderBy(c=>c.CreatedAt).ToArrayAsync();Check(calls.Length==3 && calls.Select(c=>c.ExecutionId).Distinct().Count()==2 && calls.Count(c=>c.Status=="Returned")==1 && calls.Count(c=>c.Status=="Denied")==1 && Json.Write(calls.Single(c=>c.Id==original.Id))==bytes && !await db.Set<MappingRun>().AnyAsync(),"recovery rewrote unknown call or fabricated mapping outputs");Check(await db.Set<JobLeaseAttempt>().CountAsync(a=>a.Status=="LeaseExpired")==1,"actual expired claim history lost");
  await using(var tx=await db.Database.BeginTransactionAsync()){await lease!.Finish(db,"Failed","CONTROLLED_LEDGER_TEST_ONLY",null,CancellationToken.None);await tx.CommitAsync();}
  Console.WriteLine("PASS actual mapping worker SIGKILL after durable Started; real lease expiry/reclaim retains unknown usage and slot, denied replacement makes no provider call; explicit observed termination preserves original history and permits independent returned physical call; no mapping result invented");
 }
 sealed class Paused:IMappingModelProvider{public async Task<BuilderProviderResponse> Generate(MappingModelInput input,CancellationToken ct){Console.WriteLine("MAPPING_CALL_STARTED");Console.Out.Flush();await Task.Delay(Timeout.Infinite,ct);throw new Exception("unreachable");}}
 sealed class Returned:IMappingModelProvider{public int Calls;public Task<BuilderProviderResponse> Generate(MappingModelInput input,CancellationToken ct){Calls++;return Task.FromResult(new BuilderProviderResponse("controlled actual return",new(12,3,0,null,"LocalMeasured")));}}
}
