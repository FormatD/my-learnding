using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublishedMappingContainers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MappingSetRevision_FamilyId_OwnerType_OwnerRevisionId",
                table: "MappingSetRevision");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReviewDecisionId",
                table: "MappingSetRevision",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ContentReviewRecordId",
                table: "MappingSetRevision",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverageOrigin",
                table: "MappingSetRevision",
                type: "text",
                nullable: false,
                defaultValue: "HumanReviewed");

            migrationBuilder.AddColumn<string>(
                name: "PublishedMappingVersion",
                table: "ContentReviewRecord",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ContentReviewRecord_FamilyId_Id",
                table: "ContentReviewRecord",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "ReleaseMappingSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SetRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseMappingSet", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseMappingSet_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseMappingSet_MappingSetRevision_FamilyId_SetRevisionId",
                        columns: x => new { x.FamilyId, x.SetRevisionId },
                        principalTable: "MappingSetRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseMappingSet_Releases_FamilyId_ReleaseId",
                        columns: x => new { x.FamilyId, x.ReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetRevision_FamilyId_ContentReviewRecordId",
                table: "MappingSetRevision",
                columns: new[] { "FamilyId", "ContentReviewRecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetRevision_FamilyId_OwnerType_OwnerRevisionId",
                table: "MappingSetRevision",
                columns: new[] { "FamilyId", "OwnerType", "OwnerRevisionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MappingSetRevision_ReviewSource",
                table: "MappingSetRevision",
                sql: "(\"ReviewDecisionId\" IS NOT NULL) <> (\"ContentReviewRecordId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseMappingSet_FamilyId",
                table: "ReleaseMappingSet",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseMappingSet_FamilyId_ReleaseId",
                table: "ReleaseMappingSet",
                columns: new[] { "FamilyId", "ReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseMappingSet_FamilyId_SetRevisionId",
                table: "ReleaseMappingSet",
                columns: new[] { "FamilyId", "SetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseMappingSet_ReleaseId_OwnerType_OwnerId",
                table: "ReleaseMappingSet",
                columns: new[] { "ReleaseId", "OwnerType", "OwnerId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MappingSetRevision_ContentReviewRecord_FamilyId_ContentRevi~",
                table: "MappingSetRevision",
                columns: new[] { "FamilyId", "ContentReviewRecordId" },
                principalTable: "ContentReviewRecord",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MappingSetRevision_ContentReviewRecord_FamilyId_ContentRevi~",
                table: "MappingSetRevision");

            migrationBuilder.DropTable(
                name: "ReleaseMappingSet");

            migrationBuilder.DropIndex(
                name: "IX_MappingSetRevision_FamilyId_ContentReviewRecordId",
                table: "MappingSetRevision");

            migrationBuilder.DropIndex(
                name: "IX_MappingSetRevision_FamilyId_OwnerType_OwnerRevisionId",
                table: "MappingSetRevision");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MappingSetRevision_ReviewSource",
                table: "MappingSetRevision");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ContentReviewRecord_FamilyId_Id",
                table: "ContentReviewRecord");

            migrationBuilder.DropColumn(
                name: "ContentReviewRecordId",
                table: "MappingSetRevision");

            migrationBuilder.DropColumn(
                name: "CoverageOrigin",
                table: "MappingSetRevision");

            migrationBuilder.DropColumn(
                name: "PublishedMappingVersion",
                table: "ContentReviewRecord");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReviewDecisionId",
                table: "MappingSetRevision",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingSetRevision_FamilyId_OwnerType_OwnerRevisionId",
                table: "MappingSetRevision",
                columns: new[] { "FamilyId", "OwnerType", "OwnerRevisionId" },
                unique: true);
        }
    }
}
