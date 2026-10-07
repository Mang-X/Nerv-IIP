using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleInsertionPreviewJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_insertion_preview_jobs",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Insertion-preview job id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    input_json = table.Column<string>(type: "jsonb", nullable: false, comment: "SchedulingInsertionPreviewInputContract v1; Scheduling owns and reads the accepted horizon and order selections."),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Created, Running, Completed or Failed execution fact."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC acceptance timestamp."),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC calculation start timestamp."),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC terminal timestamp."),
                    preview_json = table.Column<string>(type: "jsonb", nullable: true, comment: "SchedulePlanContract v1 Preview result; Scheduling writes and reads it without persisting a plan."),
                    failure_reason = table.Column<string>(type: "text", nullable: true, comment: "Displayable calculation failure reason.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_insertion_preview_jobs", x => x.id);
                },
                comment: "Scheduling-owned asynchronous insertion preview jobs.");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_insertion_preview_jobs_status_created_at_utc",
                schema: "scheduling",
                table: "schedule_insertion_preview_jobs",
                columns: new[] { "status", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_insertion_preview_jobs",
                schema: "scheduling");
        }
    }
}
