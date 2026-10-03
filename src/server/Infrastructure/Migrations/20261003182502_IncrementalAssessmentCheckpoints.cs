using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IncrementalAssessmentCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalculationMode",
                table: "Generations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IncrementalBaseGenerationId",
                table: "Generations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedInputCount",
                table: "Generations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Generations_FamilyId_Id_StudentId",
                table: "Generations",
                columns: new[] { "FamilyId", "Id", "StudentId" });

            migrationBuilder.CreateTable(
                name: "AssessmentCheckpoint",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EngineVersion = table.Column<string>(type: "text", nullable: false),
                    PrefixHash = table.Column<string>(type: "text", nullable: false),
                    InputCount = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentCheckpoint", x => x.Id);
                    table.CheckConstraint("CK_AssessmentCheckpoint_State", "\"InputCount\">=0 AND length(\"PrefixHash\")=64 AND length(\"PayloadHash\")=64 AND jsonb_typeof(\"Payload\"::jsonb)='object'");
                    table.ForeignKey(
                        name: "FK_AssessmentCheckpoint_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentCheckpoint_Generations_FamilyId_GenerationId_Stud~",
                        columns: x => new { x.FamilyId, x.GenerationId, x.StudentId },
                        principalTable: "Generations",
                        principalColumns: new[] { "FamilyId", "Id", "StudentId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentCheckpoint_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Generations_FamilyId_IncrementalBaseGenerationId_StudentId",
                table: "Generations",
                columns: new[] { "FamilyId", "IncrementalBaseGenerationId", "StudentId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Generations_Incremental",
                table: "Generations",
                sql: "(\"CalculationMode\" IS NULL AND \"ProcessedInputCount\" IS NULL AND \"IncrementalBaseGenerationId\" IS NULL) OR (\"CalculationMode\" IS NOT NULL AND \"CalculationMode\" IN ('FullStream','FullStreamChangedPrefix','IncrementalAppend') AND \"ProcessedInputCount\" IS NOT NULL AND \"ProcessedInputCount\">=0 AND ((\"CalculationMode\"='IncrementalAppend' AND \"IncrementalBaseGenerationId\" IS NOT NULL AND \"IncrementalBaseGenerationId\"<>\"Id\") OR (\"CalculationMode\"<>'IncrementalAppend' AND \"IncrementalBaseGenerationId\" IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCheckpoint_FamilyId",
                table: "AssessmentCheckpoint",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCheckpoint_FamilyId_GenerationId_StudentId",
                table: "AssessmentCheckpoint",
                columns: new[] { "FamilyId", "GenerationId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCheckpoint_FamilyId_StudentId",
                table: "AssessmentCheckpoint",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCheckpoint_GenerationId",
                table: "AssessmentCheckpoint",
                column: "GenerationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Generations_Generations_FamilyId_IncrementalBaseGenerationI~",
                table: "Generations",
                columns: new[] { "FamilyId", "IncrementalBaseGenerationId", "StudentId" },
                principalTable: "Generations",
                principalColumns: new[] { "FamilyId", "Id", "StudentId" },
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.Sql("CREATE FUNCTION learning_checkpoint_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'assessment checkpoint is immutable' USING ERRCODE='23514'; END $$; CREATE TRIGGER assessment_checkpoint_immutable BEFORE UPDATE ON \"AssessmentCheckpoint\" FOR EACH ROW EXECUTE FUNCTION learning_checkpoint_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Generations_Generations_FamilyId_IncrementalBaseGenerationI~",
                table: "Generations");

            migrationBuilder.DropTable(
                name: "AssessmentCheckpoint");
            migrationBuilder.Sql("DROP FUNCTION learning_checkpoint_immutable();");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Generations_FamilyId_Id_StudentId",
                table: "Generations");

            migrationBuilder.DropIndex(
                name: "IX_Generations_FamilyId_IncrementalBaseGenerationId_StudentId",
                table: "Generations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Generations_Incremental",
                table: "Generations");

            migrationBuilder.DropColumn(
                name: "CalculationMode",
                table: "Generations");

            migrationBuilder.DropColumn(
                name: "IncrementalBaseGenerationId",
                table: "Generations");

            migrationBuilder.DropColumn(
                name: "ProcessedInputCount",
                table: "Generations");
        }
    }
}
