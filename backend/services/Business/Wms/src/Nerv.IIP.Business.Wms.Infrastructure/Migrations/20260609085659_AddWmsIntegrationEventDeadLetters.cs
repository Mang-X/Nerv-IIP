using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsIntegrationEventDeadLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_event_dead_letters",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Dead-letter message id."),
                    consumer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Integration event consumer name that rejected the message."),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Rejected integration event id when present."),
                    event_type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "Rejected integration event type when present."),
                    event_version = table.Column<int>(type: "integer", nullable: true, comment: "Rejected integration event envelope version when present."),
                    source_service = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Source service from the rejected event envelope when present."),
                    idempotency_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Rejected integration event idempotency key when present."),
                    event_clr_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "CLR contract type captured for replay diagnostics."),
                    event_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Serialized rejected integration event envelope and payload."),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Machine-readable reason the consumer rejected the message."),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Operator-readable rejection detail."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Dead-letter status: Pending or Replayed."),
                    dead_lettered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the service stored the dead-letter message."),
                    replayed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the dead-letter message was marked replayed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_event_dead_letters", x => x.id);
                },
                comment: "Integration events rejected before business handling and retained for replay triage.");

            migrationBuilder.CreateIndex(
                name: "IX_integration_event_dead_letters_consumer_name_event_id",
                schema: "wms",
                table: "integration_event_dead_letters",
                columns: new[] { "consumer_name", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_event_dead_letters_consumer_name_status_dead_le~",
                schema: "wms",
                table: "integration_event_dead_letters",
                columns: new[] { "consumer_name", "status", "dead_lettered_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_event_dead_letters",
                schema: "wms");
        }
    }
}
