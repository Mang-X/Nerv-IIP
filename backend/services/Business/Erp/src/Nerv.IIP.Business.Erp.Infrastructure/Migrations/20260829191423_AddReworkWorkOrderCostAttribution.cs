using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReworkWorkOrderCostAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_ncr_code",
                schema: "erp",
                table: "work_order_costs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality NCR business code retained for rework cost readback.");

            migrationBuilder.AddColumn<string>(
                name: "source_ncr_id",
                schema: "erp",
                table: "work_order_costs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality NCR public id for a rework work-order cost; null for ordinary work orders.");

            migrationBuilder.AddColumn<string>(
                name: "source_work_order_id",
                schema: "erp",
                table: "work_order_costs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MES source work-order public id for a rework work-order cost.");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_costs_organization_id_environment_id_source_ncr_~",
                schema: "erp",
                table: "work_order_costs",
                columns: new[] { "organization_id", "environment_id", "source_ncr_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_costs_organization_id_environment_id_source_work~",
                schema: "erp",
                table: "work_order_costs",
                columns: new[] { "organization_id", "environment_id", "source_work_order_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_work_order_costs_organization_id_environment_id_source_ncr_~",
                schema: "erp",
                table: "work_order_costs");

            migrationBuilder.DropIndex(
                name: "IX_work_order_costs_organization_id_environment_id_source_work~",
                schema: "erp",
                table: "work_order_costs");

            migrationBuilder.DropColumn(
                name: "source_ncr_code",
                schema: "erp",
                table: "work_order_costs");

            migrationBuilder.DropColumn(
                name: "source_ncr_id",
                schema: "erp",
                table: "work_order_costs");

            migrationBuilder.DropColumn(
                name: "source_work_order_id",
                schema: "erp",
                table: "work_order_costs");
        }
    }
}
