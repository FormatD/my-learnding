using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnlineAssessmentAppend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Generations_Incremental",
                table: "Generations");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentCheckpoint_GenerationId",
                table: "AssessmentCheckpoint");

            migrationBuilder.AddColumn<Guid>(
                name: "CheckpointId",
                table: "ConsumerReceipt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckpointId",
                table: "AssessmentRebuildResult",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationMode",
                table: "AssessmentCheckpoint",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Cursor",
                table: "AssessmentCheckpoint",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputHash",
                table: "AssessmentCheckpoint",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedInputCount",
                table: "AssessmentCheckpoint",
                type: "integer",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AssessmentCheckpoint_FamilyId_Id_StudentId_GenerationId",
                table: "AssessmentCheckpoint",
                columns: new[] { "FamilyId", "Id", "StudentId", "GenerationId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Generations_Incremental",
                table: "Generations",
                sql: "(\"CalculationMode\" IS NULL AND \"ProcessedInputCount\" IS NULL AND \"IncrementalBaseGenerationId\" IS NULL) OR (\"CalculationMode\" IS NOT NULL AND \"CalculationMode\" IN ('FullStream','FullStreamChangedPrefix','IncrementalAppend','OnlineAppend') AND \"ProcessedInputCount\" IS NOT NULL AND \"ProcessedInputCount\">=0 AND ((\"CalculationMode\"='IncrementalAppend' AND \"IncrementalBaseGenerationId\" IS NOT NULL AND \"IncrementalBaseGenerationId\"<>\"Id\") OR (\"CalculationMode\"<>'IncrementalAppend' AND \"IncrementalBaseGenerationId\" IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerReceipt_FamilyId_CheckpointId_StudentId_GenerationId",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "CheckpointId", "StudentId", "GenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRebuildResult_FamilyId_CheckpointId_StudentId_Gen~",
                table: "AssessmentRebuildResult",
                columns: new[] { "FamilyId", "CheckpointId", "StudentId", "GenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCheckpoint_GenerationId_InputCount",
                table: "AssessmentCheckpoint",
                columns: new[] { "GenerationId", "InputCount" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssessmentCheckpoint_Application",
                table: "AssessmentCheckpoint",
                sql: "(\"InputHash\" IS NULL AND \"Cursor\" IS NULL AND \"CalculationMode\" IS NULL AND \"ProcessedInputCount\" IS NULL) OR (\"InputHash\" IS NOT NULL AND length(\"InputHash\")=64 AND \"Cursor\" IS NOT NULL AND \"Cursor\">=0 AND \"CalculationMode\" IS NOT NULL AND \"CalculationMode\" IN ('FullStream','FullStreamChangedPrefix','IncrementalAppend','OnlineAppend') AND \"ProcessedInputCount\" IS NOT NULL AND \"ProcessedInputCount\">=0 AND \"ProcessedInputCount\"<=\"InputCount\")");

            migrationBuilder.AddForeignKey(
                name: "FK_AssessmentRebuildResult_AssessmentCheckpoint_FamilyId_Check~",
                table: "AssessmentRebuildResult",
                columns: new[] { "FamilyId", "CheckpointId", "StudentId", "GenerationId" },
                principalTable: "AssessmentCheckpoint",
                principalColumns: new[] { "FamilyId", "Id", "StudentId", "GenerationId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ConsumerReceipt_AssessmentCheckpoint_FamilyId_CheckpointId_~",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "CheckpointId", "StudentId", "GenerationId" },
                principalTable: "AssessmentCheckpoint",
                principalColumns: new[] { "FamilyId", "Id", "StudentId", "GenerationId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssessmentRebuildResult_AssessmentCheckpoint_FamilyId_Check~",
                table: "AssessmentRebuildResult");

            migrationBuilder.DropForeignKey(
                name: "FK_ConsumerReceipt_AssessmentCheckpoint_FamilyId_CheckpointId_~",
                table: "ConsumerReceipt");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Generations_Incremental",
                table: "Generations");

            migrationBuilder.DropIndex(
                name: "IX_ConsumerReceipt_FamilyId_CheckpointId_StudentId_GenerationId",
                table: "ConsumerReceipt");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentRebuildResult_FamilyId_CheckpointId_StudentId_Gen~",
                table: "AssessmentRebuildResult");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AssessmentCheckpoint_FamilyId_Id_StudentId_GenerationId",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentCheckpoint_GenerationId_InputCount",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AssessmentCheckpoint_Application",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropColumn(
                name: "CheckpointId",
                table: "ConsumerReceipt");

            migrationBuilder.DropColumn(
                name: "CheckpointId",
                table: "AssessmentRebuildResult");

            migrationBuilder.DropColumn(
                name: "CalculationMode",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropColumn(
                name: "Cursor",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropColumn(
                name: "InputHash",
                table: "AssessmentCheckpoint");

            migrationBuilder.DropColumn(
                name: "ProcessedInputCount",
                table: "AssessmentCheckpoint");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Generations_Incremental",
                table: "Generations",
                sql: "(\"CalculationMode\" IS NULL AND \"ProcessedInputCount\" IS NULL AND \"IncrementalBaseGenerationId\" IS NULL) OR (\"CalculationMode\" IS NOT NULL AND \"CalculationMode\" IN ('FullStream','FullStreamChangedPrefix','IncrementalAppend') AND \"ProcessedInputCount\" IS NOT NULL AND \"ProcessedInputCount\">=0 AND ((\"CalculationMode\"='IncrementalAppend' AND \"IncrementalBaseGenerationId\" IS NOT NULL AND \"IncrementalBaseGenerationId\"<>\"Id\") OR (\"CalculationMode\"<>'IncrementalAppend' AND \"IncrementalBaseGenerationId\" IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCheckpoint_GenerationId",
                table: "AssessmentCheckpoint",
                column: "GenerationId",
                unique: true);
        }
    }
}
