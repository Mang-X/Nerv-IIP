using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspectionTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "quality",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending or Replayed.");

            migrationBuilder.CreateTable(
                name: "inspection_tasks",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inspection task aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the task."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the task applies."),
                    inspection_plan_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Matched active inspection plan id."),
                    inspection_record_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Inspection record id created from this task once completed."),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Task source type: receiving, operation or final."),
                    source_service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Upstream service that emitted the task source event."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source document public id."),
                    source_document_line_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional source document line or operation id."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "SKU code awaiting inspection."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quantity awaiting inspection."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Task quantity unit of measure code."),
                    batch_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot or batch number from the source event."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number from the source event."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Inspection task status: pending, in-progress or completed."),
                    assigned_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional inspector user id that started the task."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the task was generated."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the task was last changed."),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC due time for overdue inspection reminders."),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when inspection work started."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when inspection work completed."),
                    overdue_reminder_sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the overdue reminder event was first emitted."),
                    trigger_idempotency_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "Idempotency key derived from the source event and source line.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_tasks", x => x.id);
                },
                comment: "Quality pending inspection task facts generated from upstream receipt and production events.");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_tasks_scope_status_due",
                schema: "quality",
                table: "inspection_tasks",
                columns: new[] { "organization_id", "environment_id", "status", "due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_inspection_tasks_scope_source_sku",
                schema: "quality",
                table: "inspection_tasks",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_service", "source_document_id", "source_document_line_id", "sku_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_inspection_tasks_scope_trigger_key",
                schema: "quality",
                table: "inspection_tasks",
                columns: new[] { "organization_id", "environment_id", "trigger_idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspection_tasks",
                schema: "quality");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "quality",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending or Replayed.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.");
        }
    }
}
