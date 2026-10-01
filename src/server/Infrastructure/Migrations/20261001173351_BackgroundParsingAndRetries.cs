using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundParsingAndRetries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FileId",
                table: "Sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "Outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "BuilderRuns",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_FamilyId_FileId",
                table: "Sources",
                columns: new[] { "FamilyId", "FileId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_PrivateFile_FamilyId_FileId",
                table: "Sources",
                columns: new[] { "FamilyId", "FileId" },
                principalTable: "PrivateFile",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sources_PrivateFile_FamilyId_FileId",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_Sources_FamilyId_FileId",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "FileId",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BuilderRuns");
        }
    }
}
