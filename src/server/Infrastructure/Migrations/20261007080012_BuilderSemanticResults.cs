using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BuilderSemanticResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuilderSemanticResponse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutputPayload = table.Column<string>(type: "text", nullable: false),
                    OutputHash = table.Column<string>(type: "text", nullable: false),
                    OutputComplete = table.Column<bool>(type: "boolean", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuilderSemanticResponse", x => x.Id);
                    table.UniqueConstraint("AK_BuilderSemanticResponse_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.CheckConstraint("CK_BuilderSemanticResponse_Output", "length(\"OutputHash\")=64 AND length(\"OutputPayload\")<=2000000");
                    table.ForeignKey(
                        name: "FK_BuilderSemanticResponse_BuilderSemanticCall_FamilyId_CallId",
                        columns: x => new { x.FamilyId, x.CallId },
                        principalTable: "BuilderSemanticCall",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticResponse_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuilderSemanticSuggestion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultPayload = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuilderSemanticSuggestion", x => x.Id);
                    table.CheckConstraint("CK_BuilderSemanticSuggestion_Result", "jsonb_typeof(\"ResultPayload\"::jsonb)='object' AND length(\"InputHash\")=64");
                    table.ForeignKey(
                        name: "FK_BuilderSemanticSuggestion_BuilderSemanticPreparation_Family~",
                        columns: x => new { x.FamilyId, x.PreparationId },
                        principalTable: "BuilderSemanticPreparation",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticSuggestion_BuilderSemanticResponse_FamilyId_~",
                        columns: x => new { x.FamilyId, x.ResponseId },
                        principalTable: "BuilderSemanticResponse",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticSuggestion_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticResponse_CallId",
                table: "BuilderSemanticResponse",
                column: "CallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticResponse_FamilyId",
                table: "BuilderSemanticResponse",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticResponse_FamilyId_CallId",
                table: "BuilderSemanticResponse",
                columns: new[] { "FamilyId", "CallId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticSuggestion_FamilyId",
                table: "BuilderSemanticSuggestion",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticSuggestion_FamilyId_PreparationId",
                table: "BuilderSemanticSuggestion",
                columns: new[] { "FamilyId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticSuggestion_FamilyId_ResponseId",
                table: "BuilderSemanticSuggestion",
                columns: new[] { "FamilyId", "ResponseId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticSuggestion_PreparationId",
                table: "BuilderSemanticSuggestion",
                column: "PreparationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticSuggestion_ResponseId",
                table: "BuilderSemanticSuggestion",
                column: "ResponseId",
                unique: true);
            migrationBuilder.Sql("""
            CREATE FUNCTION learning_semantic_output_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Original semantic output cannot be rewritten' USING ERRCODE='23514'; END $$;
            CREATE TRIGGER semantic_response_immutable BEFORE UPDATE ON "BuilderSemanticResponse" FOR EACH ROW EXECUTE FUNCTION learning_semantic_output_immutable();
            CREATE TRIGGER semantic_suggestion_immutable BEFORE UPDATE ON "BuilderSemanticSuggestion" FOR EACH ROW EXECUTE FUNCTION learning_semantic_output_immutable();
            """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuilderSemanticSuggestion");

            migrationBuilder.DropTable(
                name: "BuilderSemanticResponse");
            migrationBuilder.Sql("DROP FUNCTION learning_semantic_output_immutable();");

        }
    }
}
