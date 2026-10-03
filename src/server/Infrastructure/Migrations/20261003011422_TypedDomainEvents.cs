using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Learning.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TypedDomainEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DomainEventId",
                table: "Outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DomainEventId",
                table: "ConsumerReceipt",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DomainEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateType = table.Column<string>(type: "text", nullable: false),
                    EventSequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    PayloadVersion = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchTarget = table.Column<string>(type: "text", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainEvent", x => x.Id);
                    table.UniqueConstraint("AK_DomainEvent_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.CheckConstraint("CK_DomainEvent_Envelope", "\"EventSequence\">0 AND \"PayloadVersion\"=1 AND \"AggregateId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"AggregateType\"<>'' AND \"EventType\" IN ('AttemptSubmitted','GradingConfirmed','AssessmentApplied','CorrectionConfirmed','TaskTransitioned','ProgressChanged','ContentReleasePublished') AND jsonb_typeof(\"Payload\"::jsonb)='object' AND length(\"PayloadHash\")=64 AND (\"DispatchTarget\" IS NULL OR \"DispatchTarget\"='assessment/1')");
                    table.ForeignKey(
                        name: "FK_DomainEvent_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DomainEvent_Students_FamilyId_StudentId",
                        columns: x => new { x.FamilyId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_DomainEventId",
                table: "Outbox",
                column: "DomainEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_FamilyId_DomainEventId",
                table: "Outbox",
                columns: new[] { "FamilyId", "DomainEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerReceipt_FamilyId_DomainEventId",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "DomainEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_DomainEvent_EventSequence",
                table: "DomainEvent",
                column: "EventSequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DomainEvent_FamilyId",
                table: "DomainEvent",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_DomainEvent_FamilyId_StudentId_EventSequence",
                table: "DomainEvent",
                columns: new[] { "FamilyId", "StudentId", "EventSequence" });

            migrationBuilder.AddForeignKey(
                name: "FK_ConsumerReceipt_DomainEvent_FamilyId_DomainEventId",
                table: "ConsumerReceipt",
                columns: new[] { "FamilyId", "DomainEventId" },
                principalTable: "DomainEvent",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Outbox_DomainEvent_FamilyId_DomainEventId",
                table: "Outbox",
                columns: new[] { "FamilyId", "DomainEventId" },
                principalTable: "DomainEvent",
                principalColumns: new[] { "FamilyId", "Id" },
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.Sql("CREATE FUNCTION learning_domain_event_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Domain event records are immutable' USING ERRCODE='23514'; END $$; CREATE TRIGGER domain_event_immutable BEFORE UPDATE ON \"DomainEvent\" FOR EACH ROW EXECUTE FUNCTION learning_domain_event_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConsumerReceipt_DomainEvent_FamilyId_DomainEventId",
                table: "ConsumerReceipt");

            migrationBuilder.DropForeignKey(
                name: "FK_Outbox_DomainEvent_FamilyId_DomainEventId",
                table: "Outbox");

            migrationBuilder.DropTable(
                name: "DomainEvent");
            migrationBuilder.Sql("DROP FUNCTION learning_domain_event_immutable();");

            migrationBuilder.DropIndex(
                name: "IX_Outbox_DomainEventId",
                table: "Outbox");

            migrationBuilder.DropIndex(
                name: "IX_Outbox_FamilyId_DomainEventId",
                table: "Outbox");

            migrationBuilder.DropIndex(
                name: "IX_ConsumerReceipt_FamilyId_DomainEventId",
                table: "ConsumerReceipt");

            migrationBuilder.DropColumn(
                name: "DomainEventId",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "DomainEventId",
                table: "ConsumerReceipt");
        }
    }
}
