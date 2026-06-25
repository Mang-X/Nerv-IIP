using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.AppHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseIdempotencyKeyForProcessedIntegrationEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_processed_integration_events_consumer_event_id",
                schema: "apphub",
                table: "processed_integration_events");

            migrationBuilder.RenameColumn(
                name: "DedupeKey",
                schema: "apphub",
                table: "processed_integration_events",
                newName: "IdempotencyKey");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                schema: "apphub",
                table: "processed_integration_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "Source integration event identifier retained for traceability; idempotency uses IdempotencyKey.",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldComment: "Source integration event identifier unique within a consumer.");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                schema: "apphub",
                table: "processed_integration_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                comment: "Deterministic AppHub idempotency key unique within a consumer.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "AppHub dedupe key associated with the processed event.");

            migrationBuilder.Sql("""
                DELETE FROM apphub.processed_integration_events AS loser
                USING (
                    SELECT
                        "Id",
                        row_number() OVER (
                            PARTITION BY "ConsumerName", "IdempotencyKey"
                            ORDER BY "ProcessedAtUtc", "Id"
                        ) AS duplicate_rank
                    FROM apphub.processed_integration_events
                ) AS ranked
                WHERE loser."Id" = ranked."Id"
                  AND ranked.duplicate_rank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_idempotency_key",
                schema: "apphub",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_processed_integration_events_consumer_idempotency_key",
                schema: "apphub",
                table: "processed_integration_events");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                schema: "apphub",
                table: "processed_integration_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                comment: "AppHub dedupe key associated with the processed event.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "Deterministic AppHub idempotency key unique within a consumer.");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                schema: "apphub",
                table: "processed_integration_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "Source integration event identifier unique within a consumer.",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldComment: "Source integration event identifier retained for traceability; idempotency uses IdempotencyKey.");

            migrationBuilder.RenameColumn(
                name: "IdempotencyKey",
                schema: "apphub",
                table: "processed_integration_events",
                newName: "DedupeKey");

            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_event_id",
                schema: "apphub",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);
        }
    }
}
