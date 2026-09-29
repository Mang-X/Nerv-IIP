using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInterruptibleScheduleSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "segments_json",
                schema: "scheduling",
                table: "schedule_plan_assignments",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]",
                comment: "Actual production intervals for an interruptible operation; empty for continuous assignments.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "segments_json",
                schema: "scheduling",
                table: "schedule_plan_assignments");
        }
    }
}
