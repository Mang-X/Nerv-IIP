using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesEngineeringChangeWorkOrderImpacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "engineering_change_work_order_impacts",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "MES engineering change impact identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the MES execution context."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order id affected by the ECO, or production-version marker id for archived-version guards."),
                    sku_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU public id for the affected work order, or * for archived production-version marker rows."),
                    work_order_status_at_detection = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MES work order status observed when the ECO impact was detected."),
                    change_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "ProductEngineering ECO number that caused the MES impact."),
                    archived_production_version_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "ProductEngineering production version id archived by the ECO release."),
                    superseded_by_production_version_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Successor ProductEngineering production version id when the ECO declares one."),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Factory business date when the ECO became effective."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MES ECO impact status: archived-production-version, pending-decision, auto-rebound, blocked-for-manual-confirmation, or decided."),
                    detected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when MES consumed the ECO release and detected this impact."),
                    decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Planner or process-engineer decision for a started affected work order."),
                    decided_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "User or actor id that recorded the MES ECO decision."),
                    decision_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Human-readable basis for continuing or aborting the affected work order."),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the MES ECO decision was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engineering_change_work_order_impacts", x => x.id);
                },
                comment: "MES work-order impacts and archived production-version references detected from ProductEngineering ECO release events.");

            migrationBuilder.CreateIndex(
                name: "ix_eco_impacts_scope_archived_pv_status",
                schema: "mes",
                table: "engineering_change_work_order_impacts",
                columns: new[] { "organization_id", "environment_id", "archived_production_version_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_eco_impacts_scope_work_order_change",
                schema: "mes",
                table: "engineering_change_work_order_impacts",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "change_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engineering_change_work_order_impacts",
                schema: "mes");
        }
    }
}
