using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesReworkWorkOrderFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_defect_no",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MES defect number that resolved the rework source work order and operation.");

            migrationBuilder.AddColumn<string>(
                name: "source_lot_no",
                schema: "mes",
                table: "work_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional source lot from the Quality NCR rework request.");

            migrationBuilder.AddColumn<string>(
                name: "source_ncr_code",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality NCR business code retained for rework traceability.");

            migrationBuilder.AddColumn<string>(
                name: "source_ncr_id",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality NCR public id that requested the rework work order.");

            migrationBuilder.AddColumn<string>(
                name: "source_operation_task_id",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional MES source operation task business id for a rework work order.");

            migrationBuilder.AddColumn<string>(
                name: "source_serial_no",
                schema: "mes",
                table: "work_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional source serial from the Quality NCR rework request.");

            migrationBuilder.AddColumn<string>(
                name: "source_work_order_id",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MES source work order business id for a rework work order.");

            migrationBuilder.AddColumn<string>(
                name: "work_order_type",
                schema: "mes",
                table: "work_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "standard",
                comment: "Work order type: standard or rework.");

            migrationBuilder.CreateIndex(
                name: "ux_work_orders_scope_source_ncr",
                schema: "mes",
                table: "work_orders",
                columns: new[] { "organization_id", "environment_id", "source_ncr_id" },
                unique: true,
                filter: "source_ncr_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_rework_source",
                schema: "mes",
                table: "work_orders",
                sql: "(work_order_type = 'standard' AND source_work_order_id IS NULL AND source_operation_task_id IS NULL AND source_defect_no IS NULL AND source_ncr_id IS NULL AND source_ncr_code IS NULL AND source_lot_no IS NULL AND source_serial_no IS NULL) OR (work_order_type = 'rework' AND source_work_order_id IS NOT NULL AND source_defect_no IS NOT NULL AND source_ncr_id IS NOT NULL AND source_ncr_code IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_work_orders_scope_source_ncr",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_rework_source",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_defect_no",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_lot_no",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_ncr_code",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_ncr_id",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_operation_task_id",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_serial_no",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_work_order_id",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "work_order_type",
                schema: "mes",
                table: "work_orders");
        }
    }
}
