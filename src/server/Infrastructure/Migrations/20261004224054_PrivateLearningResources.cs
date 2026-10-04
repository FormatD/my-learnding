using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrivateLearningResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId",
                table: "Tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceRevisionId",
                table: "Tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "PrivateFile",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ResourceRevisionId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "PrivateFile");
        }
    }
}
