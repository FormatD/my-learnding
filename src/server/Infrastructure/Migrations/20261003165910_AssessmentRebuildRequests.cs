using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentRebuildRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssessmentRebuildRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetGenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Snapshot = table.Column<string>(type: "text", nullable: false),
                    SnapshotHash = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentRebuildRequest", x => x.Id);
                    table.UniqueConstraint("AK_AssessmentRebuildRequest_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.CheckConstraint("CK_AssessmentRebuildRequest_Snapshot", "jsonb_typeof(\"Snapshot\"::jsonb)='object' AND length(\"SnapshotHash\")=64 AND length(\"Reason\") BETWEEN 1 AND 4000");
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildRequest_Accounts_FamilyId_RequestedBy",
                        columns: x => new { x.FamilyId, x.RequestedBy },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildRequest_BackgroundJob_FamilyId_JobId",
                        columns: x => new { x.FamilyId, x.JobId },
                        principalTable: "BackgroundJob",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildRequest_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildRequest_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentRebuildResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    ReusedGeneration = table.Column<bool>(type: "boolean", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentRebuildResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildResult_AssessmentRebuildRequest_FamilyId_R~",
                        columns: x => new { x.FamilyId, x.RequestId },
                        principalTable: "AssessmentRebuildRequest",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildResult_BackgroundJob_FamilyId_JobId",
                        columns: x => new { x.FamilyId, x.JobId },
                        principalTable: "BackgroundJob",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildResult_DomainEvent_FamilyId_AppliedEventId",
                        columns: x => new { x.FamilyId, x.AppliedEventId },
                        principalTable: "DomainEvent",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildResult_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildResult_Generations_FamilyId_GenerationId",
                        columns: x => new { x.FamilyId, x.GenerationId },
                        principalTable: "Generations",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentRebuildResult_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_FamilyId_StudentId_Type",
                table: "BackgroundJob",
                columns: new[] { "FamilyId", "StudentId", "Type" },
                unique: true,
                filter: "\"Type\"='AssessmentRebuild' AND \"Status\" IN ('Queued','Running','Retrying')");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildRequest_FamilyId",
                table: "AssessmentRebuildRequest",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildRequest_FamilyId_JobId",
                table: "AssessmentRebuildRequest",
                columns: new[] { "FamilyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildRequest_FamilyId_RequestedBy",
                table: "AssessmentRebuildRequest",
                columns: new[] { "FamilyId", "RequestedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildRequest_FamilyId_StudentId",
                table: "AssessmentRebuildRequest",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildRequest_JobId",
                table: "AssessmentRebuildRequest",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildRequest_TargetGenerationId",
                table: "AssessmentRebuildRequest",
                column: "TargetGenerationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_AppliedEventId",
                table: "AssessmentRebuildResult",
                column: "AppliedEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_FamilyId",
                table: "AssessmentRebuildResult",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_FamilyId_AppliedEventId",
                table: "AssessmentRebuildResult",
                columns: new[] { "FamilyId", "AppliedEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_FamilyId_GenerationId",
                table: "AssessmentRebuildResult",
                columns: new[] { "FamilyId", "GenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_FamilyId_JobId",
                table: "AssessmentRebuildResult",
                columns: new[] { "FamilyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_FamilyId_RequestId",
                table: "AssessmentRebuildResult",
                columns: new[] { "FamilyId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_FamilyId_StudentId",
                table: "AssessmentRebuildResult",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_RequestId",
                table: "AssessmentRebuildResult",
                column: "RequestId",
                unique: true);
            migrationBuilder.Sql("CREATE FUNCTION learning_rebuild_record_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Assessment rebuild records are immutable' USING ERRCODE='23514'; END $$; CREATE TRIGGER rebuild_request_immutable BEFORE UPDATE ON \"AssessmentRebuildRequest\" FOR EACH ROW EXECUTE FUNCTION learning_rebuild_record_immutable(); CREATE TRIGGER rebuild_result_immutable BEFORE UPDATE ON \"AssessmentRebuildResult\" FOR EACH ROW EXECUTE FUNCTION learning_rebuild_record_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentRebuildResult");

            migrationBuilder.DropTable(
                name: "AssessmentRebuildRequest");
            migrationBuilder.Sql("DROP FUNCTION learning_rebuild_record_immutable();");

            migrationBuilder.DropIndex(
                name: "IX_BackgroundJob_FamilyId_StudentId_Type",
                table: "BackgroundJob");

        }
    }
}
