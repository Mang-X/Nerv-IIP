using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceInspectionWorkOrderSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "diagnostic_description",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Diagnostic description captured when the work order was opened from an upstream fact.");

            migrationBuilder.AddColumn<string>(
                name: "source_reference_id",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Source fact reference id for source-type idempotency and traceability.");

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Work order source type such as alarm, plan or inspection.");

            migrationBuilder.CreateIndex(
                name: "ux_maintenance_work_orders_source_reference",
                schema: "maintenance",
                table: "maintenance_work_orders",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_reference_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_maintenance_work_orders_source_reference",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "diagnostic_description",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "source_reference_id",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "source_type",
                schema: "maintenance",
                table: "maintenance_work_orders");
        }
    }
}
