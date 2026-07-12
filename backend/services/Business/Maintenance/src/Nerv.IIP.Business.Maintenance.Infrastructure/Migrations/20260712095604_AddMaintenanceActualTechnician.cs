using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceActualTechnician : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actual_technician_user_id",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Actual primary technician user reference recorded at completion and owned outside Maintenance.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actual_technician_user_id",
                schema: "maintenance",
                table: "maintenance_work_orders");
        }
    }
}
