using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicInspectionQuantityContinuationInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "quantity_generation_anchor_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC triggering event time retained while bounded quantity-window continuation remains pending.");

            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Processed integration event identifier."),
                    consumer_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "BusinessQuality integration event consumer name."),
                    event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Globally unique source event id used with consumer_name as the minimum inbox key."),
                    event_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Integration event type."),
                    event_version = table.Column<int>(type: "integer", nullable: false, comment: "Integration event contract version."),
                    source_service = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that produced the integration event."),
                    idempotency_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Publisher business idempotency key retained for traceability."),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when BusinessQuality accepted the event into its transactional inbox.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_integration_events", x => x.id);
                },
                comment: "Integration events processed by BusinessQuality using the ADR 0011 event-id consumer inbox.");

            migrationBuilder.CreateIndex(
                name: "ix_periodic_inspection_runtime_status_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                columns: new[] { "status", "quantity_generation_anchor_at_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_periodic_inspection_runtime_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                sql: "quantity_generation_anchor_at_utc IS NULL OR (status = 'active' AND quantity_interval IS NOT NULL AND uom_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_quality_processed_integration_events_source_type_processed_at",
                schema: "quality",
                table: "processed_integration_events",
                columns: new[] { "source_service", "event_type", "processed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_quality_processed_integration_events_consumer_event_id",
                schema: "quality",
                table: "processed_integration_events",
                columns: new[] { "consumer_name", "event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "quality");

            migrationBuilder.DropIndex(
                name: "ix_periodic_inspection_runtime_status_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_periodic_inspection_runtime_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropColumn(
                name: "quantity_generation_anchor_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");
        }
    }
}
