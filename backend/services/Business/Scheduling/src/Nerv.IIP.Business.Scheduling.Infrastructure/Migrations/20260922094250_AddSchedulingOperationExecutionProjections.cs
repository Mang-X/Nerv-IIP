using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingOperationExecutionProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_execution_projections",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Projection row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    work_order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "MES work-order public id."),
                    operation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "MES operation-task public id."),
                    operation_sequence = table.Column<int>(type: "integer", nullable: true, comment: "Optional operation sequence supplied by MES lifecycle facts."),
                    work_center_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Optional work center supplied by MES execution facts."),
                    actual_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Earliest observed operation start timestamp in UTC."),
                    actual_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Latest accepted operation completion timestamp in UTC."),
                    is_paused = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the latest lifecycle fact leaves the operation paused."),
                    completed_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Net reported good quantity, including negative reversal deltas."),
                    is_downtime_blocked = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the latest operation-scoped downtime fact is active."),
                    is_quality_blocked = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the latest operation-scoped quality result blocks execution."),
                    lifecycle_occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Ordering watermark for lifecycle state facts in UTC."),
                    lifecycle_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Integration event id that supplied the current lifecycle state."),
                    downtime_occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Ordering watermark for downtime state facts in UTC."),
                    downtime_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Integration event id that supplied the current downtime state."),
                    quality_occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Ordering watermark for quality state facts in UTC."),
                    quality_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Integration event id that supplied the current quality state."),
                    latest_source_occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Latest accepted source-fact timestamp across all execution axes in UTC."),
                    latest_source_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Integration event id for the latest accepted source fact.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_execution_projections", x => x.id);
                },
                comment: "Latest MES and quality execution facts projected at operation-task grain.");

            migrationBuilder.CreateIndex(
                name: "IX_operation_execution_projections_organization_id_environment~",
                schema: "scheduling",
                table: "operation_execution_projections",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "operation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_execution_projections",
                schema: "scheduling");
        }
    }
}
