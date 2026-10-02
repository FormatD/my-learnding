using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeChangeProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KCChangeProposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalType = table.Column<string>(type: "text", nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    FromReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KCChangeProposal", x => x.Id);
                    table.UniqueConstraint("AK_KCChangeProposal_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_KCChangeProposal_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KCChangeProposal_Releases_FamilyId_EffectiveReleaseId",
                        columns: x => new { x.FamilyId, x.EffectiveReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KCChangeProposal_Releases_FamilyId_FromReleaseId",
                        columns: x => new { x.FamilyId, x.FromReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KCChangeProposalItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<string>(type: "text", nullable: false),
                    KCId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposedRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KCChangeProposalItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KCChangeProposalItem_ContentIdentity_FamilyId_KCId",
                        columns: x => new { x.FamilyId, x.KCId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KCChangeProposalItem_ContentRevision_FamilyId_ProposedRevis~",
                        columns: x => new { x.FamilyId, x.ProposedRevisionId },
                        principalTable: "ContentRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KCChangeProposalItem_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KCChangeProposalItem_KCChangeProposal_FamilyId_ProposalId",
                        columns: x => new { x.FamilyId, x.ProposalId },
                        principalTable: "KCChangeProposal",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KCProposalEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Snapshot = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KCProposalEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KCProposalEvent_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KCProposalEvent_KCChangeProposal_FamilyId_ProposalId",
                        columns: x => new { x.FamilyId, x.ProposalId },
                        principalTable: "KCChangeProposal",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeMigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromKCId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToKCId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MigrationType = table.Column<string>(type: "text", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric", nullable: true),
                    EffectiveReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidencePolicy = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeMigration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeMigration_ContentIdentity_FamilyId_FromKCId",
                        columns: x => new { x.FamilyId, x.FromKCId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeMigration_ContentIdentity_FamilyId_ToKCId",
                        columns: x => new { x.FamilyId, x.ToKCId },
                        principalTable: "ContentIdentity",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeMigration_ContentRevision_FamilyId_FromRevisionId",
                        columns: x => new { x.FamilyId, x.FromRevisionId },
                        principalTable: "ContentRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeMigration_ContentRevision_FamilyId_ToRevisionId",
                        columns: x => new { x.FamilyId, x.ToRevisionId },
                        principalTable: "ContentRevision",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeMigration_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeMigration_KCChangeProposal_FamilyId_ProposalId",
                        columns: x => new { x.FamilyId, x.ProposalId },
                        principalTable: "KCChangeProposal",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeMigration_Releases_FamilyId_EffectiveReleaseId",
                        columns: x => new { x.FamilyId, x.EffectiveReleaseId },
                        principalTable: "Releases",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KCChangeProposal_FamilyId",
                table: "KCChangeProposal",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_KCChangeProposal_FamilyId_EffectiveReleaseId",
                table: "KCChangeProposal",
                columns: new[] { "FamilyId", "EffectiveReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_KCChangeProposal_FamilyId_FromReleaseId",
                table: "KCChangeProposal",
                columns: new[] { "FamilyId", "FromReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_KCChangeProposalItem_FamilyId",
                table: "KCChangeProposalItem",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_KCChangeProposalItem_FamilyId_KCId",
                table: "KCChangeProposalItem",
                columns: new[] { "FamilyId", "KCId" });

            migrationBuilder.CreateIndex(
                name: "IX_KCChangeProposalItem_FamilyId_ProposalId",
                table: "KCChangeProposalItem",
                columns: new[] { "FamilyId", "ProposalId" });

            migrationBuilder.CreateIndex(
                name: "IX_KCChangeProposalItem_FamilyId_ProposedRevisionId",
                table: "KCChangeProposalItem",
                columns: new[] { "FamilyId", "ProposedRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_KCChangeProposalItem_ProposalId_Side_KCId",
                table: "KCChangeProposalItem",
                columns: new[] { "ProposalId", "Side", "KCId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KCProposalEvent_FamilyId",
                table: "KCProposalEvent",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_KCProposalEvent_FamilyId_ProposalId",
                table: "KCProposalEvent",
                columns: new[] { "FamilyId", "ProposalId" });

            migrationBuilder.CreateIndex(
                name: "IX_KCProposalEvent_ProposalId_Version",
                table: "KCProposalEvent",
                columns: new[] { "ProposalId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMigration_FamilyId",
                table: "KnowledgeMigration",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMigration_FamilyId_EffectiveReleaseId",
                table: "KnowledgeMigration",
                columns: new[] { "FamilyId", "EffectiveReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMigration_FamilyId_FromKCId",
                table: "KnowledgeMigration",
                columns: new[] { "FamilyId", "FromKCId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMigration_FamilyId_FromRevisionId",
                table: "KnowledgeMigration",
                columns: new[] { "FamilyId", "FromRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMigration_FamilyId_ProposalId",
                table: "KnowledgeMigration",
                columns: new[] { "FamilyId", "ProposalId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMigration_FamilyId_ToKCId",
                table: "KnowledgeMigration",
                columns: new[] { "FamilyId", "ToKCId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMigration_FamilyId_ToRevisionId",
                table: "KnowledgeMigration",
                columns: new[] { "FamilyId", "ToRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMigration_ProposalId_FromKCId_ToKCId",
                table: "KnowledgeMigration",
                columns: new[] { "ProposalId", "FromKCId", "ToKCId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KCChangeProposalItem");

            migrationBuilder.DropTable(
                name: "KCProposalEvent");

            migrationBuilder.DropTable(
                name: "KnowledgeMigration");

            migrationBuilder.DropTable(
                name: "KCChangeProposal");
        }
    }
}
