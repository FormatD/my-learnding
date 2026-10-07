using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BuilderSemanticCallLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_BuilderSemanticPreparation_FamilyId_Id",
                table: "BuilderSemanticPreparation",
                columns: new[] { "FamilyId", "Id" });

            migrationBuilder.CreateTable(
                name: "BuilderSemanticCall",
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
                    table.PrimaryKey("PK_BuilderSemanticCall", x => x.Id);
                    table.UniqueConstraint("AK_BuilderSemanticCall_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.CheckConstraint("CK_BuilderSemanticCall_Input", "length(\"InputHash\")=64 AND length(\"ModelConfigHash\")=64 AND \"AttemptNumber\">=1 AND \"RetryRound\">=0 AND \"CallNumber\">=1");
                    table.CheckConstraint("CK_BuilderSemanticCall_Lifecycle", "((\"Status\"='Started' AND \"FinishedAt\" IS NULL AND \"BudgetState\"='Reserved') OR (\"Status\"<>'Started' AND \"FinishedAt\" IS NOT NULL AND \"FinishedAt\">=\"StartedAt\")) AND (\"ElapsedMilliseconds\" IS NULL OR \"ElapsedMilliseconds\">=0) AND (\"BudgetState\"<>'Settled' OR (\"Status\"='Returned' AND \"ChargedCost\"=0 AND \"BillingStatus\" IN ('LocalMeasured','LocalNoCharge'))) AND ((\"Status\"='Denied')=(\"BudgetState\"='Denied'))");
                    table.CheckConstraint("CK_BuilderSemanticCall_Local", "\"Currency\" IS NULL AND (\"ChargedCost\" IS NULL OR \"ChargedCost\"=0) AND \"BillingStatus\" IN ('Unknown','LocalMeasured','LocalNoCharge') AND (\"InputTokens\" IS NULL OR \"InputTokens\">=0) AND (\"OutputTokens\" IS NULL OR \"OutputTokens\">=0)");
                    table.CheckConstraint("CK_BuilderSemanticCall_Payload", "jsonb_typeof(\"InputPayload\"::jsonb)='object' AND jsonb_typeof(\"ModelConfigPayload\"::jsonb)='object' AND jsonb_typeof(\"BudgetSnapshot\"::jsonb)='object'");
                    table.CheckConstraint("CK_BuilderSemanticCall_Status", "\"Status\" IN ('Started','Returned','Failed','Cancelled','Denied') AND \"BudgetState\" IN ('Reserved','Settled','Unresolved','Denied')");
                    table.ForeignKey(
                        name: "FK_BuilderSemanticCall_BuilderSemanticPreparation_FamilyId_Pre~",
                        columns: x => new { x.FamilyId, x.PreparationId },
                        principalTable: "BuilderSemanticPreparation",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticCall_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuilderSemanticReconciliation",
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
                    table.PrimaryKey("PK_BuilderSemanticReconciliation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticReconciliation_Accounts_FamilyId_ActorId",
                        columns: x => new { x.FamilyId, x.ActorId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticReconciliation_BuilderSemanticCall_FamilyId_~",
                        columns: x => new { x.FamilyId, x.CallId },
                        principalTable: "BuilderSemanticCall",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderSemanticReconciliation_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticCall_ExecutionId_CallNumber",
                table: "BuilderSemanticCall",
                columns: new[] { "ExecutionId", "CallNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticCall_FamilyId",
                table: "BuilderSemanticCall",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticCall_FamilyId_PreparationId",
                table: "BuilderSemanticCall",
                columns: new[] { "FamilyId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticReconciliation_CallId",
                table: "BuilderSemanticReconciliation",
                column: "CallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticReconciliation_FamilyId",
                table: "BuilderSemanticReconciliation",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticReconciliation_FamilyId_ActorId",
                table: "BuilderSemanticReconciliation",
                columns: new[] { "FamilyId", "ActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderSemanticReconciliation_FamilyId_CallId",
                table: "BuilderSemanticReconciliation",
                columns: new[] { "FamilyId", "CallId" });
            migrationBuilder.Sql("""
CREATE FUNCTION learning_semantic_call_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF OLD."Status"<>'Started' OR NEW."Status"='Started' OR
 (to_jsonb(NEW)-ARRAY['Status','FinishedAt','ElapsedMilliseconds','InputTokens','OutputTokens','ChargedCost','Currency','BillingStatus','OutputHash','ErrorCode','BudgetState']) IS DISTINCT FROM
 (to_jsonb(OLD)-ARRAY['Status','FinishedAt','ElapsedMilliseconds','InputTokens','OutputTokens','ChargedCost','Currency','BillingStatus','OutputHash','ErrorCode','BudgetState'])
 THEN RAISE EXCEPTION 'Semantic physical call facts cannot be rewritten' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER semantic_call_guard BEFORE UPDATE ON "BuilderSemanticCall" FOR EACH ROW EXECUTE FUNCTION learning_semantic_call_guard();
CREATE FUNCTION learning_semantic_reconciliation_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Semantic reconciliations are append-only' USING ERRCODE='23514'; END $$;
CREATE TRIGGER semantic_reconciliation_immutable BEFORE UPDATE ON "BuilderSemanticReconciliation" FOR EACH ROW EXECUTE FUNCTION learning_semantic_reconciliation_immutable();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuilderSemanticReconciliation");

            migrationBuilder.DropTable(
                name: "BuilderSemanticCall");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BuilderSemanticPreparation_FamilyId_Id",
                table: "BuilderSemanticPreparation");
            migrationBuilder.Sql("DROP FUNCTION learning_semantic_call_guard(); DROP FUNCTION learning_semantic_reconciliation_immutable();");
        }
    }
}
