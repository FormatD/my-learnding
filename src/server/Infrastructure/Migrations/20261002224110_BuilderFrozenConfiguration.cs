using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BuilderFrozenConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelConfigHash",
                table: "BuilderRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelConfigPayload",
                table: "BuilderRuns",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelConfigHash",
                table: "BuilderRuns");

            migrationBuilder.DropColumn(
                name: "ModelConfigPayload",
                table: "BuilderRuns");
        }
    }
}
