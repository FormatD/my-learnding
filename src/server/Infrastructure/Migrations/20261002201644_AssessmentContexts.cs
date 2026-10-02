using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContextId",
                table: "Evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssessmentContext",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradingRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingSetRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceRuleVersion = table.Column<string>(type: "text", nullable: false),
                    ActivationStatus = table.Column<string>(type: "text", nullable: false),
                    MappingSource = table.Column<string>(type: "text", nullable: false),
                    EvidencePolicy = table.Column<string>(type: "text", nullable: false),
                    AdmissionStatus = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentContext", x => x.Id);
                    table.UniqueConstraint("AK_AssessmentContext_FamilyId_Id_GenerationId_StudentId_Attemp~", x => new { x.FamilyId, x.Id, x.GenerationId, x.StudentId, x.AttemptId, x.GradingRevisionId });
                    table.ForeignKey(
                        name: "FK_AssessmentContext_Attempts_FamilyId_AttemptId",
                        columns: x => new { x.FamilyId, x.AttemptId },
                        principalTable: "Attempts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_ContentRevision_FamilyId_QuestionRevision~",
                        columns: x => new { x.FamilyId, x.QuestionRevisionId },
                        principalTable: "ContentRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_CorrectionBatch_FamilyId_CorrectionBatchId",
                        columns: x => new { x.FamilyId, x.CorrectionBatchId },
                        principalTable: "CorrectionBatch",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_Generations_FamilyId_GenerationId",
                        columns: x => new { x.FamilyId, x.GenerationId },
                        principalTable: "Generations",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_Gradings_FamilyId_GradingRevisionId",
                        columns: x => new { x.FamilyId, x.GradingRevisionId },
                        principalTable: "Gradings",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_MappingSetRevision_FamilyId_MappingSetRev~",
                        columns: x => new { x.FamilyId, x.MappingSetRevisionId },
                        principalTable: "MappingSetRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_Releases_FamilyId_ContentReleaseId",
                        columns: x => new { x.FamilyId, x.ContentReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_Releases_FamilyId_MappingReleaseId",
                        columns: x => new { x.FamilyId, x.MappingReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentContext_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_ContextId_KCId_Part_Positive",
                table: "Evidence",
                columns: new[] { "ContextId", "KCId", "Part", "Positive" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_FamilyId_ContextId_GenerationId_StudentId_AttemptI~",
                table: "Evidence",
                columns: new[] { "FamilyId", "ContextId", "GenerationId", "StudentId", "AttemptId", "GradingId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId",
                table: "AssessmentContext",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_AttemptId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "AttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_ContentReleaseId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "ContentReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_CorrectionBatchId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "CorrectionBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_GenerationId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "GenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_GradingRevisionId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "GradingRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_MappingReleaseId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "MappingReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_MappingSetRevisionId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "MappingSetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_QuestionRevisionId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "QuestionRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_StudentId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_GenerationId_AttemptId",
                table: "AssessmentContext",
                columns: new[] { "GenerationId", "AttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_GenerationId_AttemptId_GradingRevisionId_~",
                table: "AssessmentContext",
                columns: new[] { "GenerationId", "AttemptId", "GradingRevisionId", "MappingSetRevisionId", "EvidenceRuleVersion" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Evidence_AssessmentContext_FamilyId_ContextId_GenerationId_~",
                table: "Evidence",
                columns: new[] { "FamilyId", "ContextId", "GenerationId", "StudentId", "AttemptId", "GradingId" },
                principalTable: "AssessmentContext",
                principalColumns: new[] { "FamilyId", "Id", "GenerationId", "StudentId", "AttemptId", "GradingRevisionId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Evidence_AssessmentContext_FamilyId_ContextId_GenerationId_~",
                table: "Evidence");

            migrationBuilder.DropTable(
                name: "AssessmentContext");

            migrationBuilder.DropIndex(
                name: "IX_Evidence_ContextId_KCId_Part_Positive",
                table: "Evidence");

            migrationBuilder.DropIndex(
                name: "IX_Evidence_FamilyId_ContextId_GenerationId_StudentId_AttemptI~",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "ContextId",
                table: "Evidence");
        }
    }
}
