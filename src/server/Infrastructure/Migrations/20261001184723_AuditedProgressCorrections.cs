using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditedProgressCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseId",
                table: "Progresses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Progresses",
                type: "text",
                nullable: false,
                defaultValue: "Confirmed");

            migrationBuilder.CreateTable(
                name: "ProgressChange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldProgressId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewProgressId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Before = table.Column<string>(type: "text", nullable: false),
                    After = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgressChange_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProgressChange_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Progresses_FamilyId_ReleaseId",
                table: "Progresses",
                columns: new[] { "FamilyId", "ReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgressChange_FamilyId",
                table: "ProgressChange",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressChange_FamilyId_StudentId",
                table: "ProgressChange",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Progresses_Releases_FamilyId_ReleaseId",
                table: "Progresses",
                columns: new[] { "FamilyId", "ReleaseId" },
                principalTable: "Releases",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Progresses_Releases_FamilyId_ReleaseId",
                table: "Progresses");

            migrationBuilder.DropTable(
                name: "ProgressChange");

            migrationBuilder.DropIndex(
                name: "IX_Progresses_FamilyId_ReleaseId",
                table: "Progresses");

            migrationBuilder.DropColumn(
                name: "ReleaseId",
                table: "Progresses");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Progresses");
        }
    }
}
