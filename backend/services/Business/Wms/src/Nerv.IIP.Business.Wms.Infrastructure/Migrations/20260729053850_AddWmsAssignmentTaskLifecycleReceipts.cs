using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsAssignmentTaskLifecycleReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional operator assignment snapshot captured when the task is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_team_id",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional team assignment snapshot captured when the task is created.");

            migrationBuilder.AddColumn<string>(
                name: "completed_by",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Operator or system actor that completed the task.");

            migrationBuilder.AddColumn<string>(
                name: "completion_reason",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Audited reason for completion, required for a picking difference.");

            migrationBuilder.AddColumn<DateTime>(
                name: "exception_at_utc",
                schema: "wms",
                table: "warehouse_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the terminal exception was reported.");

            migrationBuilder.AddColumn<string>(
                name: "exception_by",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Operator user id that reported the terminal exception.");

            migrationBuilder.AddColumn<string>(
                name: "exception_code",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Operator-reported exception code.");

            migrationBuilder.AddColumn<string>(
                name: "exception_reason",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Operator-reported exception reason.");

            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional source lot number copied from the execution order line.");

            migrationBuilder.AddColumn<string>(
                name: "serial_no",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional source serial number copied from the execution order line.");

            migrationBuilder.AddColumn<DateTime>(
                name: "started_at_utc",
                schema: "wms",
                table: "warehouse_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when manual execution started.");

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "wms",
                table: "warehouse_tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 1L,
                comment: "Optimistic concurrency token advanced for every successful task mutation.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "outbound_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional operator assignment snapshot captured when the outbound order is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_team_id",
                schema: "wms",
                table: "outbound_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional team assignment snapshot captured when the outbound order is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional operator assignment snapshot captured when the inbound order is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_team_id",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional team assignment snapshot captured when the inbound order is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "count_executions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional operator assignment snapshot captured when the count execution is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_team_id",
                schema: "wms",
                table: "count_executions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional team assignment snapshot captured when the count execution is created.");

            migrationBuilder.CreateTable(
                name: "warehouse_task_action_receipts",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Warehouse task action receipt id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    warehouse_task_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Warehouse task targeted by the manual action."),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Stable manual action name."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Caller-provided idempotency key scoped to the task and action."),
                    payload_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Canonical request payload fingerprint used to reject key reuse with different content."),
                    result_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Task status returned by the first successful execution."),
                    result_version = table.Column<long>(type: "bigint", nullable: false, comment: "Task version returned by the first successful execution."),
                    result_executed_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Executed quantity returned by the first successful execution."),
                    result_difference_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Absolute planned-versus-executed difference returned by the first successful execution."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the durable receipt was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_task_action_receipts", x => x.id);
                },
                comment: "Durable idempotency receipts for manual warehouse task actions.");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_tasks_operator_scope",
                schema: "wms",
                table: "warehouse_tasks",
                columns: new[] { "organization_id", "environment_id", "task_type", "status", "site_code", "assigned_operator_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_tasks_team_scope",
                schema: "wms",
                table: "warehouse_tasks",
                columns: new[] { "organization_id", "environment_id", "task_type", "status", "site_code", "assigned_team_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_operator_scope",
                schema: "wms",
                table: "outbound_orders",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_operator_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_team_scope",
                schema: "wms",
                table: "outbound_orders",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_team_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_orders_operator_scope",
                schema: "wms",
                table: "inbound_orders",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_operator_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_orders_team_scope",
                schema: "wms",
                table: "inbound_orders",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_team_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_count_executions_operator_scope",
                schema: "wms",
                table: "count_executions",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_operator_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_count_executions_team_scope",
                schema: "wms",
                table: "count_executions",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_team_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_task_action_receipts_task",
                schema: "wms",
                table: "warehouse_task_action_receipts",
                columns: new[] { "organization_id", "environment_id", "warehouse_task_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_warehouse_task_action_receipts_key",
                schema: "wms",
                table: "warehouse_task_action_receipts",
                columns: new[] { "organization_id", "environment_id", "warehouse_task_id", "action", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "warehouse_task_action_receipts",
                schema: "wms");

            migrationBuilder.DropIndex(
                name: "ix_warehouse_tasks_operator_scope",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropIndex(
                name: "ix_warehouse_tasks_team_scope",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropIndex(
                name: "ix_outbound_orders_operator_scope",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_outbound_orders_team_scope",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_inbound_orders_operator_scope",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_inbound_orders_team_scope",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_count_executions_operator_scope",
                schema: "wms",
                table: "count_executions");

            migrationBuilder.DropIndex(
                name: "ix_count_executions_team_scope",
                schema: "wms",
                table: "count_executions");

            migrationBuilder.DropColumn(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "assigned_team_id",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "completed_by",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "completion_reason",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "exception_at_utc",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "exception_by",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "exception_code",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "exception_reason",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "lot_no",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "serial_no",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "started_at_utc",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropColumn(
                name: "assigned_team_id",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropColumn(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "assigned_team_id",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "count_executions");

            migrationBuilder.DropColumn(
                name: "assigned_team_id",
                schema: "wms",
                table: "count_executions");
        }
    }
}
