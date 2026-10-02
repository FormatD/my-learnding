using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StudentScopedAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StudentId",
                table: "Audits",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Audits" a SET "StudentId"=s."Id" FROM "Students" s
                WHERE a."StudentId" IS NULL AND a."FamilyId"=s."FamilyId"
                  AND s."Id"::text=(CASE WHEN a."Action"='TaskTransition' THEN a."Details"::jsonb ELSE '{}'::jsonb END)->>'studentId';
                UPDATE "Audits" a SET "StudentId"=p."StudentId" FROM "Plans" p
                WHERE a."StudentId" IS NULL AND a."FamilyId"=p."FamilyId"
                  AND p."Id"::text=(CASE WHEN a."Action"='PlanAdjusted' THEN a."Details"::jsonb ELSE '{}'::jsonb END)->>'planId';
                DELETE FROM "Audits" WHERE "StudentId" IS NULL AND "Action" IN ('TaskTransition','PlanAdjusted');
                """);
            migrationBuilder.CreateIndex(
                name: "IX_Audits_FamilyId_StudentId",
                table: "Audits",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Audits_Students_FamilyId_StudentId",
                table: "Audits",
                columns: new[] { "FamilyId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Audits_Students_FamilyId_StudentId",
                table: "Audits");

            migrationBuilder.DropIndex(
                name: "IX_Audits_FamilyId_StudentId",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "Audits");
        }
    }
}
