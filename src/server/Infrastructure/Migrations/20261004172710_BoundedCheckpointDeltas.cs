using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BoundedCheckpointDeltas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BaseCheckpointId",
                table: "AssessmentCheckpoint",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeltaDepth",
                table: "AssessmentCheckpoint",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatePayloadHash",
                table: "AssessmentCheckpoint",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageVersion",
                table: "AssessmentCheckpoint",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCheckpoint_FamilyId_BaseCheckpointId_StudentId_Ge~",
                table: "AssessmentCheckpoint",
                columns: new[] { "FamilyId", "BaseCheckpointId", "StudentId", "GenerationId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssessmentCheckpoint_Storage",
                table: "AssessmentCheckpoint",
                sql: "(\"StorageVersion\" IS NULL AND \"BaseCheckpointId\" IS NULL AND \"DeltaDepth\" IS NULL AND \"StatePayloadHash\" IS NULL) OR (\"StorageVersion\" IS NOT NULL AND \"StorageVersion\"='checkpoint-storage/1' AND \"StatePayloadHash\" IS NOT NULL AND length(\"StatePayloadHash\")=64 AND \"DeltaDepth\" IS NOT NULL AND ((\"DeltaDepth\"=0 AND \"BaseCheckpointId\" IS NULL) OR (\"DeltaDepth\" BETWEEN 1 AND 31 AND \"BaseCheckpointId\" IS NOT NULL AND \"BaseCheckpointId\"<>\"Id\")))");

            migrationBuilder.AddForeignKey(
                name: "FK_AssessmentCheckpoint_AssessmentCheckpoint_FamilyId_BaseChec~",
                table: "AssessmentCheckpoint",
                columns: new[] { "FamilyId", "BaseCheckpointId", "StudentId", "GenerationId" },
                principalTable: "AssessmentCheckpoint",
                principalColumns: new[] { "FamilyId", "Id", "StudentId", "GenerationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An older binary cannot decode deltas after their ownership/hash columns are dropped.
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"AssessmentCheckpoint\" WHERE \"BaseCheckpointId\" IS NOT NULL) THEN RAISE EXCEPTION 'Cannot downgrade checkpoint storage while delta states exist; restore a compatible pre-upgrade backup instead'; END IF; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_AssessmentCheckpoint_AssessmentCheckpoint_FamilyId_BaseChec~",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentCheckpoint_FamilyId_BaseCheckpointId_StudentId_Ge~",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AssessmentCheckpoint_Storage",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropColumn(
                name: "BaseCheckpointId",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropColumn(
                name: "DeltaDepth",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropColumn(
                name: "StatePayloadHash",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropColumn(
                name: "StorageVersion",
                table: "AssessmentCheckpoint");
        }
    }
}
