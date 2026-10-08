using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SourceHumanReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceCheck",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Snapshot = table.Column<string>(type: "text", nullable: false),
                    SnapshotHash = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCheck", x => x.Id);
                    table.CheckConstraint("CK_SourceCheck_Evidence", "\"Operation\" IN ('Revision','Confirmation') AND length(\"Reason\") BETWEEN 1 AND 4000 AND length(\"SnapshotHash\")=64 AND jsonb_typeof(\"Snapshot\"::jsonb)='object'");
                    table.ForeignKey(
                        name: "FK_SourceCheck_Accounts_FamilyId_ActorId",
                        columns: x => new { x.FamilyId, x.ActorId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SourceCheck_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SourceCheck_Sources_FamilyId_OriginalSourceId",
                        columns: x => new { x.FamilyId, x.OriginalSourceId },
                        principalTable: "Sources",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SourceCheck_Sources_FamilyId_SourceId",
                        columns: x => new { x.FamilyId, x.SourceId },
                        principalTable: "Sources",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceCheck_FamilyId",
                table: "SourceCheck",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCheck_FamilyId_ActorId",
                table: "SourceCheck",
                columns: new[] { "FamilyId", "ActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceCheck_FamilyId_OriginalSourceId",
                table: "SourceCheck",
                columns: new[] { "FamilyId", "OriginalSourceId" });

            migrationBuilder.CreateIndex(name:"IX_SourceCheck_FamilyId_SourceId_Operation_SnapshotHash",table:"SourceCheck",columns:new[]{"FamilyId","SourceId","Operation","SnapshotHash"},unique:true);
            migrationBuilder.Sql("""
                CREATE FUNCTION learning_source_check_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'source check evidence is immutable'; END; $$;
                CREATE TRIGGER source_check_immutable BEFORE UPDATE ON "SourceCheck" FOR EACH ROW EXECUTE FUNCTION learning_source_check_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceCheck");
            migrationBuilder.Sql("DROP FUNCTION learning_source_check_immutable();");
        }
    }
}
