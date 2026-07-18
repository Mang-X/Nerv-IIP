using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenConnectorTagBindingConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_connector_tag_bindings_activation_status",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                sql: "activation_status IN ('pending', 'active', 'error', 'disabled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_connector_tag_bindings_current_retirement",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                sql: "(is_current AND retired_at_utc IS NULL) OR (NOT is_current AND retired_at_utc IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_connector_tag_bindings_activation_status",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_connector_tag_bindings_current_retirement",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings");
        }
    }
}
