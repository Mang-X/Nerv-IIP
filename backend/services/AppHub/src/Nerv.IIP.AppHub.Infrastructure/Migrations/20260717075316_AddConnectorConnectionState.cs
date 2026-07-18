using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.AppHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorConnectionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConnectedSinceUtc",
                schema: "apphub",
                table: "connector_collection_health",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC start of the current alive connection interval; present only while connection state is alive");

            migrationBuilder.AddColumn<string>(
                name: "ConnectionDiagnosticCode",
                schema: "apphub",
                table: "connector_collection_health",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Sanitized diagnostic code for the latest field connection transition; null when not reported");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConnectionObservedAtUtc",
                schema: "apphub",
                table: "connector_collection_health",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time of the latest authoritative field connection observation; null means no connection fact");

            migrationBuilder.AddColumn<string>(
                name: "ConnectionReasonCategory",
                schema: "apphub",
                table: "connector_collection_health",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Bounded category for the latest field connection transition reason; null when not reported");

            migrationBuilder.AddColumn<string>(
                name: "ConnectionStatus",
                schema: "apphub",
                table: "connector_collection_health",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "Authoritative field connection state: unknown, alive, or lost; null means a legacy report supplied no connection fact");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DisconnectedSinceUtc",
                schema: "apphub",
                table: "connector_collection_health",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC start of the current lost connection interval; present only while connection state is lost");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectedSinceUtc",
                schema: "apphub",
                table: "connector_collection_health");

            migrationBuilder.DropColumn(
                name: "ConnectionDiagnosticCode",
                schema: "apphub",
                table: "connector_collection_health");

            migrationBuilder.DropColumn(
                name: "ConnectionObservedAtUtc",
                schema: "apphub",
                table: "connector_collection_health");

            migrationBuilder.DropColumn(
                name: "ConnectionReasonCategory",
                schema: "apphub",
                table: "connector_collection_health");

            migrationBuilder.DropColumn(
                name: "ConnectionStatus",
                schema: "apphub",
                table: "connector_collection_health");

            migrationBuilder.DropColumn(
                name: "DisconnectedSinceUtc",
                schema: "apphub",
                table: "connector_collection_health");
        }
    }
}
