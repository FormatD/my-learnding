using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LegacyModelVersionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Generations\" SET \"ModelVersion\" = CASE WHEN \"RuleVersion\" = 'evidence/1' THEN 'mastery/1' ELSE 'unknown-legacy' END WHERE \"ModelVersion\" = ''");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
