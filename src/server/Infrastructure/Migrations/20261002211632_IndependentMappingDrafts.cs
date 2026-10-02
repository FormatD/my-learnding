using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndependentMappingDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MappingSetRevision_ReviewSource",
                table: "MappingSetRevision");

            migrationBuilder.CreateTable(
                name: "IndependentMappingDraft",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SetRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourcePayload = table.Column<string>(type: "text", nullable: false),
                    SourceHash = table.Column<string>(type: "text", nullable: false),
                    LibraryReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryHash = table.Column<string>(type: "text", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndependentMappingDraft", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndependentMappingDraft_Accounts_FamilyId_SubmittedBy",
                        columns: x => new { x.FamilyId, x.SubmittedBy },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IndependentMappingDraft_Drafts_FamilyId_SourceDraftId",
                        columns: x => new { x.FamilyId, x.SourceDraftId },
                        principalTable: "Drafts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IndependentMappingDraft_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IndependentMappingDraft_MappingSetRevision_FamilyId_SetRevi~",
                        columns: x => new { x.FamilyId, x.SetRevisionId },
                        principalTable: "MappingSetRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IndependentMappingDraft_Releases_FamilyId_LibraryReleaseId",
                        columns: x => new { x.FamilyId, x.LibraryReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MappingSetRevision_ReviewSource",
                table: "MappingSetRevision",
                sql: "(\"ReviewStatus\" = 'Draft' AND \"ReviewDecisionId\" IS NULL AND \"ContentReviewRecordId\" IS NULL) OR (\"ReviewStatus\" IN ('ReviewedDraft','ReviewedCatalog') AND ((\"ReviewDecisionId\" IS NOT NULL) <> (\"ContentReviewRecordId\" IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_IndependentMappingDraft_FamilyId",
                table: "IndependentMappingDraft",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_IndependentMappingDraft_FamilyId_LibraryReleaseId",
                table: "IndependentMappingDraft",
                columns: new[] { "FamilyId", "LibraryReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_IndependentMappingDraft_FamilyId_SetRevisionId",
                table: "IndependentMappingDraft",
                columns: new[] { "FamilyId", "SetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_IndependentMappingDraft_FamilyId_SourceDraftId",
                table: "IndependentMappingDraft",
                columns: new[] { "FamilyId", "SourceDraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_IndependentMappingDraft_FamilyId_SubmittedBy",
                table: "IndependentMappingDraft",
                columns: new[] { "FamilyId", "SubmittedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_IndependentMappingDraft_SetRevisionId",
                table: "IndependentMappingDraft",
                column: "SetRevisionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndependentMappingDraft");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MappingSetRevision_ReviewSource",
                table: "MappingSetRevision");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MappingSetRevision_ReviewSource",
                table: "MappingSetRevision",
                sql: "(\"ReviewDecisionId\" IS NOT NULL) <> (\"ContentReviewRecordId\" IS NOT NULL)");
        }
    }
}
