using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesQualityHoldContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quality_hold_contexts",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Quality hold context id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the quality hold context."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order id affected by the inspection result."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional MES operation task id affected by the inspection result."),
                    source_service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source service referenced by the Quality inspection record."),
                    source_document_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source document id referenced by the Quality inspection record."),
                    inspection_record_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Quality inspection record id that last updated this hold context."),
                    inspection_plan_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional Quality inspection plan id used for the inspection result."),
                    result = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Latest Quality inspection result for the MES execution context."),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Latest Quality integration event type applied to this context."),
                    disposition_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional Quality disposition or rejection reason for the hold context."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the latest Quality inspection result was recorded."),
                    active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this Quality context currently blocks MES release or operation start.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_hold_contexts", x => x.id);
                    table.ForeignKey(
                        name: "fk_quality_hold_contexts_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "MES quality hold contexts projected from Quality inspection result facts for work order release and start gates.");

            migrationBuilder.CreateIndex(
                name: "ix_quality_hold_contexts_scope_operation_active",
                schema: "mes",
                table: "quality_hold_contexts",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_quality_hold_contexts_scope_work_order_active",
                schema: "mes",
                table: "quality_hold_contexts",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ux_quality_hold_contexts_scope_source",
                schema: "mes",
                table: "quality_hold_contexts",
                columns: new[] { "organization_id", "environment_id", "source_service", "source_document_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quality_hold_contexts",
                schema: "mes");
        }
    }
}
