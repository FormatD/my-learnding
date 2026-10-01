using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetrievalAndAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Candidates_FamilyId_Id",
                table: "Candidates",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "Alias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KCId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Normalized = table.Column<string>(type: "text", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alias_Candidates_FamilyId_CandidateId",
                        columns: x => new { x.FamilyId, x.CandidateId },
                        principalTable: "Candidates",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Alias_ContentIdentity_FamilyId_KCId",
                        columns: x => new { x.FamilyId, x.KCId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Alias_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Embedding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    Space = table.Column<string>(type: "text", nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    TextHash = table.Column<string>(type: "text", nullable: false),
                    Vector = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Embedding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Embedding_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alias_FamilyId",
                table: "Alias",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_Alias_FamilyId_CandidateId",
                table: "Alias",
                columns: new[] { "FamilyId", "CandidateId" });

            migrationBuilder.CreateIndex(
                name: "IX_Alias_FamilyId_KCId_Normalized",
                table: "Alias",
                columns: new[] { "FamilyId", "KCId", "Normalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Embedding_FamilyId",
                table: "Embedding",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_Embedding_FamilyId_EntityRevisionId_Space",
                table: "Embedding",
                columns: new[] { "FamilyId", "EntityRevisionId", "Space" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alias");

            migrationBuilder.DropTable(
                name: "Embedding");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Candidates_FamilyId_Id",
                table: "Candidates");
        }
    }
}
