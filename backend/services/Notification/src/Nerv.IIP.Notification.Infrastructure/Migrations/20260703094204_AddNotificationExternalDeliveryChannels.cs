using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Notification.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationExternalDeliveryChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                schema: "notification",
                table: "delivery_attempts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "Optional external provider message identifier returned after successful delivery.");

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                schema: "notification",
                table: "delivery_attempts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Delivery provider implementation name used for the attempt.");

            migrationBuilder.AddColumn<string>(
                name: "RecipientAddress",
                schema: "notification",
                table: "delivery_attempts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                comment: "Provider-specific recipient address or account id used for external delivery.");

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Notification preference identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Environment identifier."),
                    RecipientRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Notification recipient reference."),
                    NotificationType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Notification type, usually the source event type or wildcard '*'."),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Delivery channel controlled by the preference."),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this channel is enabled for the notification type; critical severity can force delivery."),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the preference was created."),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the preference was last updated."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => x.Id);
                },
                comment: "User-level notification type and channel preferences.");

            migrationBuilder.CreateTable(
                name: "notification_recipient_channel_bindings",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Recipient channel binding identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Environment identifier."),
                    RecipientRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Notification recipient reference, for example an IAM user ref."),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "External delivery channel name."),
                    RecipientAddress = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Provider-specific user id, email address or webhook URL; provider secrets are not stored here."),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this recipient binding is active."),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the binding was created."),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the binding was last updated."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_recipient_channel_bindings", x => x.Id);
                },
                comment: "Recipient to external delivery channel account bindings owned by Notification.");

            migrationBuilder.CreateTable(
                name: "notification_subscriptions",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Notification subscription identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Environment identifier."),
                    RecipientRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Notification recipient reference."),
                    NotificationType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Notification type, usually the source event type or wildcard '*'."),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Delivery channel included by the subscription."),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this subscription is active."),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the subscription was created."),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the subscription was last updated."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_subscriptions", x => x.Id);
                },
                comment: "Recipient notification type subscriptions for external delivery channels.");

            migrationBuilder.CreateIndex(
                name: "IX_notification_preferences_OrganizationId_EnvironmentId_Notif~",
                schema: "notification",
                table: "notification_preferences",
                columns: new[] { "OrganizationId", "EnvironmentId", "NotificationType", "Channel", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_preferences_OrganizationId_EnvironmentId_Recip~",
                schema: "notification",
                table: "notification_preferences",
                columns: new[] { "OrganizationId", "EnvironmentId", "RecipientRef", "NotificationType", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_recipient_channel_bindings_OrganizationId_Env~1",
                schema: "notification",
                table: "notification_recipient_channel_bindings",
                columns: new[] { "OrganizationId", "EnvironmentId", "RecipientRef", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_recipient_channel_bindings_OrganizationId_Envi~",
                schema: "notification",
                table: "notification_recipient_channel_bindings",
                columns: new[] { "OrganizationId", "EnvironmentId", "Channel", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_OrganizationId_EnvironmentId_Not~",
                schema: "notification",
                table: "notification_subscriptions",
                columns: new[] { "OrganizationId", "EnvironmentId", "NotificationType", "Channel", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_OrganizationId_EnvironmentId_Rec~",
                schema: "notification",
                table: "notification_subscriptions",
                columns: new[] { "OrganizationId", "EnvironmentId", "RecipientRef", "NotificationType", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "notification_recipient_channel_bindings",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "notification_subscriptions",
                schema: "notification");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                schema: "notification",
                table: "delivery_attempts");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                schema: "notification",
                table: "delivery_attempts");

            migrationBuilder.DropColumn(
                name: "RecipientAddress",
                schema: "notification",
                table: "delivery_attempts");
        }
    }
}
