using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionTaskAssignmentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "assigned_user_id",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional currently assigned inspector principal id.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "Optional inspector user id that started the task.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_team_id",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional trusted team id that owns the inspection work pool.");

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "quality",
                table: "inspection_tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Optimistic version advanced for assignment and lifecycle changes.");

            migrationBuilder.CreateTable(
                name: "inspection_task_assignment_receipts",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Assignment receipt id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    inspection_task_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inspection task id."),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Assignment action: assign, claim or transfer."),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Stable action intent key."),
                    payload_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Canonical action payload fingerprint."),
                    actor_principal_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Trusted authenticated actor principal id."),
                    previous_inspector_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Inspector assignment before the action."),
                    previous_team_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Team assignment before the action."),
                    assigned_inspector_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Inspector assignment after the action."),
                    assigned_team_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Team assignment after the action."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Required transfer reason or optional assignment note."),
                    result_version = table.Column<long>(type: "bigint", nullable: false, comment: "Authoritative task version after the action."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the receipt was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_task_assignment_receipts", x => x.id);
                },
                comment: "Durable idempotency and audit receipts for inspection task assignment, claim and transfer.");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_tasks_inspector_scope",
                schema: "quality",
                table: "inspection_tasks",
                columns: new[] { "organization_id", "environment_id", "assigned_user_id", "status", "due_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_tasks_team_scope",
                schema: "quality",
                table: "inspection_tasks",
                columns: new[] { "organization_id", "environment_id", "assigned_team_id", "status", "due_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_task_assignment_receipts_task",
                schema: "quality",
                table: "inspection_task_assignment_receipts",
                columns: new[] { "organization_id", "environment_id", "inspection_task_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_inspection_task_assignment_receipts_key",
                schema: "quality",
                table: "inspection_task_assignment_receipts",
                columns: new[] { "organization_id", "environment_id", "inspection_task_id", "action", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspection_task_assignment_receipts",
                schema: "quality");

            migrationBuilder.DropIndex(
                name: "ix_inspection_tasks_inspector_scope",
                schema: "quality",
                table: "inspection_tasks");

            migrationBuilder.DropIndex(
                name: "ix_inspection_tasks_team_scope",
                schema: "quality",
                table: "inspection_tasks");

            migrationBuilder.DropColumn(
                name: "assigned_team_id",
                schema: "quality",
                table: "inspection_tasks");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "quality",
                table: "inspection_tasks");

            migrationBuilder.AlterColumn<string>(
                name: "assigned_user_id",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional inspector user id that started the task.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "Optional currently assigned inspector principal id.");
        }
    }
}
