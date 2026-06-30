using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenancePlanRuntimeWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "window_end_utc",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC end of the optional runtime availability maintenance window.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "window_start_utc",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC start of the optional runtime availability maintenance window.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "window_end_utc",
                schema: "maintenance",
                table: "maintenance_plans");

            migrationBuilder.DropColumn(
                name: "window_start_utc",
                schema: "maintenance",
                table: "maintenance_plans");
        }
    }
}
