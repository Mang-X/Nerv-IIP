using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceExpectedRestoreAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expected_restore_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Optional UTC expected asset restoration time; a prediction, not an actual restoration fact.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expected_restore_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders");
        }
    }
}
