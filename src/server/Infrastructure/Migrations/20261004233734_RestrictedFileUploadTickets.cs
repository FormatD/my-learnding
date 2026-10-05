using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestrictedFileUploadTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileUploadTicket",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<int>(type: "integer", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CompletedFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CredentialHash = table.Column<string>(type: "text", nullable: false),
                    StagedBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileUploadTicket", x => x.Id);
                    table.CheckConstraint("CK_UploadTicket_Declaration", "\"Size\" BETWEEN 1 AND 10000000 AND length(\"Name\") BETWEEN 1 AND 200 AND \"Hash\" ~ '^[a-f0-9]{64}$' AND \"CredentialHash\" ~ '^[a-f0-9]{64}$' AND \"Purpose\" IN ('Attachment','LearningResource') AND \"ExpiresAt\">\"CreatedAt\" AND \"ExpiresAt\"<=\"CreatedAt\"+INTERVAL '15 minutes'");
                    table.CheckConstraint("CK_UploadTicket_State", "\"Status\" IN ('AwaitingBytes','Stored','Completed','Expired') AND ((\"Status\"='Stored' AND octet_length(\"StagedBytes\")=\"Size\") OR (\"Status\"<>'Stored' AND octet_length(\"StagedBytes\")=0)) AND ((\"Status\"='Completed' AND \"CompletedFileId\"=\"Id\" AND \"CompletedFileId\" IS NOT NULL) OR (\"Status\"<>'Completed' AND \"CompletedFileId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_FileUploadTicket_Accounts_FamilyId_OwnerAccountId",
                        columns: x => new { x.FamilyId, x.OwnerAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileUploadTicket_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileUploadTicket_PrivateFile_FamilyId_CompletedFileId",
                        columns: x => new { x.FamilyId, x.CompletedFileId },
                        principalTable: "PrivateFile",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadTicket_FamilyId",
                table: "FileUploadTicket",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadTicket_FamilyId_CompletedFileId",
                table: "FileUploadTicket",
                columns: new[] { "FamilyId", "CompletedFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadTicket_FamilyId_OwnerAccountId",
                table: "FileUploadTicket",
                columns: new[] { "FamilyId", "OwnerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadTicket_Status_ExpiresAt",
                table: "FileUploadTicket",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileUploadTicket");
        }
    }
}
