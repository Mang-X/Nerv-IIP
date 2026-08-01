using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistSchedulePlanBlockWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "block_windows_json",
                schema: "scheduling",
                table: "schedule_plans",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]",
                comment: "Immutable JSON snapshot of equipment unavailability (maintenance/downtime) windows that actually constrained this plan.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "block_windows_json",
                schema: "scheduling",
                table: "schedule_plans");
        }
    }
}
