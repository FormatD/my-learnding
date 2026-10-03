using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MappingPreparationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MappingPreparation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTitle = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    Snapshot = table.Column<string>(type: "text", nullable: false),
                    SnapshotHash = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingPreparation", x => x.Id);
                    table.CheckConstraint("CK_MappingPreparation_Snapshot", "jsonb_typeof(\"Snapshot\"::jsonb)='object' AND length(\"SnapshotHash\")=64 AND length(\"InputHash\")=64 AND \"RunId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_MappingPreparation_Accounts_FamilyId_RequestedBy",
                        columns: x => new { x.FamilyId, x.RequestedBy },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingPreparation_BackgroundJob_FamilyId_JobId",
                        columns: x => new { x.FamilyId, x.JobId },
                        principalTable: "BackgroundJob",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingPreparation_Drafts_FamilyId_SourceDraftId",
                        columns: x => new { x.FamilyId, x.SourceDraftId },
                        principalTable: "Drafts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingPreparation_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingPreparation_Releases_FamilyId_LibraryReleaseId",
                        columns: x => new { x.FamilyId, x.LibraryReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MappingPreparation_FamilyId",
                table: "MappingPreparation",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingPreparation_FamilyId_InputHash",
                table: "MappingPreparation",
                columns: new[] { "FamilyId", "InputHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingPreparation_FamilyId_JobId",
                table: "MappingPreparation",
                columns: new[] { "FamilyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingPreparation_FamilyId_LibraryReleaseId",
                table: "MappingPreparation",
                columns: new[] { "FamilyId", "LibraryReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingPreparation_FamilyId_RequestedBy",
                table: "MappingPreparation",
                columns: new[] { "FamilyId", "RequestedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingPreparation_FamilyId_SourceDraftId",
                table: "MappingPreparation",
                columns: new[] { "FamilyId", "SourceDraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingPreparation_JobId",
                table: "MappingPreparation",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingPreparation_RunId",
                table: "MappingPreparation",
                column: "RunId",
                unique: true);
            migrationBuilder.Sql("CREATE FUNCTION learning_mapping_preparation_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Mapping input snapshots are immutable' USING ERRCODE='23514'; END $$; CREATE TRIGGER mapping_preparation_immutable BEFORE UPDATE ON \"MappingPreparation\" FOR EACH ROW EXECUTE FUNCTION learning_mapping_preparation_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MappingPreparation");
            migrationBuilder.Sql("DROP FUNCTION learning_mapping_preparation_immutable();");
        }
    }
}
