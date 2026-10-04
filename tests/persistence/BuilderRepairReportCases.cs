using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

public static class BuilderRepairReportCases
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task Run(Database db)
    {
        await db.Database.MigrateAsync();
        var family=new Family();var account=new Account{FamilyId=family.Id,UserName=Environment.GetEnvironmentVariable("REPAIR_REPORT_USER")??throw new Exception("Fixture user required"),PasswordHash=Security.Password(Environment.GetEnvironmentVariable("REPAIR_REPORT_PASSWORD")??throw new Exception("Fixture password required"))};
        db.AddRange(family,account,new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles="Parent,ContentEditor"});await db.SaveChangesAsync();
        var config=BuilderConfiguration.Current(new ConfigurationBuilder().Build());var configPayload=Json.Write(config);var configHash=Content.Hash(configPayload);
        async Task<BuilderRun> Prepare(string text,int repairAttempts=1)
        {
            var frozen=config with{Limits=config.Limits with{RepairAttempts=repairAttempts}};var frozenPayload=Json.Write(frozen);var frozenHash=Content.Hash(frozenPayload);
            var source=new Source{FamilyId=family.Id,Title="受控修复流程夹具，非模型效果",Text=text,Hash=Content.Hash(text),UsageScope="原创受控结构测试，不用于教材金标准"};var chunk=new Chunk{FamilyId=family.Id,SourceId=source.Id,Text=text,Locator="段落 1"};
            var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,InputVersion="builder-input/4",PromptVersion=config.PromptVersion,ModelConfigPayload=frozenPayload,ModelConfigHash=frozenHash,InputHash=Content.Hash(source.Hash+":Mock:fixture/1:"+config.PromptVersion+":builder-input/4::"+frozenHash)};
            db.AddRange(source,chunk,run);await db.SaveChangesAsync();return run;
        }
        var success=await Prepare("先乘除后加减。受控结构修复成功夹具。");var goodProvider=new Controlled(false);await Builder.ProcessOne(db,default,provider:goodProvider);db.ChangeTracker.Clear();success=await db.BuilderRuns.SingleAsync(r=>r.Id==success.Id);
        Check(success.Status=="Completed" && goodProvider.Calls==2,"repair did not complete in exactly two calls");var goodAttempt=await db.Set<BuilderAttempt>().SingleAsync(a=>a.RunId==success.Id);var protocol=Json.Read<BuilderProtocolResult>(goodAttempt.ProtocolResult!);Check(protocol.Repaired && protocol.Calls==2 && protocol.Output.SchemaVersion=="kc-candidate/2","persisted repair facts missing");
        var failed=await Prepare("先乘除后加减。受控结构修复仍失败夹具。");var badProvider=new Controlled(true);await Builder.ProcessOne(db,default,provider:badProvider);db.ChangeTracker.Clear();failed=await db.BuilderRuns.SingleAsync(r=>r.Id==failed.Id);
        Check(failed.Status=="Failed" && failed.Error=="BUILDER_NEEDS_REPAIR" && failed.Retries==0 && badProvider.Calls==2,"failed repair retried or exceeded two calls");Check(!await db.Candidates.AnyAsync(c=>c.RunId==failed.Id) && (await db.Set<BuilderAttempt>().SingleAsync(a=>a.RunId==failed.Id)).ProtocolResult==null,"failed repair retained candidate/protocol output");
        var candidates=await db.Candidates.Where(c=>c.RunId==success.Id).ToArrayAsync();Check(candidates.Length==1 && candidates[0].ProtocolPayload==Json.Write(protocol.Output.Candidates.Single()),"successful fixed candidate differs from repaired output");
        var disabled=await Prepare("先乘除后加减。受控冻结配置禁用修复夹具。",0);var disabledProvider=new Controlled(false);await Builder.ProcessOne(db,default,provider:disabledProvider);db.ChangeTracker.Clear();disabled=await db.BuilderRuns.SingleAsync(r=>r.Id==disabled.Id);
        Check(disabled.Status=="Failed" && disabled.Error=="BUILDER_NEEDS_REPAIR" && disabledProvider.Calls==1 && !await db.Candidates.AnyAsync(c=>c.RunId==disabled.Id),"frozen zero-repair configuration was bypassed");Check(Json.Read<BuilderModelConfiguration>(disabled.ModelConfigPayload!).Limits.RepairAttempts==0 && disabled.ModelConfigHash!=configHash,"disabled repair lost independent fixed config");
        foreach(var run in new[]{success,failed,disabled})
        {
            var calls=await db.Set<BuilderCall>().Where(c=>c.RunId==run.Id).OrderBy(c=>c.CallNumber).ToArrayAsync();var count=run.Id==disabled.Id?1:2;Check(calls.Length==count && calls.Select(c=>c.ExecutionId).Distinct().Count()==1 && calls.Select(c=>c.CallNumber).SequenceEqual(Enumerable.Range(1,count)) && !calls[0].Repair && (count==1 || calls[1].Repair),"repair call ledger linkage invalid");Check(calls.All(c=>c.Status=="Returned" && c.BillingStatus=="LocalNoCharge" && c.ChargedCost==0 && c.BudgetState=="Settled" && c.ModelConfigHash==run.ModelConfigHash && c.InputHash==run.InputHash && c.OutputHash!=null),"returned local ledger or frozen config missing");Check(calls[0].OutputHash==Content.Hash("{}"),"first invalid response hash lost");Check(Content.Hash(run.ModelConfigPayload!)==run.ModelConfigHash,"repair changed fixed model configuration");
        }
        var before=Json.Write(await db.Candidates.OrderBy(c=>c.Id).ToArrayAsync());await Builder.ProcessOne(db,default,provider:goodProvider);db.ChangeTracker.Clear();Check(goodProvider.Calls==2 && await db.Set<BuilderCall>().CountAsync()==5 && await db.Set<BuilderAttempt>().CountAsync()==3 && Json.Write(await db.Candidates.OrderBy(c=>c.Id).ToArrayAsync())==before,"repeated worker processing changed terminal repair records");
        Check(!await db.Evidence.AnyAsync() && !await db.Masteries.AnyAsync(),"candidate repair created learning evidence");
        Console.WriteLine(Json.Write(new{userName=account.UserName,successRunId=success.Id,failedRunId=failed.Id,disabledRunId=disabled.Id,candidateId=candidates.Single().Id}));
    }
    sealed class Controlled(bool alwaysInvalid):IBuilderCandidateProvider
    {
        public int Calls{get;private set;}
        public BuilderQuote Quote(BuilderProviderRequest request)=>BuilderQuote.Local;
        public Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct)
        {
            Calls++;Check(request.Schema==BuilderProtocol.SchemaV2,"repair schema changed");
            if(Calls==1)Check(request.InvalidOutput==null && request.ValidationCode==null,"first call marked repair");
            else Check(Calls==2 && request.InvalidOutput=="{}" && request.ValidationCode=="BUILDER_SCHEMA_INVALID","repair lost original invalid output or validation code");
            var fragment=request.Fragments.Single();var output=alwaysInvalid || Calls==1?"{}":Json.Write(new BuilderEnvelope("kc-candidate/2",[new BuilderCandidateOutput("受控修复后的能力","MATH","Procedure",3,3,"独立按顺序计算","不含未观察的建模能力",[fragment.Id],[fragment.Text],0)]));
            return Task.FromResult(new BuilderProviderResponse(output,new BuilderUsage(null,null,0,null,"LocalNoCharge")));
        }
    }
}
