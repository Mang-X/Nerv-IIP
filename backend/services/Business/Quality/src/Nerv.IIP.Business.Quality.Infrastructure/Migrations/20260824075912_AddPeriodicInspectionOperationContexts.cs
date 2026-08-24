using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicInspectionOperationContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "periodic_inspection_operations",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Periodic inspection operation aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the operation facts."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the operation facts apply."),
                    work_order_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MES work order public id."),
                    operation_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MES operation task public id."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "SKU snapshot from the work-order release event; null until release arrives."),
                    operation_sequence = table.Column<int>(type: "integer", nullable: true, comment: "Positive operation sequence from the work-order release event; null until release arrives."),
                    work_center_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Work center snapshot from the work-order release event; null until release arrives."),
                    released_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when MES released the work order; null while source facts are staged out of order."),
                    completion_sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "SKU snapshot staged from an operation completion event."),
                    completion_operation_sequence = table.Column<int>(type: "integer", nullable: true, comment: "Positive operation sequence staged from an operation completion event."),
                    completion_work_center_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Work center snapshot staged from an operation completion event."),
                    completion_uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Quantity UOM snapshot staged from an operation completion event."),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when MES completed the operation; may precede the release event in delivery order.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_periodic_inspection_operations", x => x.id);
                    table.CheckConstraint("ck_periodic_inspection_operations_completion_snapshot", "(completion_sku_code IS NULL AND completion_operation_sequence IS NULL AND completion_work_center_id IS NULL AND completion_uom_code IS NULL AND completed_at_utc IS NULL) OR (completion_sku_code IS NOT NULL AND completion_operation_sequence > 0 AND completion_work_center_id IS NOT NULL AND completion_uom_code IS NOT NULL AND completed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_periodic_inspection_operations_completion_time", "completed_at_utc IS NULL OR released_at_utc IS NULL OR completed_at_utc >= released_at_utc");
                    table.CheckConstraint("ck_periodic_inspection_operations_release_snapshot", "(sku_code IS NULL AND operation_sequence IS NULL AND work_center_id IS NULL AND released_at_utc IS NULL) OR (sku_code IS NOT NULL AND operation_sequence > 0 AND work_center_id IS NOT NULL AND released_at_utc IS NOT NULL)");
                },
                comment: "Quality-owned MES operation source facts staged for periodic inspection reconciliation.");

            migrationBuilder.CreateTable(
                name: "periodic_inspection_production_reports",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Production-report fact id."),
                    operation_context_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning periodic inspection operation aggregate id."),
                    report_no = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MES production report business identity."),
                    work_center_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MES work center snapshot carried by the production report."),
                    good_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Signed good quantity from MES; reversals carry a non-positive value in the reported UOM."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MES-reported quantity unit of measure; all reports for one operation must match."),
                    reported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC business time recorded by MES."),
                    is_reversal = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this fact reverses an earlier report."),
                    reversed_report_no = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Original MES report number referenced by a reversal.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_periodic_inspection_production_reports", x => x.id);
                    table.CheckConstraint("ck_periodic_inspection_reports_reversal", "(is_reversal AND good_quantity <= 0 AND reversed_report_no IS NOT NULL) OR (NOT is_reversal AND good_quantity >= 0 AND reversed_report_no IS NULL)");
                    table.ForeignKey(
                        name: "FK_periodic_inspection_production_reports_periodic_inspection_~",
                        column: x => x.operation_context_id,
                        principalSchema: "quality",
                        principalTable: "periodic_inspection_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Immutable MES production-report facts used to reconcile periodic inspection watermarks.");

            migrationBuilder.CreateTable(
                name: "periodic_inspection_runtime_contexts",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Periodic inspection runtime context id."),
                    operation_context_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning periodic inspection operation aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id frozen at context creation."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id frozen at context creation."),
                    work_order_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MES work order public id frozen at context creation."),
                    operation_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MES operation task public id frozen at context creation."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "SKU snapshot from the release event."),
                    operation_sequence = table.Column<int>(type: "integer", nullable: false, comment: "Positive MES operation sequence snapshot."),
                    work_center_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Work center snapshot from the release event."),
                    released_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC work-order release time."),
                    inspection_plan_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Immutable matched inspection plan id."),
                    inspection_plan_version = table.Column<int>(type: "integer", nullable: false, comment: "Immutable matched inspection plan version."),
                    time_interval_hours = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Frozen periodic time interval in hours."),
                    quantity_interval = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Frozen periodic quantity interval in the report UOM."),
                    assigned_inspector_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Frozen optional inspector assignment target."),
                    assigned_team_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Frozen optional team assignment target."),
                    first_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "Earliest UTC MES production activity time observed for the operation."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Authoritative MES production report UOM; null before the first report."),
                    cumulative_good_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Current signed net good quantity including reversal effects."),
                    quantity_high_water = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Monotonic accepted good-quantity high water; reversal facts neither advance nor roll it back."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Runtime context status: active or closed."),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC MES operation completion time when status is closed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_periodic_inspection_runtime_contexts", x => x.id);
                    table.CheckConstraint("ck_periodic_inspection_runtime_assignment", "assigned_inspector_user_id IS NULL OR assigned_team_id IS NULL");
                    table.CheckConstraint("ck_periodic_inspection_runtime_high_water", "quantity_high_water >= 0");
                    table.CheckConstraint("ck_periodic_inspection_runtime_interval", "(time_interval_hours IS NOT NULL AND time_interval_hours > 0) OR (quantity_interval IS NOT NULL AND quantity_interval > 0)");
                    table.CheckConstraint("ck_periodic_inspection_runtime_status", "(status = 'active' AND completed_at_utc IS NULL) OR (status = 'closed' AND completed_at_utc IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_periodic_inspection_runtime_contexts_periodic_inspection_op~",
                        column: x => x.operation_context_id,
                        principalSchema: "quality",
                        principalTable: "periodic_inspection_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Frozen per-plan periodic inspection runtime contexts and quantity/time watermarks; task generation is owned by a later stage.");

            migrationBuilder.CreateIndex(
                name: "ux_periodic_inspection_operations_scope_operation",
                schema: "quality",
                table: "periodic_inspection_operations",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_periodic_inspection_reports_operation_report",
                schema: "quality",
                table: "periodic_inspection_production_reports",
                columns: new[] { "operation_context_id", "report_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_periodic_inspection_runtime_contexts_operation_context_id",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                column: "operation_context_id");

            migrationBuilder.CreateIndex(
                name: "ix_periodic_inspection_runtime_scope_status_activity",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                columns: new[] { "organization_id", "environment_id", "status", "first_activity_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_periodic_inspection_runtime_scope_plan_operation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                columns: new[] { "organization_id", "environment_id", "inspection_plan_id", "work_order_id", "operation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "periodic_inspection_production_reports",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "periodic_inspection_runtime_contexts",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "periodic_inspection_operations",
                schema: "quality");
        }
    }
}
