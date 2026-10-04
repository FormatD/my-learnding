using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
public static class BuilderProtocolV2PersistenceCases
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task Run(Database db,string mode="builder-v2")
    {
        await db.Database.MigrateAsync();var family=new Family();var account=new Account{FamilyId=family.Id,UserName=Environment.GetEnvironmentVariable("V2_USER")??"controlled-v2-"+Guid.NewGuid(),PasswordHash=Security.Password(Environment.GetEnvironmentVariable("V2_PASSWORD")??"controlled-v2-private-fixture")};db.AddRange(family,account,new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles="Parent,ContentEditor,Publisher"});await db.SaveChangesAsync();var actor=new Actor(account.Id,family.Id,account.Id,null,"Parent","Parent,ContentEditor,Publisher");
        var catalog=Content.Fixture();catalog=catalog with{Kcs=catalog.Kcs.Select((k,i)=>k with{Type=i==0?"Strategy":i==1?"Expression":k.Type}).ToArray()};Check(Content.Validate(catalog).Length==0,"controlled formal library invalid");var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};db.Add(release);await Publishing.Register(db,release);await db.SaveChangesAsync();
        var text="独立选择运算策略并写清表达思路。";var source=new Source{FamilyId=family.Id,Title="受控提供者结构夹具，非模型质量证据",Text=text,Hash=Content.Hash(text)};var chunk=new Chunk{FamilyId=family.Id,SourceId=source.Id,Text=text,Locator="段落1"};db.AddRange(source,chunk);await db.SaveChangesAsync();
        async Task<BuilderRun> Prepare(BuilderModelConfiguration config)
        {
            var payload=Json.Write(config);var hash=Content.Hash(payload);var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,LibraryReleaseId=release.Id,InputVersion="builder-input/4",PromptVersion=config.PromptVersion,ModelConfigPayload=payload,ModelConfigHash=hash,InputHash=Content.Hash(source.Hash+":Mock:fixture/1:"+config.PromptVersion+":builder-input/4:"+release.Hash+":"+hash)};db.Add(run);await db.SaveChangesAsync();return run;
        }
        var current=BuilderConfiguration.Current(new ConfigurationBuilder().Build());var oldConfig=current with{Version="builder-config/2",PromptVersion="kc-candidate/1",SchemaHash=Content.Hash(BuilderProtocol.Schema)};var old=await Prepare(oldConfig);var modern=await Prepare(current);var oldPayload=old.ModelConfigPayload;var newPayload=modern.ModelConfigPayload;
        var provider=new Controlled();await Builder.ProcessOne(db,default,provider:provider);await Builder.ProcessOne(db,default,provider:provider);db.ChangeTracker.Clear();old=await db.BuilderRuns.SingleAsync(r=>r.Id==old.Id);modern=await db.BuilderRuns.SingleAsync(r=>r.Id==modern.Id);Check(old.Status=="Failed" && old.Error=="BUILDER_NEEDS_REPAIR" && modern.Status=="Completed","old/new output gates not independent");Check(old.ModelConfigPayload==oldPayload && modern.ModelConfigPayload==newPayload,"worker rewrote fixed configuration");
        Check(!await db.Candidates.AnyAsync(c=>c.RunId==old.Id),"old run retained partial output");var rows=await db.Candidates.Where(c=>c.RunId==modern.Id).OrderBy(c=>c.Type).ToArrayAsync();Check(rows.Select(c=>c.Type).SequenceEqual(new[]{"Expression","Strategy"}),"new output types not retained");
        foreach(var row in rows){var context=await BuilderCandidateReviews.Read(db,actor,row.Id);Check(context.Protocol!.KcType==row.Type && context.Matches.Length>0 && context.Matches.All(m=>m.Definition!.Type==row.Type),"same-type frozen retrieval or review lost output");}
        var damaged=rows[0];await db.Candidates.Where(c=>c.Id==damaged.Id).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.Type,"Procedure"));db.ChangeTracker.Clear();try{await BuilderCandidateReviews.Read(db,actor,damaged.Id);throw new Exception("modern fields mixed with another type");}catch(ApiError e){Check(e.Code=="CANDIDATE_PROTOCOL_UNKNOWN","wrong modern inconsistency rejection");}await db.Candidates.Where(c=>c.Id==damaged.Id).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.Type,damaged.Type));db.ChangeTracker.Clear();
        var attempt=await db.Set<BuilderAttempt>().SingleAsync(a=>a.RunId==modern.Id);Check(Json.Read<BuilderProtocolResult>(attempt.ProtocolResult!).Output.SchemaVersion=="kc-candidate/2","attempt lost fixed output version");var calls=await db.Set<BuilderCall>().Where(c=>c.RunId==modern.Id).ToArrayAsync();Check(calls.Length==1 && calls[0].ModelConfigHash==modern.ModelConfigHash && calls[0].BillingStatus=="LocalNoCharge","actual controlled call not tracked");
        var saved=Json.Write(rows);await Builder.ProcessOne(db,default,provider:provider);db.ChangeTracker.Clear();Check(Json.Write(await db.Candidates.Where(c=>c.RunId==modern.Id).OrderBy(c=>c.Type).ToArrayAsync())==saved,"repeat mutated outputs");
        if(mode=="builder-v2-seed"){Console.WriteLine(Json.Write(new{userName=account.UserName,runId=modern.Id,oldRunId=old.Id,candidates=rows.Select(r=>new{r.Id,r.Type,target=catalog.Kcs.First(k=>k.Type==r.Type).Id})}));return;}
        await db.Families.Where(f=>f.Id==family.Id).ExecuteDeleteAsync();Check(!await db.Candidates.AnyAsync() && !await db.BuilderRuns.AnyAsync() && !await db.Set<BuilderCall>().AnyAsync(),"family cleanup left v2 output");Console.WriteLine("PASS 受控提供者经真实领取/调用账本/协议/检索/提交：旧v1拒绝新类型且零候选，新v2固定两类型与同类型正式库、原配置与输出版本保持、重复消费不改及家庭删除；非真实模型质量证据");
    }
    sealed class Controlled:IBuilderCandidateProvider
    {
        public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
        public Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct)
        {
            var version=request.Schema==BuilderProtocol.Schema?"kc-candidate/1":"kc-candidate/2";var fragment=request.Fragments.Single();var output=new BuilderEnvelope(version,new[]{"Strategy","Expression"}.Select(type=>new BuilderCandidateOutput("受控"+type,"MATH",type,3,3,"明确受控结构输出，不代替语义核对","不含未观察行为",[fragment.Id],[fragment.Text],0)).ToArray());return Task.FromResult(new BuilderProviderResponse(Json.Write(output),new BuilderUsage(null,null,0,null,"LocalNoCharge")));
        }
    }
}
