using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Notification.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotificationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notification");

            migrationBuilder.CreateTable(
                name: "notification_intents",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Notification intent identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Environment identifier."),
                    SourceService = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that produced the notification intent."),
                    SourceEventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Source event type that produced the intent."),
                    SourceEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Source event identifier used for traceability."),
                    IntentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Notification intent kind such as message or task."),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Notification severity."),
                    DedupeKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Organization and environment scoped dedupe key."),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Optional weak resource reference type."),
                    ResourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Optional weak resource reference identifier."),
                    FileId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Optional FileStorage file identifier reference."),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "User-visible notification title."),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, comment: "User-visible notification summary."),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the intent was created."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_intents", x => x.Id);
                },
                comment: "Notification intent aggregate roots submitted by platform services for in-app messages and tasks.");

            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Processed integration event identifier."),
                    ConsumerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Notification integration event consumer name."),
                    EventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Source integration event identifier unique within a consumer."),
                    EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Integration event type."),
                    EventVersion = table.Column<int>(type: "integer", nullable: false, comment: "Integration event contract version."),
                    SourceService = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that produced the integration event."),
                    DedupeKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Notification dedupe key associated with the processed event."),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when Notification processed the event.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_integration_events", x => x.Id);
                },
                comment: "Integration events already processed by Notification for idempotent consumption.");

            migrationBuilder.CreateTable(
                name: "notification_messages",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Notification message identifier."),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning notification intent identifier."),
                    RecipientRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Recipient reference such as user or role."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Message read status."),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Notification severity copied from the intent."),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "User-visible notification title."),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, comment: "User-visible notification summary."),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Optional weak resource reference type."),
                    ResourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Optional weak resource reference identifier."),
                    FileId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Optional FileStorage file identifier reference."),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the message was created."),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the recipient first marked the message read.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_messages_notification_intents_NotificationInte~",
                        column: x => x.NotificationIntentId,
                        principalSchema: "notification",
                        principalTable: "notification_intents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Recipient-specific in-app notification messages owned by notification intents.");

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Delivery attempt identifier."),
                    NotificationMessageId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Notification message identifier targeted by the attempt."),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Delivery channel name."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Delivery attempt status."),
                    AttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when delivery was attempted."),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Optional provider failure reason.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_notification_messages_NotificationMessage~",
                        column: x => x.NotificationMessageId,
                        principalSchema: "notification",
                        principalTable: "notification_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Notification delivery attempt records for future provider integrations.");

            migrationBuilder.CreateTable(
                name: "notification_tasks",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Notification task identifier."),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning notification intent identifier."),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Notification message that owns the task surface."),
                    RecipientRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Recipient reference such as user or role."),
                    TaskType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Actionable notification task type."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Notification task status."),
                    ActionRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Optional action reference for task handling."),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the task was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_tasks_notification_intents_NotificationIntentId",
                        column: x => x.NotificationIntentId,
                        principalSchema: "notification",
                        principalTable: "notification_intents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notification_tasks_notification_messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "notification",
                        principalTable: "notification_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Actionable notification tasks owned by task notification intents.");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_Channel_Status_AttemptedAtUtc",
                schema: "notification",
                table: "delivery_attempts",
                columns: new[] { "Channel", "Status", "AttemptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_NotificationMessageId",
                schema: "notification",
                table: "delivery_attempts",
                column: "NotificationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_intents_OrganizationId_EnvironmentId_SourceEve~",
                schema: "notification",
                table: "notification_intents",
                columns: new[] { "OrganizationId", "EnvironmentId", "SourceEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_intents_OrganizationId_EnvironmentId_SourceSer~",
                schema: "notification",
                table: "notification_intents",
                columns: new[] { "OrganizationId", "EnvironmentId", "SourceService", "SourceEventType", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_messages_NotificationIntentId",
                schema: "notification",
                table: "notification_messages",
                column: "NotificationIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_messages_RecipientRef_Status_CreatedAtUtc",
                schema: "notification",
                table: "notification_messages",
                columns: new[] { "RecipientRef", "Status", "CreatedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_notification_tasks_MessageId",
                schema: "notification",
                table: "notification_tasks",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_tasks_NotificationIntentId",
                schema: "notification",
                table: "notification_tasks",
                column: "NotificationIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_tasks_RecipientRef_Status_CreatedAtUtc",
                schema: "notification",
                table: "notification_tasks",
                columns: new[] { "RecipientRef", "Status", "CreatedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_processed_integration_events_ConsumerName_EventId",
                schema: "notification",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processed_integration_events_SourceService_EventType_Proces~",
                schema: "notification",
                table: "processed_integration_events",
                columns: new[] { "SourceService", "EventType", "ProcessedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "notification_tasks",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "notification_messages",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "notification_intents",
                schema: "notification");
        }
    }
}
