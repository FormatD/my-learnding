using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ParentBurdenRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParentBurdenRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Minutes = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionReason = table.Column<string>(type: "text", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentBurdenRecord", x => x.Id);
                    table.UniqueConstraint("AK_ParentBurdenRecord_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ParentBurdenRecord_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParentBurdenRecord_ParentBurdenRecord_FamilyId_SupersedesId",
                        columns: x => new { x.FamilyId, x.SupersedesId },
                        principalTable: "ParentBurdenRecord",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParentBurdenRecord_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParentBurdenRecord_FamilyId",
                table: "ParentBurdenRecord",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentBurdenRecord_FamilyId_StudentId",
                table: "ParentBurdenRecord",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentBurdenRecord_FamilyId_SupersedesId",
                table: "ParentBurdenRecord",
                columns: new[] { "FamilyId", "SupersedesId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentBurdenRecord_StudentId_Date",
                table: "ParentBurdenRecord",
                columns: new[] { "StudentId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentBurdenRecord_SupersedesId",
                table: "ParentBurdenRecord",
                column: "SupersedesId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParentBurdenRecord");
        }
    }
}
