using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryProductionReportAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "mes",
                table: "production_reports",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "manual",
                comment: "Report origin: manual operator entry or telemetry count automation.");

            migrationBuilder.CreateTable(
                name: "telemetry_production_report_candidates",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Telemetry production report candidate aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the candidate."),
                    source_idempotency_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "IndustrialTelemetry event idempotency key; unique candidate source boundary."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Device asset that produced the counter delta."),
                    tag_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Production-count telemetry tag key."),
                    reporting_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Configured telemetry report mode: posted or draft."),
                    good_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Positive good-quantity delta derived from the monotonic telemetry counter."),
                    bucket_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Inclusive UTC telemetry counter bucket start."),
                    bucket_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Exclusive UTC telemetry counter bucket end."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "MES-local mapped work center when available."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Current MES work order resolved for the counter delta when unambiguous."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Current MES operation task resolved for the counter delta when unambiguous."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Candidate status: draft or pending-confirmation."),
                    suspension_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Reason direct posting was suspended, such as active-alarm or no-current-work-order."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when MES received the telemetry count delta.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_production_report_candidates", x => x.id);
                },
                comment: "MES telemetry count deltas awaiting manual confirmation or retained as configured report drafts.");

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_report_candidates_scope_status_created",
                schema: "mes",
                table: "telemetry_production_report_candidates",
                columns: new[] { "organization_id", "environment_id", "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_telemetry_report_candidates_scope_source",
                schema: "mes",
                table: "telemetry_production_report_candidates",
                columns: new[] { "organization_id", "environment_id", "source_idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telemetry_production_report_candidates",
                schema: "mes");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "mes",
                table: "production_reports");
        }
    }
}
