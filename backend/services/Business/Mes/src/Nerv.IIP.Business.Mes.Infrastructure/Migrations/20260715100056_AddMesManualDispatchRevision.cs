using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesManualDispatchRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "held_inspection_document_id",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality inspection plan or document durably associated with the current hold cycle when supplied.");

            migrationBuilder.AddColumn<string>(
                name: "reversed_by",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Authenticated principal reference that performed the production report reversal.");

            migrationBuilder.AddColumn<bool>(
                name: "has_active_manual_dispatch",
                schema: "mes",
                table: "operation_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the operation currently owns an active MES manual-device dispatch lock; false with revision zero and a device remains legacy-unknown.");

            migrationBuilder.AddColumn<long>(
                name: "manual_dispatch_revision",
                schema: "mes",
                table: "operation_tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Monotonic MES manual-device dispatch lifecycle revision; zero is legacy-unknown after upgrade.");

            migrationBuilder.CreateTable(
                name: "quality_hold_transitions",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stable quality hold transition identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization scope for the transition."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment scope for the transition."),
                    source_service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Service that owns the held source document."),
                    source_document_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Stable MES source document identifier whose hold lifecycle changed."),
                    hold_cycle_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Stable identifier correlating an applied hold with its release in one lifecycle cycle."),
                    correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Source command or integration-event correlation identifier for this transition."),
                    event_kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Lifecycle event kind: hold-applied, inspection-released, or manual-force-released."),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Known actor supplied by the transition source; legacy values are not synthesized."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC instant when the lifecycle transition occurred."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Reason supplied by the source transition when available; unknown legacy values remain null."),
                    source_inspection_record_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Quality inspection record that caused the transition when applicable."),
                    source_inspection_document_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Quality inspection plan or document associated with the transition when available."),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Transition origin: automatic or manual."),
                    idempotency_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Governed source idempotency key when supplied; unavailable legacy values remain null.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_hold_transitions", x => x.id);
                },
                comment: "Append-only MES quality hold lifecycle transitions; current state remains in quality_hold_contexts.");

            migrationBuilder.CreateIndex(
                name: "ix_quality_hold_transitions_scope_source_timeline",
                schema: "mes",
                table: "quality_hold_transitions",
                columns: new[] { "organization_id", "environment_id", "source_document_id", "occurred_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_quality_hold_transitions_scope_correlation_kind",
                schema: "mes",
                table: "quality_hold_transitions",
                columns: new[] { "organization_id", "environment_id", "source_service", "source_document_id", "hold_cycle_id", "correlation_id", "event_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_quality_hold_transitions_scope_idempotency_kind",
                schema: "mes",
                table: "quality_hold_transitions",
                columns: new[] { "organization_id", "environment_id", "source_service", "source_document_id", "hold_cycle_id", "idempotency_key", "event_kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quality_hold_transitions",
                schema: "mes");

            migrationBuilder.DropColumn(
                name: "held_inspection_document_id",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "reversed_by",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "has_active_manual_dispatch",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "manual_dispatch_revision",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
