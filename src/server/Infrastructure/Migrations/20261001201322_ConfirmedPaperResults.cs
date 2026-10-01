using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmedPaperResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AttemptId",
                table: "PaperWrong",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationReason",
                table: "PaperWrong",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                table: "PaperWrong",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedBy",
                table: "PaperWrong",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DraftId",
                table: "PaperWrong",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionId",
                table: "PaperWrong",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseId",
                table: "PaperWrong",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperWrong_AttemptId",
                table: "PaperWrong",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperWrong_FamilyId_AttemptId",
                table: "PaperWrong",
                columns: new[] { "FamilyId", "AttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperWrong_FamilyId_DraftId",
                table: "PaperWrong",
                columns: new[] { "FamilyId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperWrong_FamilyId_ReleaseId",
                table: "PaperWrong",
                columns: new[] { "FamilyId", "ReleaseId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PaperWrong_Attempts_FamilyId_AttemptId",
                table: "PaperWrong",
                columns: new[] { "FamilyId", "AttemptId" },
                principalTable: "Attempts",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperWrong_Drafts_FamilyId_DraftId",
                table: "PaperWrong",
                columns: new[] { "FamilyId", "DraftId" },
                principalTable: "Drafts",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperWrong_Releases_FamilyId_ReleaseId",
                table: "PaperWrong",
                columns: new[] { "FamilyId", "ReleaseId" },
                principalTable: "Releases",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaperWrong_Attempts_FamilyId_AttemptId",
                table: "PaperWrong");

            migrationBuilder.DropForeignKey(
                name: "FK_PaperWrong_Drafts_FamilyId_DraftId",
                table: "PaperWrong");

            migrationBuilder.DropForeignKey(
                name: "FK_PaperWrong_Releases_FamilyId_ReleaseId",
                table: "PaperWrong");

            migrationBuilder.DropIndex(
                name: "IX_PaperWrong_AttemptId",
                table: "PaperWrong");

            migrationBuilder.DropIndex(
                name: "IX_PaperWrong_FamilyId_AttemptId",
                table: "PaperWrong");

            migrationBuilder.DropIndex(
                name: "IX_PaperWrong_FamilyId_DraftId",
                table: "PaperWrong");

            migrationBuilder.DropIndex(
                name: "IX_PaperWrong_FamilyId_ReleaseId",
                table: "PaperWrong");

            migrationBuilder.DropColumn(
                name: "AttemptId",
                table: "PaperWrong");

            migrationBuilder.DropColumn(
                name: "ConfirmationReason",
                table: "PaperWrong");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "PaperWrong");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                table: "PaperWrong");

            migrationBuilder.DropColumn(
                name: "DraftId",
                table: "PaperWrong");

            migrationBuilder.DropColumn(
                name: "QuestionId",
                table: "PaperWrong");

            migrationBuilder.DropColumn(
                name: "ReleaseId",
                table: "PaperWrong");
        }
    }
}
