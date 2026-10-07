using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableModelEmbeddingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelEmbeddingIndex",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryHash = table.Column<string>(type: "text", nullable: false),
                    SpaceId = table.Column<string>(type: "text", nullable: false),
                    ConfigurationPayload = table.Column<string>(type: "text", nullable: false),
                    LibraryPayload = table.Column<string>(type: "text", nullable: false),
                    VectorsPayload = table.Column<string>(type: "text", nullable: false),
                    SnapshotHash = table.Column<string>(type: "text", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelEmbeddingIndex", x => x.Id);
                    table.CheckConstraint("CK_ModelEmbeddingIndex_Snapshot", "\"Count\" BETWEEN 1 AND 512 AND \"LibraryHash\" ~ '^[a-f0-9]{64}$' AND \"SnapshotHash\" ~ '^[a-f0-9]{64}$' AND jsonb_typeof(\"ConfigurationPayload\"::jsonb)='object' AND jsonb_typeof(\"LibraryPayload\"::jsonb)='array' AND jsonb_typeof(\"VectorsPayload\"::jsonb)='array' AND jsonb_array_length(\"LibraryPayload\"::jsonb)=\"Count\" AND jsonb_array_length(\"VectorsPayload\"::jsonb)=\"Count\" AND octet_length(\"ConfigurationPayload\")+octet_length(\"LibraryPayload\")+octet_length(\"VectorsPayload\")<=32000000");
                    table.ForeignKey(
                        name: "FK_ModelEmbeddingIndex_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ModelEmbeddingIndex_Releases_FamilyId_LibraryReleaseId",
                        columns: x => new { x.FamilyId, x.LibraryReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelEmbeddingIndex_FamilyId",
                table: "ModelEmbeddingIndex",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelEmbeddingIndex_FamilyId_LibraryReleaseId_SpaceId",
                table: "ModelEmbeddingIndex",
                columns: new[] { "FamilyId", "LibraryReleaseId", "SpaceId" },
                unique: true);
            migrationBuilder.Sql("""
            CREATE FUNCTION learning_model_embedding_index_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Original model embedding index cannot be rewritten' USING ERRCODE='23514'; END $$;
            CREATE TRIGGER model_embedding_index_immutable BEFORE UPDATE ON "ModelEmbeddingIndex" FOR EACH ROW EXECUTE FUNCTION learning_model_embedding_index_immutable();
            """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelEmbeddingIndex");
            migrationBuilder.Sql("DROP FUNCTION learning_model_embedding_index_immutable();");
        }
    }
}
