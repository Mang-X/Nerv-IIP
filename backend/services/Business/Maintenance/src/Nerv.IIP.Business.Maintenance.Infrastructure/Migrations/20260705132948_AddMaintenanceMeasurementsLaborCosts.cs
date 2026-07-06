using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceMeasurementsLaborCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "actual_labor_minutes",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "integer",
                nullable: true,
                comment: "Actual technician labor minutes captured at completion.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_technician_user_id",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Assigned technician user reference owned outside Maintenance.");

            migrationBuilder.AddColumn<string>(
                name: "cost_currency_code",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                comment: "Currency code for summarized maintenance cost fields.");

            migrationBuilder.AddColumn<int>(
                name: "estimated_labor_minutes",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "integer",
                nullable: true,
                comment: "Estimated technician labor minutes.");

            migrationBuilder.AddColumn<decimal>(
                name: "external_service_cost_amount",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "External service or outsourcing cost amount captured by Maintenance.");

            migrationBuilder.AddColumn<decimal>(
                name: "spare_part_cost_amount",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Summarized spare part cost amount captured by Maintenance.");

            migrationBuilder.CreateTable(
                name: "maintenance_inspection_measurements",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Maintenance inspection measurement line id."),
                    characteristic_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Maintenance-owned characteristic code measured during inspection."),
                    measured_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Measured numeric value."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Unit of measure code for the measured value."),
                    lower_spec_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Optional lower acceptable limit for the measured value."),
                    upper_spec_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Optional upper acceptable limit for the measured value."),
                    is_within_spec = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the measured value is inside the configured acceptable range."),
                    maintenance_inspection_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning maintenance inspection id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_inspection_measurements", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_inspection_measurements_maintenance_inspections~",
                        column: x => x.maintenance_inspection_id,
                        principalSchema: "maintenance",
                        principalTable: "maintenance_inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Measurement value lines captured during Maintenance inspections.");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_inspections_organization_id_environment_id_insp~",
                schema: "maintenance",
                table: "maintenance_inspections",
                columns: new[] { "organization_id", "environment_id", "inspected_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_inspection_measurements_characteristic_code",
                schema: "maintenance",
                table: "maintenance_inspection_measurements",
                column: "characteristic_code");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_inspection_measurements_maintenance_inspection_~",
                schema: "maintenance",
                table: "maintenance_inspection_measurements",
                column: "maintenance_inspection_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_inspection_measurements",
                schema: "maintenance");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_inspections_organization_id_environment_id_insp~",
                schema: "maintenance",
                table: "maintenance_inspections");

            migrationBuilder.DropColumn(
                name: "actual_labor_minutes",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "assigned_technician_user_id",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "cost_currency_code",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "estimated_labor_minutes",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "external_service_cost_amount",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "spare_part_cost_amount",
                schema: "maintenance",
                table: "maintenance_work_orders");
        }
    }
}
