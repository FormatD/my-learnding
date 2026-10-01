using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalMappingCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrectionBatchId",
                table: "Evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MappingReleaseId",
                table: "Evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CorrectionBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviewHash = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionBatch", x => x.Id);
                    table.UniqueConstraint("AK_CorrectionBatch_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrectionBatch_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrectionBatch_Releases_FamilyId_ReleaseId",
                        columns: x => new { x.FamilyId, x.ReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrectionBatch_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CorrectionItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorrectionItem_Attempts_FamilyId_AttemptId",
                        columns: x => new { x.FamilyId, x.AttemptId },
                        principalTable: "Attempts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrectionItem_CorrectionBatch_FamilyId_BatchId",
                        columns: x => new { x.FamilyId, x.BatchId },
                        principalTable: "CorrectionBatch",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrectionItem_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrectionItem_Releases_FamilyId_MappingReleaseId",
                        columns: x => new { x.FamilyId, x.MappingReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionBatch_FamilyId",
                table: "CorrectionBatch",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionBatch_FamilyId_ReleaseId",
                table: "CorrectionBatch",
                columns: new[] { "FamilyId", "ReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionBatch_FamilyId_StudentId",
                table: "CorrectionBatch",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionItem_BatchId_AttemptId",
                table: "CorrectionItem",
                columns: new[] { "BatchId", "AttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionItem_FamilyId",
                table: "CorrectionItem",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionItem_FamilyId_AttemptId",
                table: "CorrectionItem",
                columns: new[] { "FamilyId", "AttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionItem_FamilyId_BatchId",
                table: "CorrectionItem",
                columns: new[] { "FamilyId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionItem_FamilyId_MappingReleaseId",
                table: "CorrectionItem",
                columns: new[] { "FamilyId", "MappingReleaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrectionItem");

            migrationBuilder.DropTable(
                name: "CorrectionBatch");

            migrationBuilder.DropColumn(
                name: "CorrectionBatchId",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "MappingReleaseId",
                table: "Evidence");
        }
    }
}
