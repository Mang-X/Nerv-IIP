using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesOperationTaskStartAuthorizationScopeConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_chain_id",
                schema: "mes",
                table: "operation_task_start_authorizations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                comment: "BusinessApproval chain whose approved decision authorizes this start.");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_operation_tasks_scope_task_work_order",
                schema: "mes",
                table: "operation_tasks",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_task_start_authorizations_organization_id_enviro~1",
                schema: "mes",
                table: "operation_task_start_authorizations",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_task_start_authorizations_organization_id_environ~",
                schema: "mes",
                table: "operation_task_start_authorizations",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_operation_task_start_authorizations_operation_tasks",
                schema: "mes",
                table: "operation_task_start_authorizations",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "work_order_id" },
                principalSchema: "mes",
                principalTable: "operation_tasks",
                principalColumns: new[] { "organization_id", "environment_id", "operation_task_id", "work_order_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_operation_task_start_authorizations_work_orders",
                schema: "mes",
                table: "operation_task_start_authorizations",
                columns: new[] { "organization_id", "environment_id", "work_order_id" },
                principalSchema: "mes",
                principalTable: "work_orders",
                principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_operation_task_start_authorizations_operation_tasks",
                schema: "mes",
                table: "operation_task_start_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_operation_task_start_authorizations_work_orders",
                schema: "mes",
                table: "operation_task_start_authorizations");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_operation_tasks_scope_task_work_order",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropIndex(
                name: "IX_operation_task_start_authorizations_organization_id_enviro~1",
                schema: "mes",
                table: "operation_task_start_authorizations");

            migrationBuilder.DropIndex(
                name: "IX_operation_task_start_authorizations_organization_id_environ~",
                schema: "mes",
                table: "operation_task_start_authorizations");

            migrationBuilder.DropColumn(
                name: "approval_chain_id",
                schema: "mes",
                table: "operation_task_start_authorizations");
        }
    }
}
