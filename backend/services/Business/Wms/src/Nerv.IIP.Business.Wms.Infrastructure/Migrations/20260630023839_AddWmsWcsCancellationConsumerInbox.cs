using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsWcsCancellationConsumerInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "wms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Processed integration event identifier."),
                    ConsumerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "BusinessWMS integration event consumer name."),
                    EventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Source integration event identifier retained for traceability; idempotency uses IdempotencyKey."),
                    EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Integration event type."),
                    EventVersion = table.Column<int>(type: "integer", nullable: false, comment: "Integration event contract version."),
                    SourceService = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that produced the integration event."),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Deterministic BusinessWMS idempotency key unique within a consumer."),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when BusinessWMS processed the event.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_integration_events", x => x.Id);
                },
                comment: "Integration events already processed by BusinessWMS for idempotent consumption.");

            migrationBuilder.CreateIndex(
                name: "ix_processed_integration_events_source_type_processed_at",
                schema: "wms",
                table: "processed_integration_events",
                columns: new[] { "SourceService", "EventType", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_idempotency_key",
                schema: "wms",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "wms");
        }
    }
}
