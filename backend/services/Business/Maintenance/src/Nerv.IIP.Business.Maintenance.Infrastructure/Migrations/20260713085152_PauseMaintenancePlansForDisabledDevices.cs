using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PauseMaintenancePlansForDisabledDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "paused",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether preventive maintenance generation is paused for this plan.");

            migrationBuilder.CreateTable(
                name: "maintenance_device_states",
                schema: "maintenance",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary from the MasterData device event."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary from the MasterData device event."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MasterData device asset code referenced by Maintenance plans."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the latest accepted MasterData event marks the device disabled."),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time of the latest accepted MasterData device status change."),
                    source_event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Latest accepted MasterData integration event identifier for traceability.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_device_states", x => new { x.organization_id, x.environment_id, x.device_asset_id });
                },
                comment: "Latest MasterData device status projected for Maintenance scheduling gates.");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_device_states_organization_id_environment_id_di~",
                schema: "maintenance",
                table: "maintenance_device_states",
                columns: new[] { "organization_id", "environment_id", "disabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_device_states",
                schema: "maintenance");

            migrationBuilder.DropColumn(
                name: "paused",
                schema: "maintenance",
                table: "maintenance_plans");
        }
    }
}
