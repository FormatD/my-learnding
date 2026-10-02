using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GoalLegacyDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Tasks" SET "GoalSnapshots"='[]' WHERE "GoalSnapshots"='';
                UPDATE "Goals" SET "Subject"='Unspecified' WHERE "Subject"='';
                UPDATE "Goals" SET "GoalType"='Activity' WHERE "GoalType"='';
                UPDATE "Goals" SET "Period"='Daily' WHERE "Period"='';
                UPDATE "Goals" SET "ScheduleRule"='[1,2,3,4,5,6,0]' WHERE "ScheduleRule"='';
                UPDATE "Goals" SET "TargetValue"=1 WHERE "TargetValue"=0;
                UPDATE "Goals" SET "Priority"=3 WHERE "Priority"=0;
                UPDATE "Goals" SET "Version"=1 WHERE "Version"=0;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "GoalSnapshots",
                table: "Tasks",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "Goals",
                type: "bigint",
                nullable: false,
                defaultValue: 1L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "TargetValue",
                table: "Goals",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                table: "Goals",
                type: "text",
                nullable: false,
                defaultValue: "Unspecified",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ScheduleRule",
                table: "Goals",
                type: "text",
                nullable: false,
                defaultValue: "[1,2,3,4,5,6,0]",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "Goals",
                type: "integer",
                nullable: false,
                defaultValue: 3,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Period",
                table: "Goals",
                type: "text",
                nullable: false,
                defaultValue: "Daily",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "GoalType",
                table: "Goals",
                type: "text",
                nullable: false,
                defaultValue: "Activity",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "GoalSnapshots",
                table: "Tasks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "[]");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "Goals",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 1L);

            migrationBuilder.AlterColumn<int>(
                name: "TargetValue",
                table: "Goals",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                table: "Goals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "Unspecified");

            migrationBuilder.AlterColumn<string>(
                name: "ScheduleRule",
                table: "Goals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "[1,2,3,4,5,6,0]");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "Goals",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 3);

            migrationBuilder.AlterColumn<string>(
                name: "Period",
                table: "Goals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "Daily");

            migrationBuilder.AlterColumn<string>(
                name: "GoalType",
                table: "Goals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "Activity");
        }
    }
}
