using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryProductionReportCandidateLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "mes",
                table: "telemetry_production_report_candidates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Candidate lifecycle status: draft, pending-confirmation, confirmed, or dismissed; used as an optimistic concurrency predicate.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Candidate status: draft or pending-confirmation.");

            migrationBuilder.AddColumn<string>(
                name: "production_report_id",
                schema: "mes",
                table: "telemetry_production_report_candidates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Production report aggregate id created by confirmation.");

            migrationBuilder.AddColumn<string>(
                name: "resolution_reason",
                schema: "mes",
                table: "telemetry_production_report_candidates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Operator supplied dismissal reason when dismissed.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at_utc",
                schema: "mes",
                table: "telemetry_production_report_candidates",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the candidate reached a terminal state.");

            migrationBuilder.AddColumn<string>(
                name: "resolved_by",
                schema: "mes",
                table: "telemetry_production_report_candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Authenticated actor that confirmed or dismissed the candidate.");

            migrationBuilder.CreateTable(
                name: "telemetry_production_report_candidate_transitions",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Transition id."),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning telemetry candidate id."),
                    from_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Status before the transition."),
                    to_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Status after the transition."),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Authenticated transition actor."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional human disposition reason."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC transition time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_production_report_candidate_transitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_telemetry_production_report_candidate_transitions_telemetry~",
                        column: x => x.candidate_id,
                        principalSchema: "mes",
                        principalTable: "telemetry_production_report_candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Immutable audit history for telemetry report candidate lifecycle transitions.");

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_candidate_transitions_candidate_time",
                schema: "mes",
                table: "telemetry_production_report_candidate_transitions",
                columns: new[] { "candidate_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telemetry_production_report_candidate_transitions",
                schema: "mes");

            migrationBuilder.DropColumn(
                name: "production_report_id",
                schema: "mes",
                table: "telemetry_production_report_candidates");

            migrationBuilder.DropColumn(
                name: "resolution_reason",
                schema: "mes",
                table: "telemetry_production_report_candidates");

            migrationBuilder.DropColumn(
                name: "resolved_at_utc",
                schema: "mes",
                table: "telemetry_production_report_candidates");

            migrationBuilder.DropColumn(
                name: "resolved_by",
                schema: "mes",
                table: "telemetry_production_report_candidates");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "mes",
                table: "telemetry_production_report_candidates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Candidate status: draft or pending-confirmation.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Candidate lifecycle status: draft, pending-confirmation, confirmed, or dismissed; used as an optimistic concurrency predicate.");
        }
    }
}
