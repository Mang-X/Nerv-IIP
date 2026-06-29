using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Notification.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryAttemptRetryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptNo",
                schema: "notification",
                table: "delivery_attempts",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "One-based delivery attempt number for this message and channel.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextRetryAtUtc",
                schema: "notification",
                table: "delivery_attempts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when a failed attempt becomes eligible for retry; null after success or dead letter.");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_Status_NextRetryAtUtc",
                schema: "notification",
                table: "delivery_attempts",
                columns: new[] { "Status", "NextRetryAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_delivery_attempts_Status_NextRetryAtUtc",
                schema: "notification",
                table: "delivery_attempts");

            migrationBuilder.DropColumn(
                name: "AttemptNo",
                schema: "notification",
                table: "delivery_attempts");

            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                schema: "notification",
                table: "delivery_attempts");
        }
    }
}
