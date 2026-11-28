using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryConnectorConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telemetry_connector_configs",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    connector_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    protocol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    poll_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    mappings_json = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_connector_configs", x => new { x.organization_id, x.environment_id, x.connector_id });
                },
                comment: "Desired telemetry collector configuration.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telemetry_connector_configs",
                schema: "industrial_telemetry");
        }
    }
}
