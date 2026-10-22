using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropMesScheduleResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_results",
                schema: "mes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_results",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule result aggregate id."),
                    affected_work_order_ids_json = table.Column<string>(type: "text", nullable: false, comment: "JSON affected work order id list produced by MES scheduler; producer is MES, consumers are MES/WMS/read APIs, compatibility is append-only fields."),
                    assignments_json = table.Column<string>(type: "text", nullable: false, comment: "JSON schedule assignments produced by MES scheduler; producer is MES, consumers are MES/WMS/read APIs, compatibility is append-only fields."),
                    schedule_version = table.Column<int>(type: "integer", nullable: false, comment: "Monotonic schedule version preserving current MES behavior."),
                    scheduled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time requested for the schedule run."),
                    trigger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Business trigger that caused the schedule run.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_results", x => x.id);
                },
                comment: "MES schedule result facts produced by the deterministic rule scheduler.");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_results_trigger_time",
                schema: "mes",
                table: "schedule_results",
                columns: new[] { "trigger", "scheduled_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_results_version",
                schema: "mes",
                table: "schedule_results",
                column: "schedule_version",
                unique: true);
        }
    }
}
