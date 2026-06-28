using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingInputChangeInvalidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_event_dead_letters",
                schema: "scheduling",
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

            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Processed integration event identifier."),
                    ConsumerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "BusinessScheduling integration event consumer name."),
                    EventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Source integration event identifier retained for traceability; idempotency uses IdempotencyKey."),
                    EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Integration event type."),
                    EventVersion = table.Column<int>(type: "integer", nullable: false, comment: "Integration event contract version."),
                    SourceService = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that produced the integration event."),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Deterministic BusinessScheduling idempotency key unique within a consumer."),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when BusinessScheduling processed the event.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_integration_events", x => x.Id);
                },
                comment: "Integration events already processed by BusinessScheduling for idempotent consumption.");

            migrationBuilder.CreateTable(
                name: "schedule_plan_invalidations",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule plan invalidation row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    plan_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Generated schedule plan invalidated by the upstream event."),
                    source_event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Source integration event identifier."),
                    source_event_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Source integration event type."),
                    source_service = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that produced the source event."),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Scheduling invalidation reason code."),
                    affected_resource_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true, comment: "Affected resource or device asset id when the event targets equipment."),
                    affected_work_order_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true, comment: "Affected work order id when the event targets a work order."),
                    affected_operation_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true, comment: "Affected operation id when available."),
                    affected_sku_code = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true, comment: "Affected SKU code when the event changes material readiness."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the source event occurred."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when BusinessScheduling recorded the invalidation.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_plan_invalidations", x => x.id);
                },
                comment: "Event-driven Scheduling plan invalidation projection for APS replan decisions.");

            migrationBuilder.CreateIndex(
                name: "IX_integration_event_dead_letters_consumer_name_event_id",
                schema: "scheduling",
                table: "integration_event_dead_letters",
                columns: new[] { "consumer_name", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_event_dead_letters_consumer_name_status_dead_le~",
                schema: "scheduling",
                table: "integration_event_dead_letters",
                columns: new[] { "consumer_name", "status", "dead_lettered_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_processed_integration_events_source_type_processed_at",
                schema: "scheduling",
                table: "processed_integration_events",
                columns: new[] { "SourceService", "EventType", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_idempotency_key",
                schema: "scheduling",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedule_plan_invalidations_plan_recorded_at",
                schema: "scheduling",
                table: "schedule_plan_invalidations",
                columns: new[] { "organization_id", "environment_id", "plan_id", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_schedule_plan_invalidations_source_event",
                schema: "scheduling",
                table: "schedule_plan_invalidations",
                columns: new[] { "organization_id", "environment_id", "plan_id", "source_event_type", "source_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_event_dead_letters",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "schedule_plan_invalidations",
                schema: "scheduling");
        }
    }
}
