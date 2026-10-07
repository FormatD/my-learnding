using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BuilderSemanticPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuilderSemanticPreparation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Snapshot = table.Column<string>(type: "text", nullable: false),
                    SnapshotHash = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuilderSemanticPreparation", x => x.Id);
                    table.CheckConstraint("CK_BuilderSemanticPreparation_Snapshot", "jsonb_typeof(\"Snapshot\"::jsonb)='object' AND length(\"SnapshotHash\")=64 AND length(\"InputHash\")=64");
                    table.ForeignKey(
                        name: "FK_BuilderSemanticPreparation_Accounts_FamilyId_RequestedBy",
                        columns: x => new { x.FamilyId, x.RequestedBy },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticPreparation_BackgroundJob_FamilyId_JobId",
                        columns: x => new { x.FamilyId, x.JobId },
                        principalTable: "BackgroundJob",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticPreparation_BuilderRuns_FamilyId_RunId",
                        columns: x => new { x.FamilyId, x.RunId },
                        principalTable: "BuilderRuns",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticPreparation_Candidates_FamilyId_CandidateId",
                        columns: x => new { x.FamilyId, x.CandidateId },
                        principalTable: "Candidates",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticPreparation_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticPreparation_Releases_FamilyId_LibraryRelease~",
                        columns: x => new { x.FamilyId, x.LibraryReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticPreparation_FamilyId",
                table: "BuilderSemanticPreparation",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticPreparation_FamilyId_CandidateId",
                table: "BuilderSemanticPreparation",
                columns: new[] { "FamilyId", "CandidateId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticPreparation_FamilyId_InputHash",
                table: "BuilderSemanticPreparation",
                columns: new[] { "FamilyId", "InputHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticPreparation_FamilyId_JobId",
                table: "BuilderSemanticPreparation",
                columns: new[] { "FamilyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticPreparation_FamilyId_LibraryReleaseId",
                table: "BuilderSemanticPreparation",
                columns: new[] { "FamilyId", "LibraryReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticPreparation_FamilyId_RequestedBy",
                table: "BuilderSemanticPreparation",
                columns: new[] { "FamilyId", "RequestedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticPreparation_FamilyId_RunId",
                table: "BuilderSemanticPreparation",
                columns: new[] { "FamilyId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticPreparation_JobId",
                table: "BuilderSemanticPreparation",
                column: "JobId",
                unique: true);
            migrationBuilder.Sql("CREATE FUNCTION learning_semantic_preparation_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Semantic input snapshots are immutable' USING ERRCODE='23514'; END $$; CREATE TRIGGER semantic_preparation_immutable BEFORE UPDATE ON \"BuilderSemanticPreparation\" FOR EACH ROW EXECUTE FUNCTION learning_semantic_preparation_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuilderSemanticPreparation");
            migrationBuilder.Sql("DROP FUNCTION learning_semantic_preparation_immutable();");
        }
    }
}
