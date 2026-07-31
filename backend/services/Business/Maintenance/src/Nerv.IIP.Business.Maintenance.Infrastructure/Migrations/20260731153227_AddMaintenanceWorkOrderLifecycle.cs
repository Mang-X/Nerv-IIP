using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceWorkOrderLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "accepted_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the assigned technician accepted the work order.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_team_id",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Assigned maintenance team reference owned by MasterData.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the work order was cancelled.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the verified work order was closed.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when repair outcome was verified.");

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Optimistic concurrency version advanced by assignment and lifecycle actions.");

            migrationBuilder.CreateTable(
                name: "maintenance_work_order_lifecycle_events",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Lifecycle event id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    maintenance_work_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning maintenance work order id."),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Assignment or lifecycle action."),
                    from_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Authoritative status before the action."),
                    to_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Authoritative status after the action."),
                    actor_principal_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Authenticated principal that performed the action."),
                    technician_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Technician responsible at this action step."),
                    team_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Maintenance team responsible at this action step."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Business reason for the action."),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Intent-level idempotency key."),
                    payload_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "SHA-256 fingerprint used to reject same-key different-payload replays."),
                    resulting_version = table.Column<int>(type: "integer", nullable: false, comment: "Work-order version after the action."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Authoritative UTC action time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_work_order_lifecycle_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_work_order_lifecycle_events_maintenance_work_or~",
                        column: x => x.maintenance_work_order_id,
                        principalSchema: "maintenance",
                        principalTable: "maintenance_work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Append-only auditable maintenance assignment and lifecycle action receipts.");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_work_order_lifecycle_events_maintenance_work_or~",
                schema: "maintenance",
                table: "maintenance_work_order_lifecycle_events",
                column: "maintenance_work_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_work_order_lifecycle_events_organization_id_en~1",
                schema: "maintenance",
                table: "maintenance_work_order_lifecycle_events",
                columns: new[] { "organization_id", "environment_id", "maintenance_work_order_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_work_order_lifecycle_events_organization_id_env~",
                schema: "maintenance",
                table: "maintenance_work_order_lifecycle_events",
                columns: new[] { "organization_id", "environment_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_work_order_lifecycle_events",
                schema: "maintenance");

            migrationBuilder.DropColumn(
                name: "accepted_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "assigned_team_id",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "cancelled_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "closed_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "verified_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "maintenance",
                table: "maintenance_work_orders");
        }
    }
}
