using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesOperationTaskStartAuthorizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_task_start_authorizations",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stable authorization fact identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant scope."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment scope."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Operation task authorized to start."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Work order containing the authorized operation."),
                    operation_sequence = table.Column<int>(type: "integer", nullable: false, comment: "Routing sequence captured at authorization time."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Non-empty business reason for the authorized skip."),
                    authorized_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Canonical principal from the trusted internal caller."),
                    correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Request correlation identifier for traceability."),
                    idempotency_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Caller intent key for replay convergence."),
                    authorized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when authorization and start succeeded."),
                    result_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Operation task status returned by the combined command.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_task_start_authorizations", x => x.id);
                },
                comment: "Immutable internal authorization facts for starting an MES operation before preceding operations complete.");

            migrationBuilder.CreateIndex(
                name: "ix_operation_task_start_authorizations_scope_task_timeline",
                schema: "mes",
                table: "operation_task_start_authorizations",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "authorized_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_operation_task_start_authorizations_scope_task_idempotency",
                schema: "mes",
                table: "operation_task_start_authorizations",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_task_start_authorizations",
                schema: "mes");
        }
    }
}
