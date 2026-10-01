using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BuilderInputAndProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedDraftId",
                table: "Candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedKCId",
                table: "Candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Decision",
                table: "Candidates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "Candidates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "Candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "Candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputVersion",
                table: "BuilderRuns",
                type: "text",
                nullable: false,
                defaultValue: "builder-input/1");

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryReleaseId",
                table: "BuilderRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Drafts_FamilyId_Id",
                table: "Drafts",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_FamilyId_CreatedDraftId",
                table: "Candidates",
                columns: new[] { "FamilyId", "CreatedDraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderRuns_FamilyId_LibraryReleaseId",
                table: "BuilderRuns",
                columns: new[] { "FamilyId", "LibraryReleaseId" });

            migrationBuilder.AddForeignKey(
                name: "FK_BuilderRuns_Releases_FamilyId_LibraryReleaseId",
                table: "BuilderRuns",
                columns: new[] { "FamilyId", "LibraryReleaseId" },
                principalTable: "Releases",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_Drafts_FamilyId_CreatedDraftId",
                table: "Candidates",
                columns: new[] { "FamilyId", "CreatedDraftId" },
                principalTable: "Drafts",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BuilderRuns_Releases_FamilyId_LibraryReleaseId",
                table: "BuilderRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_Drafts_FamilyId_CreatedDraftId",
                table: "Candidates");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Drafts_FamilyId_Id",
                table: "Drafts");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_FamilyId_CreatedDraftId",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_BuilderRuns_FamilyId_LibraryReleaseId",
                table: "BuilderRuns");

            migrationBuilder.DropColumn(
                name: "CreatedDraftId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "CreatedKCId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "Decision",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "InputVersion",
                table: "BuilderRuns");

            migrationBuilder.DropColumn(
                name: "LibraryReleaseId",
                table: "BuilderRuns");
        }
    }
}
