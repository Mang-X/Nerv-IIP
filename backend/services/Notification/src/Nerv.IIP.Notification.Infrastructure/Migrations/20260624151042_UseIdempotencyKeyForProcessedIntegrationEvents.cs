using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Notification.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseIdempotencyKeyForProcessedIntegrationEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_processed_integration_events_ConsumerName_EventId",
                schema: "notification",
                table: "processed_integration_events");

            migrationBuilder.RenameColumn(
                name: "DedupeKey",
                schema: "notification",
                table: "processed_integration_events",
                newName: "IdempotencyKey");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                schema: "notification",
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
                schema: "notification",
                table: "processed_integration_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                comment: "Deterministic Notification idempotency key unique within a consumer.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "Notification dedupe key associated with the processed event.");

            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_idempotency_key",
                schema: "notification",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_processed_integration_events_consumer_idempotency_key",
                schema: "notification",
                table: "processed_integration_events");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                schema: "notification",
                table: "processed_integration_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                comment: "Notification dedupe key associated with the processed event.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "Deterministic Notification idempotency key unique within a consumer.");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                schema: "notification",
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
                schema: "notification",
                table: "processed_integration_events",
                newName: "DedupeKey");

            migrationBuilder.CreateIndex(
                name: "IX_processed_integration_events_ConsumerName_EventId",
                schema: "notification",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);
        }
    }
}
