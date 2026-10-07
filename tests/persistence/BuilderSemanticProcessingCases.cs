using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
public static class BuilderSemanticProcessingCases
{
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    sealed class Paused(string mode):IBuilderSemanticProvider
    {
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Output="";
        public async Task<BuilderProviderResponse> Generate(BuilderSemanticInput input,CancellationToken ct)
        {
            Started.TrySetResult();await Release.Task.WaitAsync(ct);
            if(mode=="Timeout")throw new OperationCanceledException("Controlled provider deadline only");
            if(mode=="Failure")throw new IOException("Controlled provider did not confirm a return");
            Output=mode=="Malformed"?"{}":Json.Write(new BuilderSemanticResult(BuilderSemanticProtocol.Version,input.CandidateId,"CreateDraft",null,null,"受控工作流夹具，需要人工核对行为及边界。",[new(input.Fragments[0].Id,"先算乘除，再算加减")],[]));
            return new(Output,new(17,9,0,null,"LocalMeasured"),mode!="Truncated");
        }
    }
    public static async Task Run(Database owner)
    {
        await owner.Database.MigrateAsync();
        foreach(var mode in new[]{"Success","Source","Reviewed","Role","Withdrawal","Cancelled","Malformed","Truncated","Timeout","Failure"})
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Omlx:Model","controlled-frozen-semantic"}}).Build();await using var db=new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(owner.Database.GetConnectionString()).Options,config);
            var (actor,p)=await BuilderSemanticPreparationCases.Seed(db);await db.Families.Where(f=>f.Id==p.FamilyId).ExecuteUpdateAsync(u=>u.SetProperty(f=>f.OwnerAccountId,actor.AccountId));await db.Set<BackgroundJob>().Where(j=>j.Id==p.JobId).ExecuteUpdateAsync(u=>u.SetProperty(j=>j.LeaseSeconds,6).SetProperty(j=>j.HeartbeatSeconds,1));db.ChangeTracker.Clear();
            var candidateBefore=Json.Write(await db.Candidates.AsNoTracking().SingleAsync(c=>c.Id==p.CandidateId));config["Omlx:Model"]="later-runtime-model";
            var provider=new Paused(mode);var working=BuilderSemanticProcessing.ProcessOne(db,provider:provider);await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await using(var writer=BackgroundJobs.Open(owner))using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3)))await using(var tx=await writer.Database.BeginTransactionAsync(deadline.Token))
            {
                await writer.Lock(p.FamilyId,deadline.Token);writer.Audits.Add(new(){FamilyId=p.FamilyId,Action="SemanticProcessingConcurrentWrite",Details="Actual controlled provider remains paused"});
                if(mode=="Source"){var sourceId=await writer.BuilderRuns.Where(r=>r.Id==p.RunId).Select(r=>r.SourceId).SingleAsync(deadline.Token);await writer.Sources.Where(s=>s.Id==sourceId).ExecuteUpdateAsync(u=>u.SetProperty(s=>s.Text,s=>s.Text+" 来源变化。"),deadline.Token);}
                if(mode=="Reviewed")await writer.Candidates.Where(c=>c.Id==p.CandidateId).ExecuteUpdateAsync(u=>u.SetProperty(c=>c.Status,"Rejected"),deadline.Token);
                if(mode=="Role")await writer.Set<FamilyMembership>().Where(m=>m.FamilyId==p.FamilyId).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,"Parent"),deadline.Token);
                if(mode=="Withdrawal")await writer.Releases.Where(r=>r.Id==p.LibraryReleaseId).ExecuteUpdateAsync(u=>u.SetProperty(r=>r.Withdrawn,true),deadline.Token);
                if(mode=="Cancelled")await JobCancellation.Cancel(writer,actor,p.JobId,"Controlled cancellation during provider wait",deadline.Token);
                await writer.SaveChangesAsync(deadline.Token);await tx.CommitAsync(deadline.Token);
            }
            if(mode!="Cancelled")provider.Release.TrySetResult();await working.WaitAsync(TimeSpan.FromSeconds(12));db.ChangeTracker.Clear();
            var job=await db.Set<BackgroundJob>().AsNoTracking().SingleAsync(j=>j.Id==p.JobId);var call=await db.Set<BuilderSemanticCall>().AsNoTracking().SingleAsync(c=>c.PreparationId==p.Id);
            Check(call.Model=="controlled-frozen-semantic" && call.InputPayload==Json.Read<BuilderSemanticFrozenInput>(p.Snapshot).ModelInputPayload,"Current runtime replaced original frozen model/input");
            var suggestion=await db.Set<BuilderSemanticSuggestion>().AsNoTracking().SingleOrDefaultAsync(s=>s.PreparationId==p.Id);var receipt=await db.Set<BuilderSemanticResponse>().AsNoTracking().SingleOrDefaultAsync(r=>r.CallId==call.Id);
            if(mode=="Success")
            {
                Check(job.Status=="Succeeded" && suggestion!=null && suggestion.ResponseId==receipt!.Id && Json.Read<BuilderSemanticResult>(suggestion.ResultPayload).Decision=="CreateDraft","Successful suggestion did not commit with actual task");
                Check(await db.Set<JobLeaseAttempt>().AnyAsync(a=>a.JobId==p.JobId && a.Status=="Succeeded") && await db.Audits.AnyAsync(a=>a.Action=="BuilderSemanticSuggestionPrepared" && a.FamilyId==p.FamilyId),"Atomic result lacks real terminal receipt/audit");
                await BuilderSemanticProcessing.ProcessOne(db,provider:provider);Check(await db.Set<BuilderSemanticCall>().CountAsync(c=>c.PreparationId==p.Id)==1,"Duplicate consumption invoked model");
                try{await db.Set<BuilderSemanticSuggestion>().Where(s=>s.Id==suggestion!.Id).ExecuteUpdateAsync(u=>u.SetProperty(s=>s.ResultPayload,"{}"));throw new Exception("Original suggestion overwritten");}catch(Npgsql.PostgresException ex){Check(ex.SqlState=="23514","Wrong suggestion protection");}
            }
            else
            {
                var expected=mode switch{"Source"=>"JOB_INPUT_SNAPSHOT_CHANGED","Reviewed"=>"CANDIDATE_ALREADY_REVIEWED","Role"=>"JOB_REQUESTER_FORBIDDEN","Withdrawal"=>"LIBRARY_WITHDRAWN","Cancelled"=>JobCancellation.Code,"Malformed"=>"BUILDER_SEMANTIC_SCHEMA_INVALID","Truncated"=>"LOCAL_PROVIDER_OUTPUT_INCOMPLETE","Timeout"=>"LOCAL_PROVIDER_TIMEOUT",_=>"SEMANTIC_PROCESSING_FAILED"};
                Check(suggestion==null && job.Status==(mode=="Cancelled"?"Cancelled":"Failed") && job.LastErrorCode==expected,"Wrong semantic failure state "+mode+": "+job.Status+"/"+job.LastErrorCode);
            }
            if(mode is "Timeout" or "Failure" or "Cancelled")Check(receipt==null && call.BudgetState=="Unresolved" && call.InputTokens==null && call.OutputTokens==null && call.ChargedCost==null && (await BuilderBudget.State(db,p.FamilyId,call.BudgetDay)).ActiveCalls==1,"Unknown invocation fabricated a return or released a slot");
            else
            {
                Check(receipt!=null && receipt.OutputPayload==provider.Output && receipt.OutputHash==Content.Hash(provider.Output) && receipt.OutputComplete==(mode!="Truncated") && call.Status=="Returned" && call.InputTokens==17 && call.OutputTokens==9 && call.BudgetState=="Settled","Rejected business result erased original raw return/usage");
                try{await db.Set<BuilderSemanticResponse>().Where(r=>r.Id==receipt!.Id).ExecuteUpdateAsync(u=>u.SetProperty(r=>r.OutputPayload,"rewritten"));throw new Exception("Raw output overwritten");}catch(Npgsql.PostgresException ex){Check(ex.SqlState=="23514","Wrong receipt protection");}
            }
            if(mode is "Malformed" or "Truncated" or "Timeout" or "Failure" or "Cancelled" or "Role")
            {
                var retryActor=actor;
                if(mode=="Role")
                {
                    var replacement=new Account{FamilyId=p.FamilyId,UserName="semantic-recovery-"+Guid.NewGuid()};db.AddRange(replacement,new FamilyMembership{FamilyId=p.FamilyId,AccountId=replacement.Id,Roles="Parent,ContentEditor"});await db.SaveChangesAsync();retryActor=new Actor(Guid.NewGuid(),p.FamilyId,replacement.Id,null,"Parent","Parent,ContentEditor");
                }
                if(mode is "Timeout" or "Failure" or "Cancelled")
                {
                    await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(p.FamilyId);try{await BuilderSemanticJobs.Retry(db,actor,p.Id,"Cannot assume provider termination");throw new Exception("Unknown physical call allowed retry without reconciliation");}catch(ApiError ex){Check(ex.Code=="BUILDER_USAGE_RECONCILIATION_REQUIRED","Wrong unknown retry denial");}await tx.RollbackAsync();}
                    await using(var tx=await db.Database.BeginTransactionAsync()){await BuilderSemanticCallTracking.Reconcile(db,actor,call.Id,new(true,null,null,"Observed controlled in-process provider task complete","Actual controlled task ended; no external oMLX involved"));await db.SaveChangesAsync();await tx.CommitAsync();}
                }
                var originalCall=Json.Write(call);var originalSnapshot=p.Snapshot;
                await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(p.FamilyId);await BuilderSemanticJobs.Retry(db,retryActor,p.Id,"Explicitly checked fixed input and completed original invocation");await db.SaveChangesAsync();await tx.CommitAsync();}
                var replacementProvider=new Paused("Success");replacementProvider.Release.TrySetResult();await BuilderSemanticProcessing.ProcessOne(db,provider:replacementProvider);db.ChangeTracker.Clear();
                Check((await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId)).Status=="Succeeded" && await db.Set<BuilderSemanticCall>().CountAsync(c=>c.PreparationId==p.Id)==2 && await db.Set<BuilderSemanticCall>().AnyAsync(c=>c.PreparationId==p.Id && c.RetryRound==1 && c.Model=="controlled-frozen-semantic") && Json.Write(await db.Set<BuilderSemanticCall>().AsNoTracking().SingleAsync(c=>c.Id==call.Id))==originalCall && (await db.Set<BuilderSemanticPreparation>().SingleAsync(x=>x.Id==p.Id)).Snapshot==originalSnapshot,"Explicit retry replaced original configuration/facts or failed");
                Check(await db.Audits.AnyAsync(a=>a.Action=="BuilderSemanticSuggestionPrepared" && a.FamilyId==p.FamilyId && a.ActorId==retryActor.Id),"Recovery ignored actual current authorizer");
            }
            if(mode!="Reviewed")Check(Json.Write(await db.Candidates.AsNoTracking().SingleAsync(c=>c.Id==p.CandidateId))==candidateBefore,"Semantic suggestion performed a candidate/human review write");
            Check(!await db.Drafts.AnyAsync(d=>d.FamilyId==p.FamilyId),"Semantic suggestion auto-created a content draft");
            await db.Families.Where(f=>f.Id==p.FamilyId).ExecuteDeleteAsync();Check(!await db.Set<BuilderSemanticResponse>().AnyAsync(r=>r.FamilyId==p.FamilyId) && !await db.Set<BuilderSemanticSuggestion>().AnyAsync(s=>s.FamilyId==p.FamilyId),"Family deletion retained semantic outputs");
            Console.WriteLine("PASS actual PG semantic queue "+mode+": frozen config, independent raw return, lock-free provider wait, fenced suggestion/terminal receipt, no automatic human review; controlled provider only");
        }
    }
    sealed class BeforeResultCommit:DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData data,InterceptionResult result,CancellationToken ct=default)
        {
            if(data.Context?.ChangeTracker.Entries<BuilderSemanticSuggestion>().Any()==true){Console.WriteLine("SEMANTIC_BEFORE_RESULT_COMMIT");Console.Out.Flush();await Task.Delay(Timeout.Infinite,CancellationToken.None);}return result;
        }
    }
    sealed class Forbidden:IBuilderSemanticProvider
    {
        public int Calls;
        public Task<BuilderProviderResponse> Generate(BuilderSemanticInput input,CancellationToken ct){Calls++;throw new Exception("Recovery must reuse the exact committed return without calling provider");}
    }
    public static async Task Crash(Database owner,string mode)
    {
        await owner.Database.MigrateAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Omlx:Model","controlled-result-crash"}}).Build();var options=new DbContextOptionsBuilder<Database>().UseNpgsql(owner.Database.GetConnectionString());if(mode=="semantic-result-crash")options.AddInterceptors(new BeforeResultCommit());await using var db=new Database(options.Options,config);
        if(mode=="semantic-result-seed"){var (_,p)=await BuilderSemanticPreparationCases.Seed(db);await db.Set<BackgroundJob>().Where(j=>j.Id==p.JobId).ExecuteUpdateAsync(u=>u.SetProperty(j=>j.LeaseSeconds,3).SetProperty(j=>j.HeartbeatSeconds,1));return;}
        if(mode=="semantic-result-crash"){var provider=new Paused("Success");provider.Release.TrySetResult();await BuilderSemanticProcessing.ProcessOne(db,provider:provider);throw new Exception("Commit barrier unexpectedly passed");}
        Check(mode=="semantic-result-recover","Unknown semantic crash mode");var original=await db.Set<BuilderSemanticCall>().AsNoTracking().SingleAsync();var raw=await db.Set<BuilderSemanticResponse>().AsNoTracking().SingleAsync();var bytes=Json.Write(original);var rawBytes=Json.Write(raw);Check(original.Status=="Returned" && original.InputTokens==17 && original.OutputTokens==9 && original.OutputHash==raw.OutputHash && !await db.Set<BuilderSemanticSuggestion>().AnyAsync(),"Result crash lost actual return or exposed uncommitted suggestion");
        await using var second=new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(owner.Database.GetConnectionString()).Options,config);var firstProvider=new Forbidden();var secondProvider=new Forbidden();await Task.WhenAll(BuilderSemanticProcessing.ProcessOne(db,provider:firstProvider),BuilderSemanticProcessing.ProcessOne(second,provider:secondProvider));db.ChangeTracker.Clear();
        Check(firstProvider.Calls==0 && secondProvider.Calls==0 && await db.Set<BuilderSemanticCall>().CountAsync()==1 && Json.Write(await db.Set<BuilderSemanticCall>().AsNoTracking().SingleAsync())==bytes && Json.Write(await db.Set<BuilderSemanticResponse>().AsNoTracking().SingleAsync())==rawBytes,"Recovery duplicated a physical invocation or changed original raw facts");
        Check(await db.Set<BuilderSemanticSuggestion>().CountAsync()==1 && await db.Set<JobLeaseAttempt>().CountAsync(a=>a.Status=="LeaseExpired")==1 && await db.Set<JobLeaseAttempt>().CountAsync(a=>a.Status=="Succeeded")==1 && (await db.Set<BackgroundJob>().SingleAsync()).Status=="Succeeded","Racing replacement workers did not atomically recover one suggestion with original lease history");Check((await db.Candidates.SingleAsync()).Status=="Pending" && !await db.Drafts.AnyAsync(),"Recovery fabricated a human review/draft");Console.WriteLine("PASS actual SIGKILL before semantic result commit: raw returned bytes/usage survive, real expired lease and racing replacements reuse same response without another provider call, exactly one immutable suggestion and terminal receipt; controlled provider only");
    }

}
