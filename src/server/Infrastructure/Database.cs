using Microsoft.EntityFrameworkCore;

namespace Learning;
public class Database(DbContextOptions<Database> options) : DbContext(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Availability> Availabilities => Set<Availability>();
    public DbSet<Progress> Progresses => Set<Progress>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<ContentDraft> Drafts => Set<ContentDraft>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanRevision> PlanRevisions => Set<PlanRevision>();
    public DbSet<StudyTask> Tasks => Set<StudyTask>();
    public DbSet<Placement> Placements => Set<Placement>();
    public DbSet<LearningSession> Sessions => Set<LearningSession>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<Grading> Gradings => Set<Grading>();
    public DbSet<Outbox> Outbox => Set<Outbox>();
    public DbSet<Generation> Generations => Set<Generation>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<Mastery> Masteries => Set<Mastery>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<BuilderRun> BuilderRuns => Set<BuilderRun>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Audit> Audits => Set<Audit>();
    public DbSet<CommandRecord> Commands => Set<CommandRecord>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        // Each private entity has a concrete table and a mandatory family foreign key.
        foreach (var t in typeof(Row).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Row))))
        {
            var e = b.Entity(t);
            e.HasBaseType((Type?)null);
            e.HasKey("Id");
            e.HasOne(typeof(Family)).WithMany().HasForeignKey("FamilyId").OnDelete(DeleteBehavior.Cascade);
            e.HasIndex("FamilyId");
        }
        b.Entity<Account>().HasIndex(x => x.UserName).IsUnique();
        b.Entity<AuthSession>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<Availability>().HasIndex(x => new { x.StudentId, x.Date }).IsUnique();
        b.Entity<Progress>().HasIndex(x => new { x.StudentId, x.Date, x.LessonId }).IsUnique();
        b.Entity<Plan>().HasIndex(x => new { x.StudentId, x.Date }).IsUnique();
        b.Entity<PlanRevision>().Property(x=>x.RuleVersion).HasDefaultValue("legacy/unknown");
        b.Entity<PlanRevision>().HasIndex(x => new { x.PlanId, x.Number }).IsUnique();
        b.Entity<Placement>().HasIndex(x => new { x.RevisionId, x.TaskId }).IsUnique();
        b.Entity<Attempt>().HasIndex(x => new { x.StudentId, x.ClientSubmissionId }).IsUnique();
        b.Entity<Attempt>().HasIndex(x => new { x.SessionId, x.Number }).IsUnique();
        b.Entity<Attempt>().Property(x => x.Sequence).UseIdentityAlwaysColumn();
        b.Entity<Grading>().HasIndex(x => new { x.AttemptId, x.Number }).IsUnique();
        b.Entity<Evidence>().HasIndex(x => new { x.GenerationId, x.AttemptId, x.KCId, x.Part }).IsUnique();
        b.Entity<Mastery>().HasIndex(x => new { x.GenerationId, x.KCId }).IsUnique();
        b.Entity<Review>().HasIndex(x => new { x.GenerationId, x.TargetType, x.TargetId }).IsUnique();
        b.Entity<CommandRecord>().HasIndex(x => new { x.FamilyId, x.ActorId, x.Scope, x.Key }).IsUnique();
        b.Entity<Source>().HasIndex(x => new { x.FamilyId, x.Hash }).IsUnique();
        Foreign<Account, FamilyMembership>(b,"AccountId");
        b.Entity<FamilyMembership>().HasIndex(m=>new {m.FamilyId,m.AccountId}).IsUnique();
        b.Entity<Family>().HasOne<Account>().WithMany().HasForeignKey(f=>new {f.Id,f.OwnerAccountId}).HasPrincipalKey(a=>new {a.FamilyId,a.Id}).OnDelete(DeleteBehavior.Restrict);
        Foreign<Student, Audit>(b,"StudentId");
        Foreign<Student, ParentBurdenRecord>(b,"StudentId");
        Foreign<ParentBurdenRecord, ParentBurdenRecord>(b,"SupersedesId");
        b.Entity<ParentBurdenRecord>().HasIndex(r=>r.SupersedesId).IsUnique();
        b.Entity<ParentBurdenRecord>().HasIndex(r=>new{r.StudentId,r.Date});
        Foreign<Student, Goal>(b, "StudentId"); Foreign<Student, GoalChange>(b,"StudentId"); Foreign<Goal, GoalChange>(b,"GoalId");
        b.Entity<Goal>().Property(g=>g.Subject).HasDefaultValue("Unspecified");
        b.Entity<Goal>().Property(g=>g.GoalType).HasDefaultValue("Activity");
        b.Entity<Goal>().Property(g=>g.Period).HasDefaultValue("Daily");
        b.Entity<Goal>().Property(g=>g.ScheduleRule).HasDefaultValue("[1,2,3,4,5,6,0]");
        b.Entity<Goal>().Property(g=>g.TargetValue).HasDefaultValue(1);
        b.Entity<Goal>().Property(g=>g.Priority).HasDefaultValue(3);
        b.Entity<Goal>().Property(g=>g.Version).HasDefaultValue(1L);
        b.Entity<StudyTask>().Property(t=>t.GoalSnapshots).HasDefaultValue("[]");
        b.Entity<GoalChange>().HasIndex(x=>new{x.GoalId,x.Version}).IsUnique(); Foreign<Student, Availability>(b, "StudentId");
        Foreign<Student, Progress>(b, "StudentId"); Foreign<Release, Progress>(b,"ReleaseId"); Foreign<Student, ProgressChange>(b,"StudentId"); Foreign<Student, Plan>(b, "StudentId");
        Foreign<Student, StudyTask>(b, "StudentId"); Foreign<Student, LearningSession>(b, "StudentId");
        Foreign<Plan, PlanRevision>(b, "PlanId"); Foreign<PlanRevision, Placement>(b, "RevisionId");
        Foreign<StudyTask, Placement>(b, "TaskId"); Foreign<StudyTask, LearningSession>(b, "TaskId");
        Foreign<Release, LearningSession>(b, "ReleaseId"); Foreign<Release, StudyTask>(b, "ReleaseId");
        Foreign<LearningSession, Attempt>(b, "SessionId"); Foreign<Attempt, Grading>(b, "AttemptId");
        Foreign<Attempt, Outbox>(b, "AttemptId"); Foreign<Generation, Evidence>(b, "GenerationId");
        Foreign<Generation, Mastery>(b, "GenerationId"); Foreign<Generation, Review>(b, "GenerationId");
        Foreign<Attempt, Evidence>(b, "AttemptId"); Foreign<Grading, Evidence>(b, "GradingId");
        Foreign<Source, Chunk>(b, "SourceId"); Foreign<Source, BuilderRun>(b, "SourceId");
        Foreign<ContentDraft, ContentReviewRecord>(b,"DraftId"); Foreign<Account, ContentReviewRecord>(b,"ReviewerId"); Foreign<Release, ContentReviewRecord>(b,"PublishedReleaseId");
        b.Entity<ContentReviewRecord>().HasIndex(r=>new{r.DraftId,r.DraftVersion});
        Foreign<ContentDraft, MappingRun>(b,"SourceDraftId"); Foreign<Release, MappingRun>(b,"LibraryReleaseId");
        b.Entity<MappingRun>().HasIndex(r=>new{r.FamilyId,r.InputHash}).IsUnique();
        Foreign<MappingRun, MappingSuggestion>(b,"RunId");
        b.Entity<MappingSuggestion>().HasIndex(s=>new{s.RunId,s.OwnerType,s.OwnerId}).IsUnique();
        Foreign<MappingSuggestion, MappingReviewDecision>(b,"SuggestionId"); Foreign<Account, MappingReviewDecision>(b,"ReviewerId"); Foreign<ContentDraft, MappingReviewDecision>(b,"CreatedDraftId");
        b.Entity<MappingReviewDecision>().HasIndex(d=>d.SuggestionId).IsUnique();
        Foreign<ContentDraft, MappingSetRevision>(b,"DraftId"); Foreign<MappingReviewDecision, MappingSetRevision>(b,"ReviewDecisionId"); Foreign<ContentReviewRecord, MappingSetRevision>(b,"ContentReviewRecordId");
        b.Entity<MappingSetRevision>().Property(s=>s.CoverageOrigin).HasDefaultValue("HumanReviewed");
        b.Entity<MappingSetRevision>().ToTable(t=>t.HasCheckConstraint("CK_MappingSetRevision_ReviewSource","(\"ReviewDecisionId\" IS NOT NULL) <> (\"ContentReviewRecordId\" IS NOT NULL)"));
        b.Entity<MappingSetRevision>().HasIndex(s=>new{s.FamilyId,s.OwnerType,s.OwnerRevisionId});
        Foreign<Release, ReleaseMappingSet>(b,"ReleaseId"); Foreign<MappingSetRevision, ReleaseMappingSet>(b,"SetRevisionId");
        b.Entity<ReleaseMappingSet>().HasIndex(s=>new{s.ReleaseId,s.OwnerType,s.OwnerId}).IsUnique();
        Foreign<MappingSetRevision, MappingSetItem>(b,"SetRevisionId"); Foreign<ContentIdentity, MappingSetItem>(b,"KCId"); Foreign<ContentRevision, MappingSetItem>(b,"KCRevisionId");
        b.Entity<MappingSetItem>().HasIndex(i=>new{i.SetRevisionId,i.Sequence}).IsUnique();
        Foreign<BuilderRun, BuilderAttempt>(b,"RunId");b.Entity<BuilderAttempt>().HasIndex(a=>new{a.RunId,a.RetryRound,a.Number}).IsUnique();
        Foreign<Release, BuilderRun>(b,"LibraryReleaseId"); Foreign<ContentDraft, Candidate>(b,"CreatedDraftId"); Foreign<BuilderRun, Candidate>(b, "RunId"); Foreign<Chunk, Candidate>(b, "ChunkId");
        b.Entity<ContentIdentity>().HasIndex(x => new {x.FamilyId,x.EntityType,x.Code}).IsUnique();
        b.Entity<ReleaseItem>().HasIndex(x => new {x.ReleaseId,x.EntityType,x.IdentityId}).IsUnique();
        Foreign<ContentIdentity, ContentRevision>(b,"IdentityId");
        Foreign<Release, ReleaseItem>(b,"ReleaseId");
        Foreign<ContentIdentity, ReleaseItem>(b,"IdentityId");
        Foreign<ContentRevision, ReleaseItem>(b,"RevisionId");
        Foreign<Student, Attempt>(b,"StudentId"); Foreign<Student, Generation>(b,"StudentId");
        Foreign<Student, Outbox>(b,"StudentId"); Foreign<Student, Review>(b,"StudentId");
        Foreign<Student, Mastery>(b,"StudentId"); Foreign<Student, Evidence>(b,"StudentId");
        Foreign<Student, PaperWrong>(b,"StudentId"); Foreign<PrivateFile, PaperWrong>(b,"FileId"); Foreign<ContentDraft, PaperWrong>(b,"DraftId"); Foreign<Release, PaperWrong>(b,"ReleaseId"); Foreign<Attempt, PaperWrong>(b,"AttemptId");
        b.Entity<PaperWrong>().HasIndex(x=>x.AttemptId).IsUnique();
        b.Entity<Embedding>().HasIndex(e=>new {e.FamilyId,e.EntityRevisionId,e.Space}).IsUnique();
        b.Entity<Alias>().HasIndex(a=>new {a.FamilyId,a.KCId,a.Normalized}).IsUnique();
        Foreign<Release, KCChangeProposal>(b,"FromReleaseId"); Foreign<Release, KCChangeProposal>(b,"EffectiveReleaseId");
        Foreign<KCChangeProposal, KCChangeProposalItem>(b,"ProposalId"); Foreign<ContentIdentity, KCChangeProposalItem>(b,"KCId"); Foreign<ContentRevision, KCChangeProposalItem>(b,"ProposedRevisionId");
        Foreign<KCChangeProposal, KCProposalEvent>(b,"ProposalId"); b.Entity<KCProposalEvent>().HasIndex(e=>new{e.ProposalId,e.Version}).IsUnique();
        b.Entity<KCChangeProposalItem>().HasIndex(i=>new{i.ProposalId,i.Side,i.KCId}).IsUnique();
        Foreign<KCChangeProposal, KnowledgeMigration>(b,"ProposalId"); Foreign<ContentIdentity, KnowledgeMigration>(b,"FromKCId"); Foreign<ContentIdentity, KnowledgeMigration>(b,"ToKCId");
        Foreign<ContentRevision, KnowledgeMigration>(b,"FromRevisionId"); Foreign<ContentRevision, KnowledgeMigration>(b,"ToRevisionId"); Foreign<Release, KnowledgeMigration>(b,"EffectiveReleaseId");
        b.Entity<KnowledgeMigration>().HasIndex(m=>new{m.ProposalId,m.FromKCId,m.ToKCId}).IsUnique();
        Foreign<ContentIdentity, Alias>(b,"KCId"); Foreign<Candidate, Alias>(b,"CandidateId");
        Foreign<PrivateFile, Source>(b,"FileId");
        Foreign<Student, CorrectionBatch>(b,"StudentId"); Foreign<Release, CorrectionBatch>(b,"ReleaseId");
        Foreign<CorrectionBatch, CorrectionItem>(b,"BatchId"); Foreign<Attempt, CorrectionItem>(b,"AttemptId");
        Foreign<Release, CorrectionItem>(b,"MappingReleaseId");
        b.Entity<CorrectionItem>().HasIndex(i=>new {i.BatchId,i.AttemptId}).IsUnique();
        b.Entity<CorrectionItem>().Property(i=>i.Sequence).UseIdentityAlwaysColumn();
        foreach (var type in b.Model.GetEntityTypes())
            foreach (var p in type.GetProperties().Where(p => p.ClrType == typeof(decimal))) p.SetPrecision(16);
        foreach (var type in b.Model.GetEntityTypes())
            foreach (var p in type.GetProperties().Where(p => p.ClrType == typeof(decimal))) p.SetScale(6);
    }
    static void Foreign<TParent, TChild>(ModelBuilder b, string fk) where TParent : Row where TChild : Row
    {
        b.Entity<TParent>().HasAlternateKey(x => new { x.FamilyId, x.Id });
        b.Entity<TChild>().HasOne<TParent>().WithMany().HasForeignKey("FamilyId", fk)
            .HasPrincipalKey(x => new { x.FamilyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
    public async Task Lock(Guid familyId, CancellationToken ct = default)
        => await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({familyId.ToString()}, 0))", ct);
}
