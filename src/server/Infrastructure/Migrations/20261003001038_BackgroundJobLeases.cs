using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundJobLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackgroundJob",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    InputRef = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    InputPayload = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    RetryRound = table.Column<int>(type: "integer", nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "text", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    LeaseSeconds = table.Column<int>(type: "integer", nullable: false),
                    HeartbeatSeconds = table.Column<int>(type: "integer", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJob", x => x.Id);
                    table.UniqueConstraint("AK_BackgroundJob_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.CheckConstraint("CK_BackgroundJob_Lease", "(\"Status\"='Running' AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND \"HeartbeatAt\" IS NOT NULL) OR (\"Status\"<>'Running' AND \"LeaseOwner\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
                    table.CheckConstraint("CK_BackgroundJob_Limits", "\"AttemptCount\">=0 AND \"AttemptCount\"<=\"MaxAttempts\" AND \"MaxAttempts\" BETWEEN 1 AND 10 AND \"RetryRound\">=0 AND \"LeaseSeconds\" BETWEEN 3 AND 300 AND \"HeartbeatSeconds\">=1 AND \"HeartbeatSeconds\"*2<=\"LeaseSeconds\"");
                    table.CheckConstraint("CK_BackgroundJob_Status", "\"Status\" IN ('Queued','Running','Succeeded','Retrying','Failed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_BackgroundJob_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobLeaseAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseOwner = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    RetryRound = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobLeaseAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobLeaseAttempt_BackgroundJob_FamilyId_JobId",
                        columns: x => new { x.FamilyId, x.JobId },
                        principalTable: "BackgroundJob",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobLeaseAttempt_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_FamilyId",
                table: "BackgroundJob",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_FamilyId_Type_IdempotencyKey",
                table: "BackgroundJob",
                columns: new[] { "FamilyId", "Type", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_FamilyId_Type_InputRef",
                table: "BackgroundJob",
                columns: new[] { "FamilyId", "Type", "InputRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_Status_NextRunAt_LeaseExpiresAt",
                table: "BackgroundJob",
                columns: new[] { "Status", "NextRunAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobLeaseAttempt_FamilyId",
                table: "JobLeaseAttempt",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_JobLeaseAttempt_FamilyId_JobId",
                table: "JobLeaseAttempt",
                columns: new[] { "FamilyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobLeaseAttempt_JobId_LeaseOwner",
                table: "JobLeaseAttempt",
                columns: new[] { "JobId", "LeaseOwner" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobLeaseAttempt_JobId_RetryRound_Number",
                table: "JobLeaseAttempt",
                columns: new[] { "JobId", "RetryRound", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobLeaseAttempt");

            migrationBuilder.DropTable(
                name: "BackgroundJob");
        }
    }
}
