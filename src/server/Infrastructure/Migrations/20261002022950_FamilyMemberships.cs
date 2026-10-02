using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FamilyMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerAccountId",
                table: "Families",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Accounts_FamilyId_Id",
                table: "Accounts",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "FamilyMembership",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Roles = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMembership", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyMembership_Accounts_FamilyId_AccountId",
                        columns: x => new { x.FamilyId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyMembership_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Families_Id_OwnerAccountId",
                table: "Families",
                columns: new[] { "Id", "OwnerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembership_FamilyId",
                table: "FamilyMembership",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembership_FamilyId_AccountId",
                table: "FamilyMembership",
                columns: new[] { "FamilyId", "AccountId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Families_Accounts_Id_OwnerAccountId",
                table: "Families",
                columns: new[] { "Id", "OwnerAccountId" },
                principalTable: "Accounts",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Restrict);
            // Preserve existing accounts' permissions; do not infer an owner in multi-account families.
            migrationBuilder.Sql("""
                INSERT INTO "FamilyMembership" ("Id", "AccountId", "Roles", "FamilyId", "CreatedAt")
                SELECT "Id", "Id", "Roles", "FamilyId", "CreatedAt" FROM "Accounts";
                UPDATE "Families" f SET "OwnerAccountId" = a."Id"
                FROM "Accounts" a
                WHERE a."FamilyId" = f."Id"
                  AND 'Parent' = ANY(string_to_array(a."Roles", ','))
                  AND (SELECT count(*) FROM "Accounts" existing WHERE existing."FamilyId" = f."Id") = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Families_Accounts_Id_OwnerAccountId",
                table: "Families");

            migrationBuilder.DropTable(
                name: "FamilyMembership");

            migrationBuilder.DropIndex(
                name: "IX_Families_Id_OwnerAccountId",
                table: "Families");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Accounts_FamilyId_Id",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OwnerAccountId",
                table: "Families");
        }
    }
}
