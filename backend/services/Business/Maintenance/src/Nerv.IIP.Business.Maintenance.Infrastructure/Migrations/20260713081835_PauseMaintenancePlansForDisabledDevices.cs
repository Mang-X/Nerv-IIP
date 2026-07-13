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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paused",
                schema: "maintenance",
                table: "maintenance_plans");
        }
    }
}
