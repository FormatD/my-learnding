using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
public static class MappingLocalJobCases
{
    static void Check(bool value,string error){if(!value)throw new Exception(error);}
    static Database Open(Database db,IConfiguration configuration)=>new(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()).Options,configuration);
    static IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Omlx:Endpoint","http://127.0.0.1:8000/v1"},{"Omlx:Model","controlled-frozen-local"},{"Omlx:MaxOutputTokens","1024"},{"Omlx:TimeoutMilliseconds","10000"}}).Build();
    static async Task<(MappingPreparation Preparation,Actor Actor)> Seed(Database db)
    {
        var family=new Family();var account=new Account{FamilyId=family.Id,UserName="mapping-local-job-"+Guid.NewGuid()};var catalog=Content.Fixture();
        catalog=catalog with{Lessons=catalog.Lessons.Select(l=>l with{RevisionId=Guid.NewGuid()}).ToArray(),Resources=catalog.Resources.Select(r=>r with{RevisionId=Guid.NewGuid()}).ToArray()};
        var draft=new ContentDraft{FamilyId=family.Id,Title="受控本机映射任务夹具",Payload=Json.Write(catalog)};var release=new Release{FamilyId=family.Id,Number=1,Payload=draft.Payload,Hash=Content.Hash(draft.Payload)};
        db.AddRange(family,account,new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles="Parent,ContentEditor"},draft,release);await db.SaveChangesAsync();family.OwnerAccountId=account.Id;await Publishing.Register(db,release);await db.SaveChangesAsync();
        var actor=new Actor(Guid.NewGuid(),family.Id,account.Id,null,"Parent","Parent,ContentEditor");var selections=new MappingOwnerSelection[]{new("Question",catalog.Questions[0].Id,catalog.Questions[0].RevisionId),new("Lesson",catalog.Lessons[0].Id,catalog.Lessons[0].RevisionId!.Value),new("Resource",catalog.Resources[0].Id,catalog.Resources[0].RevisionId!.Value)};
        await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(family.Id);var p=await MappingJobs.Enqueue(db,actor,new(draft.Id,release.Id,selections,"LocalOmlx"));await db.SaveChangesAsync();
        Check((await MappingJobs.Enqueue(db,actor,new(draft.Id,release.Id,selections,"LocalOmlx"))).Id==p.Id,"identical frozen configuration did not reuse queued input");await tx.CommitAsync();
        try{await MappingBuilder.PrepareLegacy(db,actor,new(draft.Id,release.Id,selections,"LocalOmlx"));throw new Exception("local synchronous entry accepted");}catch(ApiError error){Check(error.Code=="MAPPING_JOB_REQUIRED","wrong synchronous local rejection");}
        return(p,actor);
    }
    sealed class Paused(string mode):IMappingModelProvider
    {
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MappingModelInput? Input {get;private set;}
        public async Task<BuilderProviderResponse> Generate(MappingModelInput input,CancellationToken ct)
        {
            Input=input;Started.TrySetResult();await Release.Task.WaitAsync(ct);
            if(mode=="Timeout")throw new OperationCanceledException("controlled provider deadline, outer lease not cancelled");
            if(mode=="Failure")throw new IOException("controlled no confirmed response");
            var results=input.Owners.Select(o=>o.OwnerType=="Question"?
                new MappingModelResult(o.OwnerType,o.OwnerId,o.OwnerRevisionId,"Propose","受控结构夹具，仅验证冻结引用和待审核保存。",[o.Text[..Math.Min(15,o.Text.Length)]],new("SingleKC",[new(input.Library[0].Id,input.Library[0].RevisionId,"Primary",1,1,"WholeItem",null,1,null,[o.SourceRef])])):
                new MappingModelResult(o.OwnerType,o.OwnerId,o.OwnerRevisionId,"NeedsReview","受控空建议，需要人工核对教学内容。",[],new("NoEvidence",[]))).ToArray();
            return new(mode=="Malformed"?"{}":Json.Write(new MappingModelEnvelope(MappingModelProtocol.Version,results)),new(21,9,0,null,"LocalMeasured"),mode!="Truncated");
        }
    }
    public static async Task Run(Database owner,string modeName="mapping-local-job")
    {
        await owner.Database.MigrateAsync();
        if(modeName!="mapping-local-job"){await Crash(owner,modeName);return;}
        foreach(var mode in new[]{"Success","Role","Withdrawal","Cancelled","Malformed","Truncated","Timeout","Failure"})
        {
            var configuration=Configuration();await using var db=Open(owner,configuration);var (p,actor)=await Seed(db);var frozen=Json.Read<MappingFrozenInput>(p.Snapshot);
            Check(frozen.Version=="mapping-job/2" && MappingJobs.ResolveLocal(frozen).Model=="controlled-frozen-local" && frozen.ModelConfigHash==Content.Hash(frozen.ModelConfigPayload!),"local snapshot missing fixed configuration");
            configuration["Omlx:Model"]="later-runtime-model";var provider=new Paused(mode);var working=MappingJobs.ProcessOne(db,provider:provider);await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await using(var writer=BackgroundJobs.Open(owner))using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3)))await using(var tx=await writer.Database.BeginTransactionAsync(deadline.Token))
            {
                await writer.Lock(p.FamilyId,deadline.Token);writer.Audits.Add(new(){FamilyId=p.FamilyId,Action="LocalMappingConcurrentWrite",Details="controlled provider remains paused"});
                if(mode=="Role")await writer.Set<FamilyMembership>().Where(m=>m.FamilyId==p.FamilyId).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,"Parent"),deadline.Token);
                if(mode=="Withdrawal")await writer.Releases.Where(r=>r.Id==p.LibraryReleaseId).ExecuteUpdateAsync(u=>u.SetProperty(r=>r.Withdrawn,true),deadline.Token);
                if(mode=="Cancelled")await JobCancellation.Cancel(writer,actor,p.JobId,"受控提供者等待期间实际取消",deadline.Token);
                if(mode=="Success")await writer.Drafts.Where(d=>d.Id==p.SourceDraftId).ExecuteUpdateAsync(u=>u.SetProperty(d=>d.Version,d=>d.Version+1).SetProperty(d=>d.Title,"等待期间另存，不替换旧快照"),deadline.Token);
                await writer.SaveChangesAsync(deadline.Token);await tx.CommitAsync(deadline.Token);
            }
            if(mode!="Cancelled")provider.Release.TrySetResult();await working.WaitAsync(TimeSpan.FromSeconds(12));db.ChangeTracker.Clear();
            var job=await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId);var call=await db.Set<MappingModelCall>().SingleAsync(c=>c.PreparationId==p.Id);
            Check(call.Model=="controlled-frozen-local" && call.ModelConfigPayload==frozen.ModelConfigPayload && call.InputPayload==MappingModelProtocol.UserFor(provider.Input!),"runtime changes replaced frozen invocation");
            var suggestions=await db.Set<MappingSuggestion>().Where(s=>s.RunId==p.RunId).ToArrayAsync();
            if(mode=="Success")
            {
                var run=await db.Set<MappingRun>().SingleAsync(r=>r.Id==p.RunId);Check(job.Status=="Succeeded" && run.SourcePayload==frozen.SourcePayload && run.SourceTitle==frozen.SourceTitle && suggestions.Length==3,"frozen source/results not committed atomically");
                Check(suggestions.All(s=>s.Status=="Pending" && s.ModelResultPayload!=null && Json.Read<string[]>(s.ValidationFlags).Contains("LocalModelUnreviewed")) && suggestions.Count(s=>Json.Read<MappingModelResult>(s.ModelResultPayload!).Decision=="NeedsReview")==2,"model output lost reason/quotes or became reviewed");
                var before=Json.Write(suggestions.OrderBy(s=>s.Id).ToArray());await MappingJobs.ProcessOne(db,provider:provider);Check(before==Json.Write(await db.Set<MappingSuggestion>().Where(s=>s.RunId==p.RunId).OrderBy(s=>s.Id).ToArrayAsync()) && await db.Set<MappingModelCall>().CountAsync(c=>c.PreparationId==p.Id)==1,"duplicate consumption called model or changed output");
                try{await db.Set<MappingSuggestion>().Where(s=>s.Id==suggestions[0].Id).ExecuteUpdateAsync(u=>u.SetProperty(s=>s.ModelResultPayload,(string?)null));throw new Exception("original model explanation overwritten");}catch(Npgsql.PostgresException error){Check(error.SqlState=="23514","wrong immutable model result error");}
            }
            else
            {
                Check(suggestions.Length==0 && !await db.Set<MappingRun>().AnyAsync(r=>r.Id==p.RunId),"failed or cancelled local job saved partial suggestions");
                var expected=mode switch{"Role"=>"JOB_REQUESTER_FORBIDDEN","Withdrawal"=>"LIBRARY_WITHDRAWN","Cancelled"=>JobCancellation.Code,"Malformed"=>"MAPPING_MODEL_SCHEMA_INVALID","Truncated"=>"LOCAL_PROVIDER_OUTPUT_INCOMPLETE","Timeout"=>"LOCAL_PROVIDER_TIMEOUT",_=>"MAPPING_PROCESSING_FAILED"};
                Check(job.Status==(mode=="Cancelled"?"Cancelled":"Failed") && job.LastErrorCode==expected,"wrong local stop/retry behavior: "+mode+" "+job.Status+" "+job.LastErrorCode);
            }
            if(mode is "Timeout" or "Failure" or "Cancelled")Check(call.BudgetState=="Unresolved" && call.InputTokens==null && call.OutputTokens==null && call.ChargedCost==null && (await BuilderBudget.State(db,p.FamilyId,call.BudgetDay)).ActiveCalls==1,"unknown invocation automatically released or invented usage");
            else Check(call.Status=="Returned" && call.BudgetState=="Settled" && call.InputTokens==21 && call.OutputTokens==9 && call.ChargedCost==0,"output rejection erased returned actual usage");
            if(mode is "Malformed" or "Truncated")
            {
                var original=Json.Write(call);await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(p.FamilyId);await MappingJobs.Retry(db,actor,p.Id,"受控真实返回后核对原输入，明确重新处理");await db.SaveChangesAsync();await tx.CommitAsync();}
                var retryProvider=new Paused("Success");retryProvider.Release.TrySetResult();await MappingJobs.ProcessOne(db,provider:retryProvider);db.ChangeTracker.Clear();
                var calls=await db.Set<MappingModelCall>().Where(c=>c.PreparationId==p.Id).ToArrayAsync();
                Check((await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId)).Status=="Succeeded" && calls.Length==2 && calls.Any(c=>c.RetryRound==1 && c.Model=="controlled-frozen-local") && Json.Write(calls.Single(c=>c.Id==call.Id))==original && (await db.Set<MappingPreparation>().SingleAsync(x=>x.Id==p.Id)).Snapshot==p.Snapshot,"explicit retry replaced input or erased prior physical facts");
            }
            Check(!await db.Set<MappingReviewDecision>().AnyAsync(d=>d.FamilyId==p.FamilyId),"local fixture fabricated human review");
            await db.Families.Where(f=>f.Id==p.FamilyId).ExecuteDeleteAsync();Check(!await db.Set<MappingModelCall>().AnyAsync(c=>c.FamilyId==p.FamilyId),"local family deletion retained ledger");
            Console.WriteLine("PASS actual isolated PostgreSQL local queue "+mode+": frozen config, write during wait, fenced pending-only results and durable physical facts; controlled provider");
        }
        await BudgetDenial(owner);
    }
    sealed class BeforeLocalCommit:DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData data,InterceptionResult result,CancellationToken ct=default)
        {
            if(data.Context?.ChangeTracker.Entries<MappingRun>().Any()==true){Console.WriteLine("LOCAL_MAPPING_BEFORE_RESULT_COMMIT");Console.Out.Flush();await Task.Delay(Timeout.Infinite,CancellationToken.None);}
            return result;
        }
    }
    static async Task Crash(Database owner,string mode)
    {
        if(mode=="mapping-local-job-seed")
        {
            await using var db=Open(owner,Configuration());var(p,_)=await Seed(db);await db.Set<BackgroundJob>().Where(j=>j.Id==p.JobId).ExecuteUpdateAsync(u=>u.SetProperty(j=>j.LeaseSeconds,3).SetProperty(j=>j.HeartbeatSeconds,1));return;
        }
        if(mode=="mapping-local-job-crash")
        {
            var options=new DbContextOptionsBuilder<Database>().UseNpgsql(owner.Database.GetConnectionString()).AddInterceptors(new BeforeLocalCommit());await using var db=new Database(options.Options,Configuration());var provider=new Paused("Success");provider.Release.TrySetResult();await MappingJobs.ProcessOne(db,provider:provider);return;
        }
        if(mode!="mapping-local-job-recover")throw new Exception("unknown controlled local crash mode");
        var preparation=await owner.Set<MappingPreparation>().SingleAsync();var original=await owner.Set<MappingModelCall>().AsNoTracking().SingleAsync();var bytes=Json.Write(original);Check(original.Status=="Returned" && original.BudgetState=="Settled" && original.InputTokens==21 && original.OutputTokens==9 && !await owner.Set<MappingRun>().AnyAsync() && !await owner.Set<MappingSuggestion>().AnyAsync(),"crash erased physical return or exposed partial results");
        await using var a=Open(owner,Configuration());await using var b=Open(owner,Configuration());var first=new Paused("Success");var second=new Paused("Success");first.Release.TrySetResult();second.Release.TrySetResult();await Task.WhenAll(MappingJobs.ProcessOne(a,provider:first),MappingJobs.ProcessOne(b,provider:second));owner.ChangeTracker.Clear();
        var calls=await owner.Set<MappingModelCall>().AsNoTracking().ToArrayAsync();Check(calls.Length==2 && calls.All(c=>c.Status=="Returned") && calls.Select(c=>c.ExecutionId).Distinct().Count()==2 && Json.Write(calls.Single(c=>c.Id==original.Id))==bytes && await owner.Set<MappingRun>().CountAsync()==1 && await owner.Set<MappingSuggestion>().CountAsync()==3 && await owner.Set<JobLeaseAttempt>().CountAsync(j=>j.Status=="LeaseExpired")==1 && await owner.Set<JobLeaseAttempt>().CountAsync(j=>j.Status=="Succeeded")==1 && (await owner.Set<BackgroundJob>().SingleAsync(j=>j.Id==preparation.JobId)).Status=="Succeeded","local result crash recovery duplicated output or rewrote actual calls/leases");
        Console.WriteLine("PASS actual local queue SIGKILL before result commit retains Returned usage, expiry/racing consumers produce one pending result set and a distinct new physical call; controlled provider");
    }
    sealed class PausedBuilder:IBuilderCandidateProvider
    {
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BuilderQuote Quote(BuilderProviderRequest input)=>BuilderQuote.Local;
        public async Task<BuilderProviderResponse> Generate(BuilderProviderRequest input,CancellationToken ct){Started.TrySetResult();await Release.Task.WaitAsync(ct);return new("controlled budget response",new(4,2,0,null,"LocalMeasured"));}
    }
    static async Task BudgetDenial(Database owner)
    {
        await using var db=Open(owner,Configuration());var(p,actor)=await Seed(db);var source=new Source{FamilyId=p.FamilyId,Title="受控建库并发",Text="受控内容",Hash="controlled-source"};var builderRun=new BuilderRun{FamilyId=p.FamilyId,SourceId=source.Id,InputHash="controlled-budget"};db.AddRange(source,builderRun);await db.SaveChangesAsync();
        var builder=new PausedBuilder();var pending=new BuilderCallTracking(db,builderRun,1,builder).Generate(new([new(Guid.NewGuid(),source.Text)],BuilderProtocol.Schema),CancellationToken.None);await builder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var provider=new Paused("Success");await MappingJobs.ProcessOne(db,provider:provider);db.ChangeTracker.Clear();var denied=await db.Set<MappingModelCall>().SingleAsync(c=>c.PreparationId==p.Id);var bytes=Json.Write(denied);
        Check(denied.Status=="Denied" && !provider.Started.Task.IsCompleted && (await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId)).LastErrorCode=="BUILDER_CONCURRENCY_LIMIT","actual queue bypassed shared active Builder or called denied provider");
        builder.Release.TrySetResult();await pending;
        await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(p.FamilyId);await MappingJobs.Retry(db,actor,p.Id,"实际建库调用已返回，明确恢复原映射任务");await db.SaveChangesAsync();await tx.CommitAsync();}
        provider.Release.TrySetResult();await MappingJobs.ProcessOne(db,provider:provider);db.ChangeTracker.Clear();Check((await db.Set<BackgroundJob>().SingleAsync(j=>j.Id==p.JobId)).Status=="Succeeded" && Json.Write(await db.Set<MappingModelCall>().SingleAsync(c=>c.Id==denied.Id))==bytes && await db.Set<MappingModelCall>().CountAsync(c=>c.PreparationId==p.Id)==2,"budget recovery erased denial or failed fixed retry");
        await db.Families.Where(f=>f.Id==p.FamilyId).ExecuteDeleteAsync();Console.WriteLine("PASS actual queue shared Builder denial never invokes mapping provider; explicit recovery preserves denied call and frozen input");
    }
}
