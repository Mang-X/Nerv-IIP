using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.AppHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenConnectorConnectionProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                schema: "apphub",
                table: "connector_collection_health",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Application-managed optimistic concurrency version for collection-health merges");

            migrationBuilder.AddColumn<long>(
                name: "ConnectionObservedAtUtcTicks",
                schema: "apphub",
                table: "connector_collection_health",
                type: "bigint",
                nullable: true,
                comment: "Exact .NET UTC ticks for monotonic ordering beyond PostgreSQL timestamp microsecond precision");

            migrationBuilder.Sql("""
                UPDATE apphub.connector_collection_health
                SET "ConnectionObservedAtUtcTicks" = 621355968000000000
                    + (EXTRACT(EPOCH FROM "ConnectionObservedAtUtc") * 10000000)::bigint
                WHERE "ConnectionObservedAtUtc" IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_connector_collection_health_connection_shape",
                schema: "apphub",
                table: "connector_collection_health",
                sql: "(\"ConnectionStatus\" IS NULL AND \"ConnectionObservedAtUtc\" IS NULL AND \"ConnectionObservedAtUtcTicks\" IS NULL AND \"ConnectedSinceUtc\" IS NULL AND \"DisconnectedSinceUtc\" IS NULL) OR (\"ConnectionStatus\" = 'unknown' AND \"ConnectionObservedAtUtc\" IS NOT NULL AND \"ConnectionObservedAtUtcTicks\" IS NOT NULL AND \"ConnectedSinceUtc\" IS NULL AND \"DisconnectedSinceUtc\" IS NULL) OR (\"ConnectionStatus\" = 'alive' AND \"ConnectionObservedAtUtc\" IS NOT NULL AND \"ConnectionObservedAtUtcTicks\" IS NOT NULL AND \"ConnectedSinceUtc\" IS NOT NULL AND \"DisconnectedSinceUtc\" IS NULL) OR (\"ConnectionStatus\" = 'lost' AND \"ConnectionObservedAtUtc\" IS NOT NULL AND \"ConnectionObservedAtUtcTicks\" IS NOT NULL AND \"ConnectedSinceUtc\" IS NULL AND \"DisconnectedSinceUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_connector_collection_health_connection_status",
                schema: "apphub",
                table: "connector_collection_health",
                sql: "\"ConnectionStatus\" IS NULL OR \"ConnectionStatus\" IN ('unknown', 'alive', 'lost')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_connector_collection_health_connection_shape",
                schema: "apphub",
                table: "connector_collection_health");

            migrationBuilder.DropCheckConstraint(
                name: "ck_connector_collection_health_connection_status",
                schema: "apphub",
                table: "connector_collection_health");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "apphub",
                table: "connector_collection_health");

            migrationBuilder.DropColumn(
                name: "ConnectionObservedAtUtcTicks",
                schema: "apphub",
                table: "connector_collection_health");
        }
    }
}
