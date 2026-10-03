using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviewTargetConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Tasks_FamilyId_Id_StudentId",
                table: "Tasks",
                columns: new[] { "FamilyId", "Id", "StudentId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Attempts_FamilyId_Id_StudentId",
                table: "Attempts",
                columns: new[] { "FamilyId", "Id", "StudentId" });

            migrationBuilder.CreateTable(
                name: "ReviewTargetConfirmation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingSetRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    MeasurementSnapshot = table.Column<string>(type: "text", nullable: false),
                    MeasurementHash = table.Column<string>(type: "text", nullable: false),
                    PreviewHash = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewTargetConfirmation", x => x.Id);
                    table.CheckConstraint("CK_ReviewTargetConfirmation_Decision", "\"Action\" IN ('KeepOriginal','AdoptMeasuredTarget') AND length(\"Reason\") BETWEEN 1 AND 1000 AND length(\"MeasurementHash\")=64 AND length(\"PreviewHash\")=64 AND jsonb_typeof(\"MeasurementSnapshot\"::jsonb)='object' AND ((\"Action\"='KeepOriginal' AND \"ConfirmedTargetId\"=\"OriginalTargetId\") OR (\"Action\"='AdoptMeasuredTarget' AND \"ConfirmedTargetId\"<>\"OriginalTargetId\"))");
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_Accounts_FamilyId_ConfirmedBy",
                        columns: x => new { x.FamilyId, x.ConfirmedBy },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_Attempts_FamilyId_AttemptId_Studen~",
                        columns: x => new { x.FamilyId, x.AttemptId, x.StudentId },
                        principalTable: "Attempts",
                        principalColumns: new[] { "FamilyId", "Id", "StudentId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_ContentIdentity_FamilyId_Confirmed~",
                        columns: x => new { x.FamilyId, x.ConfirmedTargetId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_ContentIdentity_FamilyId_OriginalT~",
                        columns: x => new { x.FamilyId, x.OriginalTargetId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_ContentRevision_FamilyId_QuestionR~",
                        columns: x => new { x.FamilyId, x.QuestionRevisionId },
                        principalTable: "ContentRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_MappingSetRevision_FamilyId_Mappin~",
                        columns: x => new { x.FamilyId, x.MappingSetRevisionId },
                        principalTable: "MappingSetRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_Releases_FamilyId_MappingReleaseId",
                        columns: x => new { x.FamilyId, x.MappingReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewTargetConfirmation_Tasks_FamilyId_TaskId_StudentId",
                        columns: x => new { x.FamilyId, x.TaskId, x.StudentId },
                        principalTable: "Tasks",
                        principalColumns: new[] { "FamilyId", "Id", "StudentId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_AttemptId_Sequence",
                table: "ReviewTargetConfirmation",
                columns: new[] { "AttemptId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId",
                table: "ReviewTargetConfirmation",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_AttemptId_StudentId",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "AttemptId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_ConfirmedBy",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "ConfirmedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_ConfirmedTargetId",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "ConfirmedTargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_MappingReleaseId",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "MappingReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_MappingSetRevisionId",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "MappingSetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_OriginalTargetId",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "OriginalTargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_QuestionRevisionId",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "QuestionRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_StudentId",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTargetConfirmation_FamilyId_TaskId_StudentId",
                table: "ReviewTargetConfirmation",
                columns: new[] { "FamilyId", "TaskId", "StudentId" });
            migrationBuilder.Sql("""
CREATE FUNCTION learning_review_target_measured(family uuid, release_id uuid, question_id uuid, mapping_id uuid, target uuid) RETURNS boolean LANGUAGE sql STABLE AS $measure$
 SELECT CASE WHEN mapping_id IS NOT NULL THEN EXISTS(
  SELECT 1 FROM "MappingSetRevision" s JOIN "MappingSetItem" i ON i."SetRevisionId"=s."Id" AND i."FamilyId"=s."FamilyId"
  WHERE s."FamilyId"=family AND s."Id"=mapping_id AND s."OwnerType"='Question' AND s."OwnerId"=question_id AND s."EvidencePolicy"<>'NoEvidence' AND i."KCId"=target AND i."EvidenceShare">0 AND i."EvidenceMode" IN ('WholeItem','StepObserved') AND i."Role" NOT IN ('Prerequisite','Context')
 ) ELSE EXISTS(
  SELECT 1 FROM "Releases" r CROSS JOIN LATERAL jsonb_array_elements(r."Payload"::jsonb->'questions') q CROSS JOIN LATERAL jsonb_array_elements(q->'mappings') m
  WHERE r."FamilyId"=family AND r."Id"=release_id AND q->>'id'=question_id::text AND q->>'policy'<>'NoEvidence' AND m->>'kcId'=target::text AND (m->>'share')::numeric>0 AND m->>'mode' IN ('WholeItem','StepObserved') AND m->>'role' NOT IN ('Prerequisite','Context')
 ) END;
$measure$;
CREATE FUNCTION learning_review_target_confirmation_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
DECLARE snapshot jsonb; question_id uuid; current_release uuid; current_mapping uuid; current_batch uuid; revision uuid;
BEGIN
 IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Review target confirmations are immutable; append a new decision' USING ERRCODE='23514'; END IF;
 snapshot=NEW."MeasurementSnapshot"::jsonb; question_id=(snapshot->'question'->>'id')::uuid;
 IF NEW."MeasurementHash" IS DISTINCT FROM encode(sha256(convert_to(NEW."MeasurementSnapshot",'UTF8')),'hex') OR snapshot->>'version' IS DISTINCT FROM 'review-measurement/1' OR snapshot->>'familyId' IS DISTINCT FROM NEW."FamilyId"::text OR snapshot->>'studentId' IS DISTINCT FROM NEW."StudentId"::text OR snapshot->>'attemptId' IS DISTINCT FROM NEW."AttemptId"::text OR snapshot->>'taskId' IS DISTINCT FROM NEW."TaskId"::text OR snapshot->>'originalTargetId' IS DISTINCT FROM NEW."OriginalTargetId"::text OR snapshot->>'mappingReleaseId' IS DISTINCT FROM NEW."MappingReleaseId"::text OR snapshot->>'mappingSetRevisionId' IS DISTINCT FROM NEW."MappingSetRevisionId"::text OR snapshot->'question'->>'revisionId' IS DISTINCT FROM NEW."QuestionRevisionId"::text THEN
  RAISE EXCEPTION 'Review decision requires its original owned measurement snapshot' USING ERRCODE='23514';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM "Attempts" a JOIN "Sessions" s ON s."Id"=a."SessionId" AND s."FamilyId"=a."FamilyId" JOIN "Tasks" t ON t."Id"=s."TaskId" AND t."FamilyId"=s."FamilyId" WHERE a."Id"=NEW."AttemptId" AND a."FamilyId"=NEW."FamilyId" AND a."StudentId"=NEW."StudentId" AND a."Number"=1 AND t."Id"=NEW."TaskId" AND t."StudentId"=NEW."StudentId" AND t."Type"='Review' AND t."ReviewTargetId"=NEW."OriginalTargetId" AND s."QuestionId"=question_id AND question_id<>NEW."OriginalTargetId") OR NOT EXISTS(SELECT 1 FROM "FamilyMembership" m WHERE m."FamilyId"=NEW."FamilyId" AND m."AccountId"=NEW."ConfirmedBy" AND 'Parent'=ANY(string_to_array(m."Roles",','))) THEN
  RAISE EXCEPTION 'Review decision requires the actual first review answer and current parent' USING ERRCODE='23514';
 END IF;
 SELECT COALESCE(c."MappingReleaseId",s."ReleaseId"),CASE WHEN c."Id" IS NOT NULL THEN c."MappingSetRevisionId" ELSE a."MappingSetRevisionId" END,c."BatchId" INTO current_release,current_mapping,current_batch FROM "Attempts" a JOIN "Sessions" s ON s."Id"=a."SessionId" LEFT JOIN LATERAL (SELECT * FROM "CorrectionItem" i WHERE i."AttemptId"=a."Id" AND i."FamilyId"=a."FamilyId" ORDER BY i."Sequence" DESC LIMIT 1) c ON true WHERE a."Id"=NEW."AttemptId";
 IF current_release IS DISTINCT FROM NEW."MappingReleaseId" OR current_mapping IS DISTINCT FROM NEW."MappingSetRevisionId" OR current_batch::text IS DISTINCT FROM snapshot->>'mappingCorrectionBatchId' THEN RAISE EXCEPTION 'Review decision requires the current effective mapping' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM "ReleaseItem" i WHERE i."FamilyId"=NEW."FamilyId" AND i."ReleaseId"=current_release AND i."IdentityId"=question_id AND i."RevisionId"=NEW."QuestionRevisionId" AND i."EntityType"='Question') OR current_mapping IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ReleaseMappingSet" b WHERE b."FamilyId"=NEW."FamilyId" AND b."ReleaseId"=current_release AND b."OwnerId"=question_id AND b."OwnerRevisionId"=NEW."QuestionRevisionId" AND b."SetRevisionId"=current_mapping AND b."OwnerType"='Question') THEN RAISE EXCEPTION 'Review decision requires the actual published question mapping' USING ERRCODE='23514'; END IF;
 IF learning_review_target_measured(NEW."FamilyId",current_release,question_id,current_mapping,NEW."OriginalTargetId") OR NEW."Action"='AdoptMeasuredTarget' AND NOT learning_review_target_measured(NEW."FamilyId",current_release,question_id,current_mapping,NEW."ConfirmedTargetId") OR NOT EXISTS(SELECT 1 FROM "ContentIdentity" i WHERE i."Id"=NEW."OriginalTargetId" AND i."FamilyId"=NEW."FamilyId" AND i."EntityType"='KC') OR NOT EXISTS(SELECT 1 FROM "ContentIdentity" i WHERE i."Id"=NEW."ConfirmedTargetId" AND i."FamilyId"=NEW."FamilyId" AND i."EntityType"='KC') THEN RAISE EXCEPTION 'Review decision cannot adopt an unmeasured target' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $guard$;
CREATE TRIGGER review_target_confirmation_guard BEFORE INSERT OR UPDATE ON "ReviewTargetConfirmation" FOR EACH ROW EXECUTE FUNCTION learning_review_target_confirmation_guard();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION learning_review_target_confirmation_guard() CASCADE; DROP FUNCTION learning_review_target_measured(uuid,uuid,uuid,uuid,uuid);");
            migrationBuilder.DropTable(
                name: "ReviewTargetConfirmation");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Tasks_FamilyId_Id_StudentId",
                table: "Tasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Attempts_FamilyId_Id_StudentId",
                table: "Attempts");
        }
    }
}
