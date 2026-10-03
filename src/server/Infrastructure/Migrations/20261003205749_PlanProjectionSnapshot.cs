using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanProjectionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProjectionSnapshot",
                table: "PlanRevisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectionSnapshotHash",
                table: "PlanRevisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlanRevision_ProjectionSnapshot",
                table: "PlanRevisions",
                sql: "(\"ProjectionSnapshot\" IS NULL AND \"ProjectionSnapshotHash\" IS NULL) OR (\"ProjectionSnapshot\" IS NOT NULL AND length(\"ProjectionSnapshotHash\")=64 AND \"ProjectionSnapshotHash\" IS NOT NULL)");
            migrationBuilder.Sql("""
CREATE FUNCTION learning_plan_projection_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
DECLARE snapshot jsonb;
BEGIN
 IF TG_OP='UPDATE' THEN
  IF NEW."Id" IS DISTINCT FROM OLD."Id" OR NEW."FamilyId" IS DISTINCT FROM OLD."FamilyId" OR NEW."PlanId" IS DISTINCT FROM OLD."PlanId" OR NEW."ProjectionSnapshot" IS DISTINCT FROM OLD."ProjectionSnapshot" OR NEW."ProjectionSnapshotHash" IS DISTINCT FROM OLD."ProjectionSnapshotHash" THEN
   RAISE EXCEPTION 'Plan projection provenance is immutable, including unknown legacy values' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
 END IF;
 IF NEW."ProjectionSnapshot" IS NULL THEN RETURN NEW; END IF;
 snapshot=NEW."ProjectionSnapshot"::jsonb;
 IF NEW."ProjectionSnapshotHash" IS DISTINCT FROM encode(sha256(convert_to(NEW."ProjectionSnapshot",'UTF8')),'hex') OR snapshot->>'version' IS DISTINCT FROM 'plan-projection/1' OR snapshot->>'familyId' IS DISTINCT FROM NEW."FamilyId"::text OR NOT EXISTS(SELECT 1 FROM "Plans" p WHERE p."Id"=NEW."PlanId" AND p."FamilyId"=NEW."FamilyId" AND p."StudentId"::text=snapshot->>'studentId') THEN
  RAISE EXCEPTION 'Plan projection requires its original payload and owned learner' USING ERRCODE='23514';
 END IF;
 IF snapshot->>'generationId' IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "Generations" g WHERE g."Id"::text=snapshot->>'generationId' AND g."FamilyId"=NEW."FamilyId" AND g."StudentId"::text=snapshot->>'studentId' AND g."InputHash"=snapshot->>'generationInputHash' AND g."Cursor"::text=snapshot->>'generationAttemptCursor') THEN
  RAISE EXCEPTION 'Plan projection requires the actual captured generation' USING ERRCODE='23514';
 END IF;
 IF snapshot->>'assessmentEventSequence' IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "AssessmentConsumerCursor" c WHERE c."FamilyId"=NEW."FamilyId" AND c."StudentId"::text=snapshot->>'studentId' AND c."ConsumerName"='assessment/1' AND c."LastEventSequence"::text=snapshot->>'assessmentEventSequence' AND c."LastAppliedEventId"::text=snapshot->>'assessmentAppliedEventId') THEN
  RAISE EXCEPTION 'Plan projection requires the actual captured consumer boundary' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $guard$;
CREATE TRIGGER plan_projection_provenance BEFORE INSERT OR UPDATE ON "PlanRevisions" FOR EACH ROW EXECUTE FUNCTION learning_plan_projection_guard();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION learning_plan_projection_guard() CASCADE;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_PlanRevision_ProjectionSnapshot",
                table: "PlanRevisions");

            migrationBuilder.DropColumn(
                name: "ProjectionSnapshot",
                table: "PlanRevisions");

            migrationBuilder.DropColumn(
                name: "ProjectionSnapshotHash",
                table: "PlanRevisions");
        }
    }
}
