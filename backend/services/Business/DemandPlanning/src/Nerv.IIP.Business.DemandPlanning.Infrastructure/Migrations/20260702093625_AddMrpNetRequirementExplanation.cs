using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpNetRequirementExplanation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "available_to_net_quantity",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Inventory quantity actually available for netting after reserved and safety stock protection.");

            migrationBuilder.AddColumn<string>(
                name: "formula",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                comment: "Human-readable net requirement formula from persisted MRP inputs.");

            migrationBuilder.AddColumn<decimal>(
                name: "gross_demand_quantity",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Gross requirement quantity before MRP netting.");

            migrationBuilder.AddColumn<decimal>(
                name: "net_requirement_quantity",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Calculated net requirement quantity before lot sizing.");

            migrationBuilder.AddColumn<decimal>(
                name: "on_hand_quantity",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "On-hand inventory quantity snapshot used by MRP netting.");

            migrationBuilder.AddColumn<decimal>(
                name: "planned_quantity",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Planned suggestion quantity after lot sizing.");

            migrationBuilder.AddColumn<string>(
                name: "primary_source_type",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                comment: "Primary requirement source type such as sales, forecast, safety-stock, mps, or component.");

            migrationBuilder.AddColumn<decimal>(
                name: "reserved_quantity",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Reserved inventory quantity snapshot used by MRP netting.");

            migrationBuilder.AddColumn<decimal>(
                name: "safety_stock_quantity",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Safety stock quantity protected during MRP netting.");

            migrationBuilder.AddColumn<decimal>(
                name: "scheduled_receipt_quantity",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Scheduled receipt quantity consumed by MRP netting.");

            migrationBuilder.AddColumn<decimal>(
                name: "scrap_rate",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "BOM scrap rate applied to this requirement explanation.");

            migrationBuilder.AddColumn<string>(
                name: "uom_conversion_summary",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                comment: "UOM conversion summary used while calculating this suggestion.");

            migrationBuilder.AddColumn<decimal>(
                name: "yield_rate",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m,
                comment: "Yield rate applied to this requirement explanation.");

            migrationBuilder.AddColumn<decimal>(
                name: "gross_demand_quantity",
                schema: "demand_planning",
                table: "mrp_pegging_links",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Gross requirement quantity represented by this pegging link.");

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                schema: "demand_planning",
                table: "mrp_pegging_links",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                comment: "Requirement source type such as sales, forecast, safety-stock, mps, component, or scheduled-receipt.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "available_to_net_quantity",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "formula",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "gross_demand_quantity",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "net_requirement_quantity",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "on_hand_quantity",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "planned_quantity",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "primary_source_type",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "reserved_quantity",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "safety_stock_quantity",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "scheduled_receipt_quantity",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "scrap_rate",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "uom_conversion_summary",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "yield_rate",
                schema: "demand_planning",
                table: "planning_suggestions");

            migrationBuilder.DropColumn(
                name: "gross_demand_quantity",
                schema: "demand_planning",
                table: "mrp_pegging_links");

            migrationBuilder.DropColumn(
                name: "source_type",
                schema: "demand_planning",
                table: "mrp_pegging_links");
        }
    }
}
