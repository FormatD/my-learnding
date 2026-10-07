using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LocalMappingResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelResultPayload",
                table: "MappingSuggestion",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MappingSuggestion_ModelResult",
                table: "MappingSuggestion",
                sql: "\"ModelResultPayload\" IS NULL OR (jsonb_typeof(\"ModelResultPayload\"::jsonb)='object' AND COALESCE((\"ModelResultPayload\"::jsonb->>'decision') IN ('Propose','NeedsReview'),false))");
            migrationBuilder.Sql("CREATE FUNCTION learning_mapping_model_result_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"ModelResultPayload\" IS DISTINCT FROM OLD.\"ModelResultPayload\" THEN RAISE EXCEPTION 'Mapping model results are immutable' USING ERRCODE='23514'; END IF; RETURN NEW; END $$; CREATE TRIGGER mapping_model_result_immutable BEFORE UPDATE ON \"MappingSuggestion\" FOR EACH ROW EXECUTE FUNCTION learning_mapping_model_result_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER mapping_model_result_immutable ON \"MappingSuggestion\"; DROP FUNCTION learning_mapping_model_result_immutable();");
            migrationBuilder.DropCheckConstraint(
                name: "CK_MappingSuggestion_ModelResult",
                table: "MappingSuggestion");

            migrationBuilder.DropColumn(
                name: "ModelResultPayload",
                table: "MappingSuggestion");
        }
    }
}
