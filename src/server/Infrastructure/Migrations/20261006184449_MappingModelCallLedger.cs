using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MappingModelCallLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MappingPreparation_FamilyId_Id",
                table: "MappingPreparation",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "MappingModelCall",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetryRound = table.Column<int>(type: "integer", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    CallNumber = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    ModelConfigHash = table.Column<string>(type: "text", nullable: false),
                    ModelConfigPayload = table.Column<string>(type: "text", nullable: false),
                    InputPayload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ElapsedMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    ChargedCost = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    BillingStatus = table.Column<string>(type: "text", nullable: false),
                    OutputHash = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    BudgetDay = table.Column<DateOnly>(type: "date", nullable: false),
                    BudgetSnapshot = table.Column<string>(type: "text", nullable: false),
                    BudgetState = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingModelCall", x => x.Id);
                    table.UniqueConstraint("AK_MappingModelCall_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.CheckConstraint("CK_MappingModelCall_Input", "length(\"InputHash\")=64 AND length(\"ModelConfigHash\")=64 AND \"AttemptNumber\">=1 AND \"RetryRound\">=0 AND \"CallNumber\">=1");
                    table.CheckConstraint("CK_MappingModelCall_Lifecycle", "((\"Status\"='Started' AND \"FinishedAt\" IS NULL AND \"BudgetState\"='Reserved') OR (\"Status\"<>'Started' AND \"FinishedAt\" IS NOT NULL AND \"FinishedAt\">=\"StartedAt\")) AND (\"ElapsedMilliseconds\" IS NULL OR \"ElapsedMilliseconds\">=0) AND (\"BudgetState\"<>'Settled' OR (\"Status\"='Returned' AND \"ChargedCost\"=0 AND \"BillingStatus\" IN ('LocalMeasured','LocalNoCharge'))) AND ((\"Status\"='Denied')=(\"BudgetState\"='Denied'))");
                    table.CheckConstraint("CK_MappingModelCall_Local", "\"Currency\" IS NULL AND (\"ChargedCost\" IS NULL OR \"ChargedCost\"=0) AND \"BillingStatus\" IN ('Unknown','LocalMeasured','LocalNoCharge') AND (\"InputTokens\" IS NULL OR \"InputTokens\">=0) AND (\"OutputTokens\" IS NULL OR \"OutputTokens\">=0)");
                    table.CheckConstraint("CK_MappingModelCall_Payload", "jsonb_typeof(\"InputPayload\"::jsonb)='object' AND jsonb_typeof(\"ModelConfigPayload\"::jsonb)='object' AND jsonb_typeof(\"BudgetSnapshot\"::jsonb)='object'");
                    table.CheckConstraint("CK_MappingModelCall_Status", "\"Status\" IN ('Started','Returned','Failed','Cancelled','Denied') AND \"BudgetState\" IN ('Reserved','Settled','Unresolved','Denied')");
                    table.ForeignKey(
                        name: "FK_MappingModelCall_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingModelCall_MappingPreparation_FamilyId_PreparationId",
                        columns: x => new { x.FamilyId, x.PreparationId },
                        principalTable: "MappingPreparation",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingCallReconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ReceiptReference = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingCallReconciliation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MappingCallReconciliation_Accounts_FamilyId_ActorId",
                        columns: x => new { x.FamilyId, x.ActorId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingCallReconciliation_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MappingCallReconciliation_MappingModelCall_FamilyId_CallId",
                        columns: x => new { x.FamilyId, x.CallId },
                        principalTable: "MappingModelCall",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MappingCallReconciliation_CallId",
                table: "MappingCallReconciliation",
                column: "CallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingCallReconciliation_FamilyId",
                table: "MappingCallReconciliation",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingCallReconciliation_FamilyId_ActorId",
                table: "MappingCallReconciliation",
                columns: new[] { "FamilyId", "ActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingCallReconciliation_FamilyId_CallId",
                table: "MappingCallReconciliation",
                columns: new[] { "FamilyId", "CallId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingModelCall_ExecutionId_CallNumber",
                table: "MappingModelCall",
                columns: new[] { "ExecutionId", "CallNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingModelCall_FamilyId",
                table: "MappingModelCall",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingModelCall_FamilyId_PreparationId",
                table: "MappingModelCall",
                columns: new[] { "FamilyId", "PreparationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MappingCallReconciliation");

            migrationBuilder.DropTable(
                name: "MappingModelCall");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MappingPreparation_FamilyId_Id",
                table: "MappingPreparation");
        }
    }
}
