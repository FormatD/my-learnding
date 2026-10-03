using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProjectionConsumerReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryRound",
                table: "Outbox",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StudentId",
                table: "BackgroundJob",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetGenerationId",
                table: "BackgroundJob",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Outbox_FamilyId_Id",
                table: "Outbox",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "ConsumerReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumerReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsumerReceipt_BackgroundJob_FamilyId_JobId",
                        columns: x => new { x.FamilyId, x.JobId },
                        principalTable: "BackgroundJob",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConsumerReceipt_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConsumerReceipt_Generations_FamilyId_GenerationId",
                        columns: x => new { x.FamilyId, x.GenerationId },
                        principalTable: "Generations",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConsumerReceipt_Outbox_FamilyId_EventId",
                        columns: x => new { x.FamilyId, x.EventId },
                        principalTable: "Outbox",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConsumerReceipt_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_FamilyId_StudentId",
                table: "BackgroundJob",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_TargetGenerationId",
                table: "BackgroundJob",
                column: "TargetGenerationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerReceipt_ConsumerName_EventId",
                table: "ConsumerReceipt",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerReceipt_FamilyId",
                table: "ConsumerReceipt",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerReceipt_FamilyId_EventId",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerReceipt_FamilyId_GenerationId",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "GenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerReceipt_FamilyId_JobId",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerReceipt_FamilyId_StudentId",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_BackgroundJob_Students_FamilyId_StudentId",
                table: "BackgroundJob",
                columns: new[] { "FamilyId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackgroundJob_Students_FamilyId_StudentId",
                table: "BackgroundJob");

            migrationBuilder.DropTable(
                name: "ConsumerReceipt");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Outbox_FamilyId_Id",
                table: "Outbox");

            migrationBuilder.DropIndex(
                name: "IX_BackgroundJob_FamilyId_StudentId",
                table: "BackgroundJob");

            migrationBuilder.DropIndex(
                name: "IX_BackgroundJob_TargetGenerationId",
                table: "BackgroundJob");

            migrationBuilder.DropColumn(
                name: "RetryRound",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "BackgroundJob");

            migrationBuilder.DropColumn(
                name: "TargetGenerationId",
                table: "BackgroundJob");
        }
    }
}
