using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
public static class BuilderSemanticPreparationCases
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Omlx:Model","controlled-semantic-model"}}).Build();
    static Database Open(Database owner,IConfiguration config)=>new(new DbContextOptionsBuilder<Database>().UseNpgsql(owner.Database.GetConnectionString()).Options,config);
    internal static async Task<(Actor Actor,BuilderSemanticPreparation Preparation)> Seed(Database db,bool empty=false)
    {
        var family=new Family();var account=new Account{FamilyId=family.Id,UserName="semantic-preparation-"+Guid.NewGuid()};var actor=new Actor(Guid.NewGuid(),family.Id,account.Id,null,"Parent","Parent,ContentEditor");
        var catalog=Content.Fixture();catalog=catalog with{Kcs=catalog.Kcs.Select(k=>k with{Subject="MATH"}).ToArray()};var payload=Json.Write(catalog);var release=empty?null:new Release{FamilyId=family.Id,Payload=payload,Hash=Content.Hash(payload),Number=1};
        var source=new Source{FamilyId=family.Id,Title="受控语义任务来源",UsageScope="SyntheticWorkflowFixture",Text="明确可测行为：先算乘除，再算加减。",Hash=Content.Hash("明确可测行为：先算乘除，再算加减。")};var chunk=new Chunk{FamilyId=family.Id,SourceId=source.Id,Locator="受控段落1",Text=source.Text};
        var config=BuilderConfiguration.Current(db.RuntimeConfiguration,"LocalOmlx");var configPayload=Json.Write(config);var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,LibraryReleaseId=release?.Id,Type="Candidates",Status="Completed",Provider=config.Provider,Model=config.Model,InputVersion="builder-input/4",PromptVersion=config.PromptVersion,ModelConfigPayload=configPayload,ModelConfigHash=Content.Hash(configPayload),InputHash=Content.Hash("synthetic extraction workflow fixture"),CompletedAt=DateTimeOffset.UtcNow};
        var output=new BuilderCandidateOutput("先乘除后加减","MATH","Procedure",3,3,"独立按先乘除后加减完成混合运算","不包含括号",[chunk.Id],["先算乘除，再算加减"],.99m);
        var matches=empty?[]:Retrieval.Candidates(output,catalog.Kcs,config.Retrieval!);var candidate=new Candidate{FamilyId=family.Id,RunId=run.Id,ChunkId=chunk.Id,ProtocolPayload=Json.Write(output),Name=output.Name,Type=output.KcType,Behavior=output.MeasurableBehavior,Boundary=output.Boundary,Quote=output.SupportingQuotes[0],Matches=Json.Write(matches)};
        db.AddRange(family,account,new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles=actor.Roles},source,chunk,run,candidate);if(release!=null)db.Add(release);await db.SaveChangesAsync();
        await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(family.Id);var preparation=await BuilderSemanticJobs.Enqueue(db,actor,candidate.Id);await db.SaveChangesAsync();var same=await BuilderSemanticJobs.Enqueue(db,actor,candidate.Id);Check(same.Id==preparation.Id,"Identical frozen preparation not reused");await tx.CommitAsync();return(actor,preparation);
    }
    public static async Task Run(Database owner)
    {
        await owner.Database.MigrateAsync();
        var saved=new List<(Actor Actor,BuilderSemanticPreparation Preparation)>();
        foreach(var change in new[]{"None","Source","Candidate","Reviewed","Withdrawal","Role","Retry","Empty"})
        {
            var config=Configuration();await using var db=Open(owner,config);var (actor,p)=await Seed(db,change=="Empty");saved.Add((actor,p));
            await using var lease=await BackgroundJobs.Claim(db,types:[BuilderSemanticJobs.Type],inputRef:p.Id)??throw new Exception("Semantic preparation not durably claimable");
            db.ChangeTracker.Clear();await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock(p.FamilyId);var initial=await BuilderSemanticJobs.ValidateFrozen(db,lease);Check(initial.Input.CandidateId==p.CandidateId && initial.AuthorizedBy==actor.Id,"Frozen candidate identity/authorization lost");await tx.CommitAsync();}
            config["Omlx:Model"]="later-runtime-model";
            await using(var writer=BackgroundJobs.Open(owner))using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3)))await using(var tx=await writer.Database.BeginTransactionAsync(deadline.Token))
            {
                await writer.Lock(p.FamilyId,deadline.Token);writer.Audits.Add(new(){FamilyId=p.FamilyId,Action="SemanticPreparationConcurrentWrite",Details="short transactions leave the family free between checks"});
                if(change=="Source"){var candidate=await writer.Candidates.SingleAsync(c=>c.Id==p.CandidateId);var run=await writer.BuilderRuns.SingleAsync(r=>r.Id==candidate.RunId);await writer.Sources.Where(s=>s.Id==run.SourceId).ExecuteUpdateAsync(u=>u.SetProperty(s=>s.Text,s=>s.Text+" 另增来源文字。"),deadline.Token);}
                if(change=="Candidate")await writer.Candidates.Where(c=>c.Id==p.CandidateId).ExecuteUpdateAsync(u=>u.SetProperty(c=>c.SuggestedAction,"ChangedByOtherWork"),deadline.Token);
                if(change=="Reviewed")await writer.Candidates.Where(c=>c.Id==p.CandidateId).ExecuteUpdateAsync(u=>u.SetProperty(c=>c.Status,"Rejected"),deadline.Token);
                if(change=="Withdrawal")await writer.Releases.Where(r=>r.Id==p.LibraryReleaseId).ExecuteUpdateAsync(u=>u.SetProperty(r=>r.Withdrawn,true),deadline.Token);
                if(change=="Role")await writer.Set<FamilyMembership>().Where(m=>m.FamilyId==p.FamilyId).ExecuteUpdateAsync(u=>u.SetProperty(m=>m.Roles,"Parent"),deadline.Token);
                await writer.SaveChangesAsync(deadline.Token);await tx.CommitAsync(deadline.Token);
            }
            if(change=="Retry")lease.Job.RetryRound=1;
            await using(var tx=await db.Database.BeginTransactionAsync())
            {
                await db.Lock(p.FamilyId);
                try{var result=await BuilderSemanticJobs.ValidateFrozen(db,lease);Check(change is "None" or "Empty","Changed input/authorization accepted: "+change);Check(BuilderSemanticJobs.ResolveLocal(result.Frozen).Model=="controlled-semantic-model","Current model replaced frozen model");Check(!result.Frozen.ModelConfigPayload.Contains("ApiKey"),"Authentication leaked into snapshot");if(change=="Empty")Check(result.Input.Matches.Length==0 && result.Frozen.LibraryHash==null,"No library path fabricated matches/hash");}
                catch(ApiError e){var expected=change switch{"Source" or "Candidate"=>"JOB_INPUT_SNAPSHOT_CHANGED","Reviewed"=>"CANDIDATE_ALREADY_REVIEWED","Withdrawal"=>"LIBRARY_WITHDRAWN","Role"=>"JOB_REQUESTER_FORBIDDEN","Retry"=>"JOB_RETRY_AUTHORIZATION_MISSING",_=>"unexpected"};Check(e.Code==expected,"Wrong second-check failure "+change+": "+e.Code);}
                await tx.CommitAsync();
            }
            try{await db.Set<BuilderSemanticPreparation>().Where(s=>s.Id==p.Id).ExecuteUpdateAsync(u=>u.SetProperty(s=>s.Snapshot,"{}"));throw new Exception("Semantic snapshot overwritten");}catch(Npgsql.PostgresException e){Check(e.SqlState=="23514","Wrong immutable snapshot rejection");}
            Check(await db.Set<BuilderCall>().CountAsync(c=>c.FamilyId==p.FamilyId)==0 && await db.Set<MappingModelCall>().CountAsync(c=>c.FamilyId==p.FamilyId)==0,"Preparation fabricated physical call");
        }
        var first=saved[0];var second=saved[1];await using(var check=BackgroundJobs.Open(owner))
        {
            var job=new BackgroundJob{FamilyId=first.Actor.FamilyId,Type=BuilderSemanticJobs.Type,InputRef=Guid.NewGuid(),IdempotencyKey="cross-family-fixture",InputPayload="{}",InputHash=Content.Hash("{}")};check.Add(job);await check.SaveChangesAsync();
            check.Add(new BuilderSemanticPreparation{FamilyId=first.Actor.FamilyId,CandidateId=second.Preparation.CandidateId,RunId=first.Preparation.RunId,JobId=job.Id,RequestedBy=first.Actor.Id,LibraryReleaseId=first.Preparation.LibraryReleaseId,Snapshot="{}",SnapshotHash=Content.Hash("{}"),InputHash=Content.Hash("cross-family")});
            try{await check.SaveChangesAsync();throw new Exception("Cross-family semantic candidate accepted");}catch(DbUpdateException e)when(e.InnerException is Npgsql.PostgresException pg){Check(pg.SqlState=="23503","Wrong cross-family rejection");}check.ChangeTracker.Clear();
            await check.Families.Where(f=>f.Id==first.Actor.FamilyId).ExecuteDeleteAsync();Check(!await check.Set<BuilderSemanticPreparation>().AnyAsync(p=>p.FamilyId==first.Actor.FamilyId),"Family deletion left semantic snapshot");
        }
        Console.WriteLine("PASS actual PostgreSQL semantic preparation/idempotency and immutable snapshot; two short checks retain frozen model, reject changed candidate/source/review/library/permission or unaudited retry; empty library remains explicit, cross-family FK/cascade deletion enforced, no provider calls");
    }
}
