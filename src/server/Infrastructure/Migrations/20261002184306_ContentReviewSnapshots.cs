using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContentReviewSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentReviewRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceTitle = table.Column<string>(type: "text", nullable: false),
                    SourcePayload = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ReasonSource = table.Column<string>(type: "text", nullable: false),
                    PublishedReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReviewRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentReviewRecord_Accounts_FamilyId_ReviewerId",
                        columns: x => new { x.FamilyId, x.ReviewerId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentReviewRecord_Drafts_FamilyId_DraftId",
                        columns: x => new { x.FamilyId, x.DraftId },
                        principalTable: "Drafts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentReviewRecord_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentReviewRecord_Releases_FamilyId_PublishedReleaseId",
                        columns: x => new { x.FamilyId, x.PublishedReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReviewRecord_DraftId_DraftVersion",
                table: "ContentReviewRecord",
                columns: new[] { "DraftId", "DraftVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReviewRecord_FamilyId",
                table: "ContentReviewRecord",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReviewRecord_FamilyId_DraftId",
                table: "ContentReviewRecord",
                columns: new[] { "FamilyId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReviewRecord_FamilyId_PublishedReleaseId",
                table: "ContentReviewRecord",
                columns: new[] { "FamilyId", "PublishedReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReviewRecord_FamilyId_ReviewerId",
                table: "ContentReviewRecord",
                columns: new[] { "FamilyId", "ReviewerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentReviewRecord");
        }
    }
}
