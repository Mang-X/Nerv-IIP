using System;
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
            migrationBuilder.CreateTable(
                name: "schedule_plan_assignment_segments",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule assignment segment row id."),
                    schedule_plan_assignment_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning schedule assignment row id."),
                    segment_index = table.Column<int>(type: "integer", nullable: false, comment: "Zero-based order of the production interval within its assignment."),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Actual segment start timestamp in UTC."),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Actual segment end timestamp in UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_plan_assignment_segments", x => x.id);
                    table.ForeignKey(
                        name: "FK_schedule_plan_assignment_segments_schedule_plan_assignments~",
                        column: x => x.schedule_plan_assignment_id,
                        principalSchema: "scheduling",
                        principalTable: "schedule_plan_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Actual production intervals within an interruptible schedule assignment.");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_plan_assignment_segments_schedule_plan_assignment_~",
                schema: "scheduling",
                table: "schedule_plan_assignment_segments",
                columns: new[] { "schedule_plan_assignment_id", "segment_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_plan_assignment_segments",
                schema: "scheduling");
        }
    }
}
