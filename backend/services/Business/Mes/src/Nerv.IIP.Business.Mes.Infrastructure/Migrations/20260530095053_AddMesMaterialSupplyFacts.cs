using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesMaterialSupplyFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_production_reports_organization_id_environment_id_report_no",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "report_no" });

            migrationBuilder.CreateTable(
                name: "material_issue_requests",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Material issue request aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the material issue request."),
                    request_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES material issue request number allocated by the service numbering counter."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order id requesting materials."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional MES operation task id requesting materials."),
                    material_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Material SKU id requested for staging or line-side receipt."),
                    material_lot_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Actual material lot id received line-side, when known."),
                    requested_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Requested material issue quantity."),
                    received_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Confirmed line-side received quantity."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Material issue lifecycle status within MES."),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the material issue request was created."),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when line-side receipt was confirmed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_issue_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_material_issue_requests_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "MES material issue and line-side receipt facts tracking requested, received and consumed material quantities for work orders.");

            migrationBuilder.CreateTable(
                name: "material_requirements",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Material requirement snapshot id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the material readiness context."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order id this material requirement belongs to."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional MES operation task id this requirement belongs to."),
                    material_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData material SKU id required by the work order or operation."),
                    material_lot_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional preferred or allocated material lot id from Inventory/WMS readiness."),
                    required_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Required material quantity from released MBOM or operation demand snapshot."),
                    available_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Available Inventory quantity snapshot for this requirement."),
                    staged_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "WMS staged quantity snapshot for this requirement."),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning source system that produced the material readiness snapshot."),
                    source_snapshot_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source snapshot id or version used to trace the readiness calculation."),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when MES captured this material requirement readiness snapshot.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_requirements", x => x.id);
                    table.ForeignKey(
                        name: "fk_material_requirements_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "MES material requirement snapshots captured from released MBOM, Inventory and WMS readiness facts for work order execution.");

            migrationBuilder.CreateTable(
                name: "production_report_material_consumptions",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Production report material consumption id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the material consumption fact."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES production report number that consumed this material lot."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order id associated with the material consumption."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation task id associated with the material consumption."),
                    material_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Material SKU id consumed by the production report."),
                    material_lot_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Actual material lot id consumed by the production report."),
                    consumed_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Consumed material quantity for this lot."),
                    material_issue_request_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES material issue request number that supplied the consumed lot.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_report_material_consumptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_material_consumptions_reports",
                        columns: x => new { x.organization_id, x.environment_id, x.report_no },
                        principalSchema: "mes",
                        principalTable: "production_reports",
                        principalColumns: new[] { "organization_id", "environment_id", "report_no" },
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "MES material lot consumption facts referenced by production reports for work order and material traceability.");

            migrationBuilder.CreateIndex(
                name: "ix_material_issue_requests_scope_operation",
                schema: "mes",
                table: "material_issue_requests",
                columns: new[] { "organization_id", "environment_id", "operation_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_material_issue_requests_scope_work_order_material",
                schema: "mes",
                table: "material_issue_requests",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "material_id" });

            migrationBuilder.CreateIndex(
                name: "ux_material_issue_requests_scope_request_no",
                schema: "mes",
                table: "material_issue_requests",
                columns: new[] { "organization_id", "environment_id", "request_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_material_requirements_scope_operation",
                schema: "mes",
                table: "material_requirements",
                columns: new[] { "organization_id", "environment_id", "operation_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_material_requirements_scope_work_order_material",
                schema: "mes",
                table: "material_requirements",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "material_id", "material_lot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_material_consumptions_scope_lot",
                schema: "mes",
                table: "production_report_material_consumptions",
                columns: new[] { "organization_id", "environment_id", "material_lot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_material_consumptions_scope_work_order",
                schema: "mes",
                table: "production_report_material_consumptions",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_report_material_consumptions_report_material_lot",
                schema: "mes",
                table: "production_report_material_consumptions",
                columns: new[] { "organization_id", "environment_id", "report_no", "material_id", "material_lot_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_issue_requests",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "material_requirements",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "production_report_material_consumptions",
                schema: "mes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_production_reports_organization_id_environment_id_report_no",
                schema: "mes",
                table: "production_reports");
        }
    }
}
