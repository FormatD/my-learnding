using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledLearningGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoalSnapshots",
                table: "Tasks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "Goals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoalType",
                table: "Goals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "KCId",
                table: "Goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Period",
                table: "Goals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Goals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleRule",
                table: "Goals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "Goals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "Goals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TargetValue",
                table: "Goals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Goals",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Goals_FamilyId_Id",
                table: "Goals",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "GoalChange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Before = table.Column<string>(type: "text", nullable: false),
                    After = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalChange_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoalChange_Goals_FamilyId_GoalId",
                        columns: x => new { x.FamilyId, x.GoalId },
                        principalTable: "Goals",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoalChange_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoalChange_FamilyId",
                table: "GoalChange",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalChange_FamilyId_GoalId",
                table: "GoalChange",
                columns: new[] { "FamilyId", "GoalId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalChange_FamilyId_StudentId",
                table: "GoalChange",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalChange_GoalId_Version",
                table: "GoalChange",
                columns: new[] { "GoalId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalChange");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Goals_FamilyId_Id",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "GoalSnapshots",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "GoalType",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "KCId",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "Period",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "ScheduleRule",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "TargetValue",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Goals");
        }
    }
}
