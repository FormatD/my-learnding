using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceRevocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrectionBatchId",
                table: "Gradings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AffectedAttemptIds",
                table: "CorrectionBatch",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cause",
                table: "CorrectionBatch",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceGradingRevisionId",
                table: "CorrectionBatch",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GradingCorrectionBatchId",
                table: "AssessmentContext",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MappingCorrectionBatchId",
                table: "AssessmentContext",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Evidence_FamilyId_Id",
                table: "Evidence",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "EvidenceRevocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacementGenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Effect = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceRevocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceRevocation_CorrectionBatch_FamilyId_CorrectionBatch~",
                        columns: x => new { x.FamilyId, x.CorrectionBatchId },
                        principalTable: "CorrectionBatch",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvidenceRevocation_Evidence_FamilyId_EvidenceId",
                        columns: x => new { x.FamilyId, x.EvidenceId },
                        principalTable: "Evidence",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvidenceRevocation_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvidenceRevocation_Generations_FamilyId_ReplacementGenerati~",
                        columns: x => new { x.FamilyId, x.ReplacementGenerationId },
                        principalTable: "Generations",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvidenceRevocation_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Gradings_FamilyId_CorrectionBatchId",
                table: "Gradings",
                columns: new[] { "FamilyId", "CorrectionBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionBatch_FamilyId_SourceGradingRevisionId",
                table: "CorrectionBatch",
                columns: new[] { "FamilyId", "SourceGradingRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_GradingCorrectionBatchId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "GradingCorrectionBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentContext_FamilyId_MappingCorrectionBatchId",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "MappingCorrectionBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRevocation_EvidenceId",
                table: "EvidenceRevocation",
                column: "EvidenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRevocation_FamilyId",
                table: "EvidenceRevocation",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRevocation_FamilyId_CorrectionBatchId",
                table: "EvidenceRevocation",
                columns: new[] { "FamilyId", "CorrectionBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRevocation_FamilyId_EvidenceId",
                table: "EvidenceRevocation",
                columns: new[] { "FamilyId", "EvidenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRevocation_FamilyId_ReplacementGenerationId",
                table: "EvidenceRevocation",
                columns: new[] { "FamilyId", "ReplacementGenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRevocation_FamilyId_StudentId",
                table: "EvidenceRevocation",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AssessmentContext_CorrectionBatch_FamilyId_GradingCorrectio~",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "GradingCorrectionBatchId" },
                principalTable: "CorrectionBatch",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AssessmentContext_CorrectionBatch_FamilyId_MappingCorrectio~",
                table: "AssessmentContext",
                columns: new[] { "FamilyId", "MappingCorrectionBatchId" },
                principalTable: "CorrectionBatch",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CorrectionBatch_Gradings_FamilyId_SourceGradingRevisionId",
                table: "CorrectionBatch",
                columns: new[] { "FamilyId", "SourceGradingRevisionId" },
                principalTable: "Gradings",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Gradings_CorrectionBatch_FamilyId_CorrectionBatchId",
                table: "Gradings",
                columns: new[] { "FamilyId", "CorrectionBatchId" },
                principalTable: "CorrectionBatch",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssessmentContext_CorrectionBatch_FamilyId_GradingCorrectio~",
                table: "AssessmentContext");

            migrationBuilder.DropForeignKey(
                name: "FK_AssessmentContext_CorrectionBatch_FamilyId_MappingCorrectio~",
                table: "AssessmentContext");

            migrationBuilder.DropForeignKey(
                name: "FK_CorrectionBatch_Gradings_FamilyId_SourceGradingRevisionId",
                table: "CorrectionBatch");

            migrationBuilder.DropForeignKey(
                name: "FK_Gradings_CorrectionBatch_FamilyId_CorrectionBatchId",
                table: "Gradings");

            migrationBuilder.DropTable(
                name: "EvidenceRevocation");

            migrationBuilder.DropIndex(
                name: "IX_Gradings_FamilyId_CorrectionBatchId",
                table: "Gradings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Evidence_FamilyId_Id",
                table: "Evidence");

            migrationBuilder.DropIndex(
                name: "IX_CorrectionBatch_FamilyId_SourceGradingRevisionId",
                table: "CorrectionBatch");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentContext_FamilyId_GradingCorrectionBatchId",
                table: "AssessmentContext");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentContext_FamilyId_MappingCorrectionBatchId",
                table: "AssessmentContext");

            migrationBuilder.DropColumn(
                name: "CorrectionBatchId",
                table: "Gradings");

            migrationBuilder.DropColumn(
                name: "AffectedAttemptIds",
                table: "CorrectionBatch");

            migrationBuilder.DropColumn(
                name: "Cause",
                table: "CorrectionBatch");

            migrationBuilder.DropColumn(
                name: "SourceGradingRevisionId",
                table: "CorrectionBatch");

            migrationBuilder.DropColumn(
                name: "GradingCorrectionBatchId",
                table: "AssessmentContext");

            migrationBuilder.DropColumn(
                name: "MappingCorrectionBatchId",
                table: "AssessmentContext");
        }
    }
}
