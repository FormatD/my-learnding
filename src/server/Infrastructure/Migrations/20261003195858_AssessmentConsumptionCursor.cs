using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentConsumptionCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ConsumerReceipt_FamilyId_Id_StudentId_EventId_GenerationId_~",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "Id", "StudentId", "EventId", "GenerationId", "InputHash" });

            migrationBuilder.CreateTable(
                name: "AssessmentConsumerCursor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "text", nullable: false),
                    LastEventSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastAppliedEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentConsumerCursor", x => x.Id);
                    table.CheckConstraint("CK_AssessmentConsumerCursor_Progress", "\"ConsumerName\"='assessment/1' AND \"LastEventSequence\">0 AND length(\"InputHash\")=64");
                    table.ForeignKey(
                        name: "FK_AssessmentConsumerCursor_ConsumerReceipt_FamilyId_LastRecei~",
                        columns: x => new { x.FamilyId, x.LastReceiptId, x.StudentId, x.LastEventId, x.GenerationId, x.InputHash },
                        principalTable: "ConsumerReceipt",
                        principalColumns: new[] { "FamilyId", "Id", "StudentId", "EventId", "GenerationId", "InputHash" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentConsumerCursor_DomainEvent_FamilyId_LastAppliedEv~",
                        columns: x => new { x.FamilyId, x.LastAppliedEventId },
                        principalTable: "DomainEvent",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentConsumerCursor_DomainEvent_FamilyId_LastEventId",
                        columns: x => new { x.FamilyId, x.LastEventId },
                        principalTable: "DomainEvent",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentConsumerCursor_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentConsumerCursor_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentConsumerCursor_FamilyId",
                table: "AssessmentConsumerCursor",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentConsumerCursor_FamilyId_LastAppliedEventId",
                table: "AssessmentConsumerCursor",
                columns: new[] { "FamilyId", "LastAppliedEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentConsumerCursor_FamilyId_LastEventId",
                table: "AssessmentConsumerCursor",
                columns: new[] { "FamilyId", "LastEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentConsumerCursor_FamilyId_LastReceiptId_StudentId_L~",
                table: "AssessmentConsumerCursor",
                columns: new[] { "FamilyId", "LastReceiptId", "StudentId", "LastEventId", "GenerationId", "InputHash" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentConsumerCursor_FamilyId_StudentId",
                table: "AssessmentConsumerCursor",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentConsumerCursor_StudentId_ConsumerName",
                table: "AssessmentConsumerCursor",
                columns: new[] { "StudentId", "ConsumerName" },
                unique: true);
            migrationBuilder.Sql("""
CREATE FUNCTION learning_assessment_cursor_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
BEGIN
 IF TG_OP='UPDATE' AND (NEW."Id"<>OLD."Id" OR NEW."FamilyId"<>OLD."FamilyId" OR NEW."StudentId"<>OLD."StudentId" OR NEW."ConsumerName"<>OLD."ConsumerName" OR NEW."CreatedAt"<>OLD."CreatedAt" OR NEW."LastEventSequence"<=OLD."LastEventSequence") THEN
  RAISE EXCEPTION 'Assessment cursor must advance its own immutable subject' USING ERRCODE='23514';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM "DomainEvent" e WHERE e."Id"=NEW."LastEventId" AND e."FamilyId"=NEW."FamilyId" AND e."StudentId"=NEW."StudentId" AND e."EventSequence"=NEW."LastEventSequence" AND e."DispatchTarget"=NEW."ConsumerName") THEN
  RAISE EXCEPTION 'Assessment cursor requires its real ordered source event' USING ERRCODE='23514';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM "DomainEvent" e WHERE e."Id"=NEW."LastAppliedEventId" AND e."FamilyId"=NEW."FamilyId" AND e."StudentId"=NEW."StudentId" AND e."EventType"='AssessmentApplied' AND e."AggregateId"=NEW."GenerationId" AND e."Payload"::jsonb->'data'->>'inputHash'=NEW."InputHash" AND e."Payload"::jsonb->'data'->'consumption'->>'version'='assessment-consumption/1' AND e."Payload"::jsonb->'data'->'consumption'->>'throughSequence'=NEW."LastEventSequence"::text AND e."Payload"::jsonb->'data'->'consumption'->>'lastEventId'=NEW."LastEventId"::text AND e."Payload"::jsonb->'data'->'consumption'->>'lastReceiptId'=NEW."LastReceiptId"::text AND (e."Payload"::jsonb->'data'->'consumption'->'newEventIds') ? NEW."LastEventId"::text AND COALESCE(e."Payload"::jsonb->'data'->'consumption'->>'priorSequence','0')=CASE WHEN TG_OP='INSERT' THEN '0' ELSE OLD."LastEventSequence"::text END) THEN
  RAISE EXCEPTION 'Assessment cursor requires its actual application and prior boundary' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $guard$;
CREATE TRIGGER assessment_cursor_progress BEFORE INSERT OR UPDATE ON "AssessmentConsumerCursor" FOR EACH ROW EXECUTE FUNCTION learning_assessment_cursor_guard();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION learning_assessment_cursor_guard() CASCADE;");
            migrationBuilder.DropTable(
                name: "AssessmentConsumerCursor");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ConsumerReceipt_FamilyId_Id_StudentId_EventId_GenerationId_~",
                table: "ConsumerReceipt");
        }
    }
}
