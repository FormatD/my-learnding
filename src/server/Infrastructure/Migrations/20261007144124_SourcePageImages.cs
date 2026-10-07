using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SourcePageImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceImage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceHash = table.Column<string>(type: "text", nullable: false),
                    DocumentHash = table.Column<string>(type: "text", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    PrintedPage = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceImage", x => x.Id);
                    table.CheckConstraint("CK_SourceImage_Provenance", "length(\"SourceHash\")=64 AND \"DocumentHash\" ~ '^[a-f0-9]{64}$' AND \"Page\" BETWEEN 1 AND 10000 AND length(\"PrintedPage\") BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_SourceImage_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SourceImage_PrivateFile_FamilyId_FileId",
                        columns: x => new { x.FamilyId, x.FileId },
                        principalTable: "PrivateFile",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SourceImage_Sources_FamilyId_SourceId",
                        columns: x => new { x.FamilyId, x.SourceId },
                        principalTable: "Sources",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceImage_FamilyId",
                table: "SourceImage",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceImage_FamilyId_FileId",
                table: "SourceImage",
                columns: new[] { "FamilyId", "FileId" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceImage_FamilyId_SourceId_DocumentHash_Page",
                table: "SourceImage",
                columns: new[] { "FamilyId", "SourceId", "DocumentHash", "Page" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION learning_source_image_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'source image provenance is immutable'; END; $$;
                CREATE TRIGGER source_image_immutable BEFORE UPDATE ON "SourceImage" FOR EACH ROW EXECUTE FUNCTION learning_source_image_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceImage");
            migrationBuilder.Sql("DROP FUNCTION learning_source_image_immutable();");
        }
    }
}
