using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceRuntimeWindowQueryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_maintenance_plans_organization_id_environment_id_device_ass~",
                schema: "maintenance",
                table: "maintenance_plans",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "window_start_utc", "window_end_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_maintenance_plans_organization_id_environment_id_device_ass~",
                schema: "maintenance",
                table: "maintenance_plans");
        }
    }
}
