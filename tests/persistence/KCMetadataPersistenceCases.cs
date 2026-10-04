using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
public static class KCMetadataPersistenceCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static async Task Run(Database db)
    {
        await db.Database.MigrateAsync();var family=new Family();var account=new Account{FamilyId=family.Id,UserName="metadata-"+Guid.NewGuid()};db.AddRange(family,account,new FamilyMembership{FamilyId=family.Id,AccountId=account.Id,Roles="Parent,ContentEditor,Publisher"});await db.SaveChangesAsync();var actor=new Actor(account.Id,family.Id,account.Id,null,"Parent","Parent,ContentEditor,Publisher");
        var legacy=Content.Fixture();legacy=legacy with{Kcs=legacy.Kcs.Select(k=>k with{Subject=null,GradeMin=null,GradeMax=null}).ToArray()};
        async Task<Release> Publish(Catalog c,int number)
        {
            var draft=new ContentDraft{FamilyId=family.Id,Title="隔离元数据审核夹具",Payload=Json.Write(c)};db.Add(draft);await db.SaveChangesAsync();await ContentReviews.Approve(db,actor,draft,new(draft.Version,"受控学科年级字段核对"));await db.SaveChangesAsync();var review=await ContentReviews.ForPublish(db,draft);var release=new Release{FamilyId=family.Id,Number=number,Payload=draft.Payload,Hash=Content.Hash(draft.Payload)};db.Add(release);await Publishing.Register(db,release,review);await db.SaveChangesAsync();return release;
        }
        var first=await Publish(legacy,1);var original=first.Payload;var originalRevisions=await db.Set<ContentRevision>().AsNoTracking().Where(r=>r.FamilyId==family.Id).OrderBy(r=>r.Id).ToArrayAsync();var revisionIds=originalRevisions.Select(r=>r.Id).ToArray();var definitions=Json.Write(originalRevisions);var profile=BuilderConfiguration.Current(new ConfigurationBuilder().Build());
        var text="独立计算混合运算，核对两步计算。";var source=new Source{FamilyId=family.Id,Title="元数据受控来源",Text=text,Hash=Content.Hash(text)};db.AddRange(source,new Chunk{FamilyId=family.Id,SourceId=source.Id,Locator="段落1",Text=text});await db.SaveChangesAsync();
        async Task<BuilderRun> Prepare(Release release,BuilderModelConfiguration config)
        {var payload=Json.Write(config);var hash=Content.Hash(payload);var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,LibraryReleaseId=release.Id,PromptVersion=config.PromptVersion,InputVersion="builder-input/4",ModelConfigPayload=payload,ModelConfigHash=hash,InputHash=Content.Hash(source.Hash+":Mock:fixture/1:"+config.PromptVersion+":builder-input/4:"+release.Hash+":"+hash)};db.Add(run);await db.SaveChangesAsync();return run;}
        var unknown=await Prepare(first,profile);var old=await Prepare(first,profile with{Retrieval=profile.Retrieval! with{Version="builder-retrieval/3",SubjectPolicy=null,GradePolicy=null}});await Builder.ProcessOne(db,default);await Builder.ProcessOne(db,default);db.ChangeTracker.Clear();Check(Json.Read<Match[]>((await db.Candidates.SingleAsync(c=>c.RunId==unknown.Id)).Matches).Length==0 && Json.Read<Match[]>((await db.Candidates.SingleAsync(c=>c.RunId==old.Id)).Matches).Length>0,"new unknown subject or old profile reinterpreted");
        var known=legacy with{Kcs=legacy.Kcs.Select(k=>k with{RevisionId=Guid.NewGuid(),Subject="MATH",GradeMin=10,GradeMax=12,Domain="数与运算",DifficultyLevel="Hard",CognitiveLevel="理解与应用"}).ToArray()};var second=await Publish(known,2);var frozen=await Prepare(second,profile);var frozenPayload=frozen.ModelConfigPayload;
        var later=known with{Kcs=known.Kcs.Select(k=>k with{RevisionId=Guid.NewGuid(),GradeMin=2,GradeMax=6,Domain="数学运算",DifficultyLevel="Easy",CognitiveLevel="独立操作"}).ToArray()};await Publish(later,3);await Builder.ProcessOne(db,default);db.ChangeTracker.Clear();var candidate=await db.Candidates.SingleAsync(c=>c.RunId==frozen.Id);var context=await BuilderCandidateReviews.Read(db,actor,candidate.Id);Check(context.Matches.Length>0 && context.Matches.All(m=>m.Definition!.Subject=="MATH" && m.Definition.GradeMin==10 && m.Definition.GradeMax==12 && m.Definition.Domain=="数与运算" && m.Definition.DifficultyLevel=="Hard" && m.Definition.CognitiveLevel=="理解与应用") && context.LibraryNumber==2 && (await db.BuilderRuns.SingleAsync(r=>r.Id==frozen.Id)).ModelConfigPayload==frozenPayload,"later grade revision replaced frozen library or cross-grade was excluded");
        Check((await db.Releases.SingleAsync(r=>r.Id==first.Id)).Payload==original && Json.Write(await db.Set<ContentRevision>().AsNoTracking().Where(r=>revisionIds.Contains(r.Id)).OrderBy(r=>r.Id).ToArrayAsync())==definitions,"original definitions changed");
        foreach(var subject in new string?[]{"ENGLISH",null})
        {
            await using var tx=await db.Database.BeginTransactionAsync();var changed=later with{Kcs=later.Kcs.Select(k=>k with{RevisionId=Guid.NewGuid(),Subject=subject,GradeMin=subject==null?null:k.GradeMin,GradeMax=subject==null?null:k.GradeMax}).ToArray()};
            try{await Publish(changed,4);throw new Exception("recorded subject changed");}catch(ApiError e){Check(e.Code=="KC_SUBJECT_CHANGED","wrong subject identity rejection");}await tx.RollbackAsync();db.ChangeTracker.Clear();
        }
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            var overwritten=later with{Kcs=later.Kcs.Select(k=>k with{Domain="试图覆盖旧修订"}).ToArray()};
            try{await Publish(overwritten,4);throw new Exception("descriptor revision overwritten");}catch(ApiError e){Check(e.Code=="REVISION_IMMUTABLE","wrong descriptor revision rejection");}await tx.RollbackAsync();db.ChangeTracker.Clear();
        }
        Check(await db.Releases.CountAsync()==3 && !await db.Evidence.AnyAsync(),"metadata edit copied evidence or committed rejected release");await db.Families.Where(f=>f.Id==family.Id).ExecuteDeleteAsync();Check(!await db.Set<ContentRevision>().AnyAsync() && !await db.Candidates.AnyAsync(),"metadata cleanup failed");
        Console.WriteLine("PASS 真实发布旧定义不回填，新学科年级及领域/复杂度/认知描述经审核新修订且旧修订不可覆盖；旧retrieval/3保持，新未知学科零匹配；跨年级固定正式库及原描述不被后发布替换；已记录学科更换/清空拒绝、零证据复制、整家庭清理");
    }
}
