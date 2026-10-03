using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BuilderBudgetReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BudgetDay",
                table: "BuilderCall",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BudgetSnapshot",
                table: "BuilderCall",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BudgetState",
                table: "BuilderCall",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotePayload",
                table: "BuilderCall",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedCost",
                table: "BuilderCall",
                type: "numeric(16,6)",
                precision: 16,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReservedTokens",
                table: "BuilderCall",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BuilderCall_FamilyId_Id",
                table: "BuilderCall",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "BuilderBudgetPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyCostLimit = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: false),
                    DailyTokenLimit = table.Column<long>(type: "bigint", nullable: false),
                    PerCallCostLimit = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: false),
                    PerCallTokenLimit = table.Column<long>(type: "bigint", nullable: false),
                    MaxConcurrentCalls = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuilderBudgetPolicy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuilderBudgetPolicy_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuilderBudgetReconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargedCost = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ReceiptReference = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuilderBudgetReconciliation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuilderBudgetReconciliation_Accounts_FamilyId_ActorId",
                        columns: x => new { x.FamilyId, x.ActorId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderBudgetReconciliation_BuilderCall_FamilyId_CallId",
                        columns: x => new { x.FamilyId, x.CallId },
                        principalTable: "BuilderCall",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderBudgetReconciliation_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderBudgetPolicy_FamilyId",
                table: "BuilderBudgetPolicy",
                column: "FamilyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuilderBudgetReconciliation_CallId",
                table: "BuilderBudgetReconciliation",
                column: "CallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuilderBudgetReconciliation_FamilyId",
                table: "BuilderBudgetReconciliation",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderBudgetReconciliation_FamilyId_ActorId",
                table: "BuilderBudgetReconciliation",
                columns: new[] { "FamilyId", "ActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderBudgetReconciliation_FamilyId_CallId",
                table: "BuilderBudgetReconciliation",
                columns: new[] { "FamilyId", "CallId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuilderBudgetPolicy");

            migrationBuilder.DropTable(
                name: "BuilderBudgetReconciliation");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BuilderCall_FamilyId_Id",
                table: "BuilderCall");

            migrationBuilder.DropColumn(
                name: "BudgetDay",
                table: "BuilderCall");

            migrationBuilder.DropColumn(
                name: "BudgetSnapshot",
                table: "BuilderCall");

            migrationBuilder.DropColumn(
                name: "BudgetState",
                table: "BuilderCall");

            migrationBuilder.DropColumn(
                name: "QuotePayload",
                table: "BuilderCall");

            migrationBuilder.DropColumn(
                name: "ReservedCost",
                table: "BuilderCall");

            migrationBuilder.DropColumn(
                name: "ReservedTokens",
                table: "BuilderCall");
        }
    }
}
