using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BuilderRetryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "BuilderRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryRound",
                table: "BuilderRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BuilderAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetryRound = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InputSnapshot = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuilderAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuilderAttempt_BuilderRuns_FamilyId_RunId",
                        columns: x => new { x.FamilyId, x.RunId },
                        principalTable: "BuilderRuns",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderAttempt_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAttempt_FamilyId",
                table: "BuilderAttempt",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAttempt_FamilyId_RunId",
                table: "BuilderAttempt",
                columns: new[] { "FamilyId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAttempt_RunId_RetryRound_Number",
                table: "BuilderAttempt",
                columns: new[] { "RunId", "RetryRound", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuilderAttempt");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "BuilderRuns");

            migrationBuilder.DropColumn(
                name: "RetryRound",
                table: "BuilderRuns");
        }
    }
}
