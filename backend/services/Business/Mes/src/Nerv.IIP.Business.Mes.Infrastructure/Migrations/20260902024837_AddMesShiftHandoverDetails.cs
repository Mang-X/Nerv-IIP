using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesShiftHandoverDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "open_issue_count",
                schema: "mes",
                table: "shift_handovers",
                type: "integer",
                nullable: false,
                comment: "Environment-level count of still-open shop-floor facts derived when the handover was created; not the number of shift_handover_open_issues rows.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Number of open issues captured when the handover was created.");

            migrationBuilder.AddColumn<string>(
                name: "incoming_user_id",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Identity of the worker taking the shift over; written when the handover is accepted.");

            migrationBuilder.AddColumn<string>(
                name: "incoming_user_name",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Display name of the incoming worker captured at acceptance time.");

            migrationBuilder.AddColumn<string>(
                name: "outgoing_user_id",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Identity of the worker handing the shift over.");

            migrationBuilder.AddColumn<string>(
                name: "outgoing_user_name",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Display name of the outgoing worker captured at handover time; snapshot so the read face needs no directory call.");

            migrationBuilder.CreateTable(
                name: "shift_handover_open_issues",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Shift handover open issue id."),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Open issue category: Equipment or Quality."),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Severity judged by the outgoing team: Low, Medium or High."),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "What the incoming team has to deal with, in the outgoing team's own words."),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional business id of the originating fact such as a downtime event or defect record."),
                    shift_handover_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning shift handover aggregate id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_handover_open_issues", x => x.id);
                    table.CheckConstraint("ck_shift_handover_open_issues_category", "category IN ('Equipment', 'Quality')");
                    table.CheckConstraint("ck_shift_handover_open_issues_severity", "severity IN ('Low', 'Medium', 'High')");
                    table.ForeignKey(
                        name: "fk_shift_handover_open_issues_handovers",
                        column: x => x.shift_handover_id,
                        principalSchema: "mes",
                        principalTable: "shift_handovers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "MES equipment and quality problems handed over unresolved to the incoming team.");

            migrationBuilder.CreateTable(
                name: "shift_handover_unfinished_work_orders",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Shift handover unfinished work-order line id."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order business id carried over to the incoming team."),
                    planned_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Work-order planned quantity captured at handover time."),
                    completed_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Work-order completed quantity captured at handover time."),
                    work_order_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Work-order status captured at handover time."),
                    shift_handover_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning shift handover aggregate id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_handover_unfinished_work_orders", x => x.id);
                    table.CheckConstraint("ck_shift_handover_unfinished_work_orders_progress", "planned_quantity > 0 AND completed_quantity >= 0 AND completed_quantity < planned_quantity");
                    table.ForeignKey(
                        name: "fk_shift_handover_unfinished_work_orders_handovers",
                        column: x => x.shift_handover_id,
                        principalSchema: "mes",
                        principalTable: "shift_handovers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "MES unfinished work orders carried into the next shift, with progress frozen at handover time.");

            migrationBuilder.CreateTable(
                name: "shift_handover_wip_items",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Shift handover WIP count line id."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order business id the WIP quantity belongs to."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Operation task the WIP sits on; null when counted at work-order granularity."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "WIP quantity counted at handover time."),
                    shift_handover_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning shift handover aggregate id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_handover_wip_items", x => x.id);
                    table.CheckConstraint("ck_shift_handover_wip_items_quantity", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_shift_handover_wip_items_handovers",
                        column: x => x.shift_handover_id,
                        principalSchema: "mes",
                        principalTable: "shift_handovers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "MES WIP count lines frozen at shift handover time; never recomputed from work orders.");

            migrationBuilder.CreateIndex(
                name: "ix_shift_handover_open_issues_handover",
                schema: "mes",
                table: "shift_handover_open_issues",
                column: "shift_handover_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_handover_unfinished_work_orders_handover",
                schema: "mes",
                table: "shift_handover_unfinished_work_orders",
                column: "shift_handover_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_handover_wip_items_handover",
                schema: "mes",
                table: "shift_handover_wip_items",
                column: "shift_handover_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_handover_open_issues",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "shift_handover_unfinished_work_orders",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "shift_handover_wip_items",
                schema: "mes");

            migrationBuilder.DropColumn(
                name: "incoming_user_id",
                schema: "mes",
                table: "shift_handovers");

            migrationBuilder.DropColumn(
                name: "incoming_user_name",
                schema: "mes",
                table: "shift_handovers");

            migrationBuilder.DropColumn(
                name: "outgoing_user_id",
                schema: "mes",
                table: "shift_handovers");

            migrationBuilder.DropColumn(
                name: "outgoing_user_name",
                schema: "mes",
                table: "shift_handovers");

            migrationBuilder.AlterColumn<int>(
                name: "open_issue_count",
                schema: "mes",
                table: "shift_handovers",
                type: "integer",
                nullable: false,
                comment: "Number of open issues captured when the handover was created.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Environment-level count of still-open shop-floor facts derived when the handover was created; not the number of shift_handover_open_issues rows.");
        }
    }
}
