using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MappingSuggestionReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MappingRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceTitle = table.Column<string>(type: "text", nullable: false),
                    SourcePayload = table.Column<string>(type: "text", nullable: false),
                    SourceHash = table.Column<string>(type: "text", nullable: false),
                    LibraryReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryHash = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingRun", x => x.Id);
                    table.UniqueConstraint("AK_MappingRun_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MappingRun_Drafts_FamilyId_SourceDraftId",
                        columns: x => new { x.FamilyId, x.SourceDraftId },
                        principalTable: "Drafts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingRun_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingRun_Releases_FamilyId_LibraryReleaseId",
                        columns: x => new { x.FamilyId, x.LibraryReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingSuggestion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerTitle = table.Column<string>(type: "text", nullable: false),
                    EvidencePolicy = table.Column<string>(type: "text", nullable: false),
                    SuggestedItems = table.Column<string>(type: "text", nullable: false),
                    Matches = table.Column<string>(type: "text", nullable: false),
                    ValidationFlags = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingSuggestion", x => x.Id);
                    table.UniqueConstraint("AK_MappingSuggestion_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MappingSuggestion_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingSuggestion_MappingRun_FamilyId_RunId",
                        columns: x => new { x.FamilyId, x.RunId },
                        principalTable: "MappingRun",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingReviewDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SuggestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "text", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    OriginalPayloadHash = table.Column<string>(type: "text", nullable: false),
                    CorrectedPayload = table.Column<string>(type: "text", nullable: false),
                    CorrectedPayloadHash = table.Column<string>(type: "text", nullable: false),
                    CreatedDraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingReviewDecision", x => x.Id);
                    table.UniqueConstraint("AK_MappingReviewDecision_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MappingReviewDecision_Accounts_FamilyId_ReviewerId",
                        columns: x => new { x.FamilyId, x.ReviewerId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingReviewDecision_Drafts_FamilyId_CreatedDraftId",
                        columns: x => new { x.FamilyId, x.CreatedDraftId },
                        principalTable: "Drafts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingReviewDecision_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingReviewDecision_MappingSuggestion_FamilyId_Suggestion~",
                        columns: x => new { x.FamilyId, x.SuggestionId },
                        principalTable: "MappingSuggestion",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingSetRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalOwnerRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerDefinitionHash = table.Column<string>(type: "text", nullable: false),
                    RevisionNo = table.Column<int>(type: "integer", nullable: false),
                    EvidencePolicy = table.Column<string>(type: "text", nullable: false),
                    ReviewStatus = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingSetRevision", x => x.Id);
                    table.UniqueConstraint("AK_MappingSetRevision_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MappingSetRevision_Drafts_FamilyId_DraftId",
                        columns: x => new { x.FamilyId, x.DraftId },
                        principalTable: "Drafts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingSetRevision_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingSetRevision_MappingReviewDecision_FamilyId_ReviewDec~",
                        columns: x => new { x.FamilyId, x.ReviewDecisionId },
                        principalTable: "MappingReviewDecision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingSetItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SetRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    KCId = table.Column<Guid>(type: "uuid", nullable: false),
                    KCRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    CoverageWeight = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: false),
                    EvidenceShare = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: false),
                    EvidenceMode = table.Column<string>(type: "text", nullable: false),
                    Step = table.Column<string>(type: "text", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ModelScore = table.Column<decimal>(type: "numeric", nullable: true),
                    SourceRefs = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingSetItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MappingSetItem_ContentIdentity_FamilyId_KCId",
                        columns: x => new { x.FamilyId, x.KCId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingSetItem_ContentRevision_FamilyId_KCRevisionId",
                        columns: x => new { x.FamilyId, x.KCRevisionId },
                        principalTable: "ContentRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingSetItem_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingSetItem_MappingSetRevision_FamilyId_SetRevisionId",
                        columns: x => new { x.FamilyId, x.SetRevisionId },
                        principalTable: "MappingSetRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MappingReviewDecision_FamilyId",
                table: "MappingReviewDecision",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingReviewDecision_FamilyId_CreatedDraftId",
                table: "MappingReviewDecision",
                columns: new[] { "FamilyId", "CreatedDraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingReviewDecision_FamilyId_ReviewerId",
                table: "MappingReviewDecision",
                columns: new[] { "FamilyId", "ReviewerId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingReviewDecision_FamilyId_SuggestionId",
                table: "MappingReviewDecision",
                columns: new[] { "FamilyId", "SuggestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingReviewDecision_SuggestionId",
                table: "MappingReviewDecision",
                column: "SuggestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingRun_FamilyId",
                table: "MappingRun",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingRun_FamilyId_InputHash",
                table: "MappingRun",
                columns: new[] { "FamilyId", "InputHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingRun_FamilyId_LibraryReleaseId",
                table: "MappingRun",
                columns: new[] { "FamilyId", "LibraryReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingRun_FamilyId_SourceDraftId",
                table: "MappingRun",
                columns: new[] { "FamilyId", "SourceDraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetItem_FamilyId",
                table: "MappingSetItem",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetItem_FamilyId_KCId",
                table: "MappingSetItem",
                columns: new[] { "FamilyId", "KCId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetItem_FamilyId_KCRevisionId",
                table: "MappingSetItem",
                columns: new[] { "FamilyId", "KCRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetItem_FamilyId_SetRevisionId",
                table: "MappingSetItem",
                columns: new[] { "FamilyId", "SetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetItem_SetRevisionId_Sequence",
                table: "MappingSetItem",
                columns: new[] { "SetRevisionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetRevision_FamilyId",
                table: "MappingSetRevision",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetRevision_FamilyId_DraftId",
                table: "MappingSetRevision",
                columns: new[] { "FamilyId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetRevision_FamilyId_OwnerType_OwnerRevisionId",
                table: "MappingSetRevision",
                columns: new[] { "FamilyId", "OwnerType", "OwnerRevisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetRevision_FamilyId_ReviewDecisionId",
                table: "MappingSetRevision",
                columns: new[] { "FamilyId", "ReviewDecisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSuggestion_FamilyId",
                table: "MappingSuggestion",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingSuggestion_FamilyId_RunId",
                table: "MappingSuggestion",
                columns: new[] { "FamilyId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSuggestion_RunId_OwnerType_OwnerId",
                table: "MappingSuggestion",
                columns: new[] { "RunId", "OwnerType", "OwnerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MappingSetItem");

            migrationBuilder.DropTable(
                name: "MappingSetRevision");

            migrationBuilder.DropTable(
                name: "MappingReviewDecision");

            migrationBuilder.DropTable(
                name: "MappingSuggestion");

            migrationBuilder.DropTable(
                name: "MappingRun");
        }
    }
}
