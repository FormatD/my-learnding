using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LearningMappingReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MappingSetRevisionId",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionRevisionId",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MappingSetRevisionId",
                table: "Evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MappingSetRevisionId",
                table: "CorrectionItem",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionRevisionId",
                table: "CorrectionItem",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MappingSetRevisionId",
                table: "Attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionRevisionId",
                table: "Attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_FamilyId_MappingSetRevisionId",
                table: "Sessions",
                columns: new[] { "FamilyId", "MappingSetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_FamilyId_QuestionRevisionId",
                table: "Sessions",
                columns: new[] { "FamilyId", "QuestionRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_FamilyId_MappingSetRevisionId",
                table: "Evidence",
                columns: new[] { "FamilyId", "MappingSetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionItem_FamilyId_MappingSetRevisionId",
                table: "CorrectionItem",
                columns: new[] { "FamilyId", "MappingSetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionItem_FamilyId_QuestionRevisionId",
                table: "CorrectionItem",
                columns: new[] { "FamilyId", "QuestionRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_FamilyId_MappingSetRevisionId",
                table: "Attempts",
                columns: new[] { "FamilyId", "MappingSetRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_FamilyId_QuestionRevisionId",
                table: "Attempts",
                columns: new[] { "FamilyId", "QuestionRevisionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Attempts_ContentRevision_FamilyId_QuestionRevisionId",
                table: "Attempts",
                columns: new[] { "FamilyId", "QuestionRevisionId" },
                principalTable: "ContentRevision",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Attempts_MappingSetRevision_FamilyId_MappingSetRevisionId",
                table: "Attempts",
                columns: new[] { "FamilyId", "MappingSetRevisionId" },
                principalTable: "MappingSetRevision",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CorrectionItem_ContentRevision_FamilyId_QuestionRevisionId",
                table: "CorrectionItem",
                columns: new[] { "FamilyId", "QuestionRevisionId" },
                principalTable: "ContentRevision",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CorrectionItem_MappingSetRevision_FamilyId_MappingSetRevisi~",
                table: "CorrectionItem",
                columns: new[] { "FamilyId", "MappingSetRevisionId" },
                principalTable: "MappingSetRevision",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Evidence_MappingSetRevision_FamilyId_MappingSetRevisionId",
                table: "Evidence",
                columns: new[] { "FamilyId", "MappingSetRevisionId" },
                principalTable: "MappingSetRevision",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_ContentRevision_FamilyId_QuestionRevisionId",
                table: "Sessions",
                columns: new[] { "FamilyId", "QuestionRevisionId" },
                principalTable: "ContentRevision",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_MappingSetRevision_FamilyId_MappingSetRevisionId",
                table: "Sessions",
                columns: new[] { "FamilyId", "MappingSetRevisionId" },
                principalTable: "MappingSetRevision",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attempts_ContentRevision_FamilyId_QuestionRevisionId",
                table: "Attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_Attempts_MappingSetRevision_FamilyId_MappingSetRevisionId",
                table: "Attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_CorrectionItem_ContentRevision_FamilyId_QuestionRevisionId",
                table: "CorrectionItem");

            migrationBuilder.DropForeignKey(
                name: "FK_CorrectionItem_MappingSetRevision_FamilyId_MappingSetRevisi~",
                table: "CorrectionItem");

            migrationBuilder.DropForeignKey(
                name: "FK_Evidence_MappingSetRevision_FamilyId_MappingSetRevisionId",
                table: "Evidence");

            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_ContentRevision_FamilyId_QuestionRevisionId",
                table: "Sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_MappingSetRevision_FamilyId_MappingSetRevisionId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_FamilyId_MappingSetRevisionId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_FamilyId_QuestionRevisionId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Evidence_FamilyId_MappingSetRevisionId",
                table: "Evidence");

            migrationBuilder.DropIndex(
                name: "IX_CorrectionItem_FamilyId_MappingSetRevisionId",
                table: "CorrectionItem");

            migrationBuilder.DropIndex(
                name: "IX_CorrectionItem_FamilyId_QuestionRevisionId",
                table: "CorrectionItem");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_FamilyId_MappingSetRevisionId",
                table: "Attempts");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_FamilyId_QuestionRevisionId",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "MappingSetRevisionId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "QuestionRevisionId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "MappingSetRevisionId",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "MappingSetRevisionId",
                table: "CorrectionItem");

            migrationBuilder.DropColumn(
                name: "QuestionRevisionId",
                table: "CorrectionItem");

            migrationBuilder.DropColumn(
                name: "MappingSetRevisionId",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "QuestionRevisionId",
                table: "Attempts");
        }
    }
}
