using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResourceLinksAndModelVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResourceUrl",
                table: "Tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelVersion",
                table: "Generations",
                type: "text",
                nullable: false,
                defaultValue: "");
            migrationBuilder.Sql("UPDATE \"Generations\" SET \"ModelVersion\" = 'mastery/1' WHERE \"ModelVersion\" = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResourceUrl",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ModelVersion",
                table: "Generations");
        }
    }
}
