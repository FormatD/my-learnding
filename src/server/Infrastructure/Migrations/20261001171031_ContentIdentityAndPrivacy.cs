using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContentIdentityAndPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentIdentity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentIdentity", x => x.Id);
                    table.UniqueConstraint("AK_ContentIdentity_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ContentIdentity_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentRevision", x => x.Id);
                    table.UniqueConstraint("AK_ContentRevision_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ContentRevision_ContentIdentity_FamilyId_IdentityId",
                        columns: x => new { x.FamilyId, x.IdentityId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentRevision_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseItem_ContentIdentity_FamilyId_IdentityId",
                        columns: x => new { x.FamilyId, x.IdentityId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseItem_ContentRevision_FamilyId_RevisionId",
                        columns: x => new { x.FamilyId, x.RevisionId },
                        principalTable: "ContentRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseItem_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseItem_Releases_FamilyId_ReleaseId",
                        columns: x => new { x.FamilyId, x.ReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_FamilyId_StudentId",
                table: "Reviews",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_FamilyId_StudentId",
                table: "Outbox",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Masteries_FamilyId_StudentId",
                table: "Masteries",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Generations_FamilyId_StudentId",
                table: "Generations",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_FamilyId_StudentId",
                table: "Evidence",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_FamilyId_StudentId",
                table: "Attempts",
                columns: new[] { "FamilyId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentIdentity_FamilyId",
                table: "ContentIdentity",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentIdentity_FamilyId_EntityType_Code",
                table: "ContentIdentity",
                columns: new[] { "FamilyId", "EntityType", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentRevision_FamilyId",
                table: "ContentRevision",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentRevision_FamilyId_IdentityId",
                table: "ContentRevision",
                columns: new[] { "FamilyId", "IdentityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseItem_FamilyId",
                table: "ReleaseItem",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseItem_FamilyId_IdentityId",
                table: "ReleaseItem",
                columns: new[] { "FamilyId", "IdentityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseItem_FamilyId_ReleaseId",
                table: "ReleaseItem",
                columns: new[] { "FamilyId", "ReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseItem_FamilyId_RevisionId",
                table: "ReleaseItem",
                columns: new[] { "FamilyId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseItem_ReleaseId_EntityType_IdentityId",
                table: "ReleaseItem",
                columns: new[] { "ReleaseId", "EntityType", "IdentityId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Attempts_Students_FamilyId_StudentId",
                table: "Attempts",
                columns: new[] { "FamilyId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Evidence_Students_FamilyId_StudentId",
                table: "Evidence",
                columns: new[] { "FamilyId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Generations_Students_FamilyId_StudentId",
                table: "Generations",
                columns: new[] { "FamilyId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Masteries_Students_FamilyId_StudentId",
                table: "Masteries",
                columns: new[] { "FamilyId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Outbox_Students_FamilyId_StudentId",
                table: "Outbox",
                columns: new[] { "FamilyId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_Students_FamilyId_StudentId",
                table: "Reviews",
                columns: new[] { "FamilyId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attempts_Students_FamilyId_StudentId",
                table: "Attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_Evidence_Students_FamilyId_StudentId",
                table: "Evidence");

            migrationBuilder.DropForeignKey(
                name: "FK_Generations_Students_FamilyId_StudentId",
                table: "Generations");

            migrationBuilder.DropForeignKey(
                name: "FK_Masteries_Students_FamilyId_StudentId",
                table: "Masteries");

            migrationBuilder.DropForeignKey(
                name: "FK_Outbox_Students_FamilyId_StudentId",
                table: "Outbox");

            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_Students_FamilyId_StudentId",
                table: "Reviews");

            migrationBuilder.DropTable(
                name: "ReleaseItem");

            migrationBuilder.DropTable(
                name: "ContentRevision");

            migrationBuilder.DropTable(
                name: "ContentIdentity");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_FamilyId_StudentId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Outbox_FamilyId_StudentId",
                table: "Outbox");

            migrationBuilder.DropIndex(
                name: "IX_Masteries_FamilyId_StudentId",
                table: "Masteries");

            migrationBuilder.DropIndex(
                name: "IX_Generations_FamilyId_StudentId",
                table: "Generations");

            migrationBuilder.DropIndex(
                name: "IX_Evidence_FamilyId_StudentId",
                table: "Evidence");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_FamilyId_StudentId",
                table: "Attempts");
        }
    }
}
