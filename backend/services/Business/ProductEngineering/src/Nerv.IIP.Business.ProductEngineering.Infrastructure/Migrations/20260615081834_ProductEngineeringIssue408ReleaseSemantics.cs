using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductEngineeringIssue408ReleaseSemantics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "control_key",
                schema: "product_engineering",
                table: "routing_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Standard operation control key snapshot.");

            migrationBuilder.AddColumn<bool>(
                name: "is_outsourced",
                schema: "product_engineering",
                table: "routing_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this routing operation is outsourced.");

            migrationBuilder.AddColumn<bool>(
                name: "requires_quality_inspection",
                schema: "product_engineering",
                table: "routing_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether quality inspection is expected for this routing operation.");

            migrationBuilder.AddColumn<bool>(
                name: "requires_reporting",
                schema: "product_engineering",
                table: "routing_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether MES reporting is expected for this routing operation.");

            migrationBuilder.AddColumn<int>(
                name: "run_minutes",
                schema: "product_engineering",
                table: "routing_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Run duration snapshot in minutes.");

            migrationBuilder.AddColumn<int>(
                name: "setup_minutes",
                schema: "product_engineering",
                table: "routing_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Setup duration snapshot in minutes.");

            migrationBuilder.AddColumn<int>(
                name: "teardown_minutes",
                schema: "product_engineering",
                table: "routing_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Teardown duration snapshot in minutes.");

            migrationBuilder.AddColumn<string>(
                name: "alternate_group",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional alternate material group.");

            migrationBuilder.AddColumn<int>(
                name: "alternate_priority",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines",
                type: "integer",
                nullable: true,
                comment: "Optional priority within the alternate material group.");

            migrationBuilder.AddColumn<bool>(
                name: "backflush",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this material is normally backflushed during execution.");

            migrationBuilder.AddColumn<bool>(
                name: "is_phantom",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this consumed material is a phantom component exploded during planning or execution.");

            migrationBuilder.AddColumn<string>(
                name: "reference_designators",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Optional reference designators or positions for this material line.");

            migrationBuilder.AddColumn<string>(
                name: "substitute_sku_codes",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Semicolon-delimited substitute SKU codes captured at MBOM release.");

            migrationBuilder.AddColumn<decimal>(
                name: "yield_rate",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m,
                comment: "Expected material yield rate.");

            migrationBuilder.AddColumn<string>(
                name: "alternate_group",
                schema: "product_engineering",
                table: "engineering_bom_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional alternate item group for interchangeable EBOM components.");

            migrationBuilder.AddColumn<int>(
                name: "alternate_priority",
                schema: "product_engineering",
                table: "engineering_bom_lines",
                type: "integer",
                nullable: true,
                comment: "Optional priority within the alternate component group.");

            migrationBuilder.AddColumn<bool>(
                name: "backflush",
                schema: "product_engineering",
                table: "engineering_bom_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this component is normally backflushed during execution.");

            migrationBuilder.AddColumn<bool>(
                name: "is_phantom",
                schema: "product_engineering",
                table: "engineering_bom_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this component is a phantom item exploded during planning or execution.");

            migrationBuilder.AddColumn<string>(
                name: "reference_designators",
                schema: "product_engineering",
                table: "engineering_bom_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Optional reference designators or positions for this component line.");

            migrationBuilder.AddColumn<decimal>(
                name: "scrap_rate",
                schema: "product_engineering",
                table: "engineering_bom_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Expected component scrap rate for engineering planning.");

            migrationBuilder.AddColumn<decimal>(
                name: "yield_rate",
                schema: "product_engineering",
                table: "engineering_bom_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m,
                comment: "Expected component yield rate for engineering planning.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "control_key",
                schema: "product_engineering",
                table: "routing_operations");

            migrationBuilder.DropColumn(
                name: "is_outsourced",
                schema: "product_engineering",
                table: "routing_operations");

            migrationBuilder.DropColumn(
                name: "requires_quality_inspection",
                schema: "product_engineering",
                table: "routing_operations");

            migrationBuilder.DropColumn(
                name: "requires_reporting",
                schema: "product_engineering",
                table: "routing_operations");

            migrationBuilder.DropColumn(
                name: "run_minutes",
                schema: "product_engineering",
                table: "routing_operations");

            migrationBuilder.DropColumn(
                name: "setup_minutes",
                schema: "product_engineering",
                table: "routing_operations");

            migrationBuilder.DropColumn(
                name: "teardown_minutes",
                schema: "product_engineering",
                table: "routing_operations");

            migrationBuilder.DropColumn(
                name: "alternate_group",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines");

            migrationBuilder.DropColumn(
                name: "alternate_priority",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines");

            migrationBuilder.DropColumn(
                name: "backflush",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines");

            migrationBuilder.DropColumn(
                name: "is_phantom",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines");

            migrationBuilder.DropColumn(
                name: "reference_designators",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines");

            migrationBuilder.DropColumn(
                name: "substitute_sku_codes",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines");

            migrationBuilder.DropColumn(
                name: "yield_rate",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines");

            migrationBuilder.DropColumn(
                name: "alternate_group",
                schema: "product_engineering",
                table: "engineering_bom_lines");

            migrationBuilder.DropColumn(
                name: "alternate_priority",
                schema: "product_engineering",
                table: "engineering_bom_lines");

            migrationBuilder.DropColumn(
                name: "backflush",
                schema: "product_engineering",
                table: "engineering_bom_lines");

            migrationBuilder.DropColumn(
                name: "is_phantom",
                schema: "product_engineering",
                table: "engineering_bom_lines");

            migrationBuilder.DropColumn(
                name: "reference_designators",
                schema: "product_engineering",
                table: "engineering_bom_lines");

            migrationBuilder.DropColumn(
                name: "scrap_rate",
                schema: "product_engineering",
                table: "engineering_bom_lines");

            migrationBuilder.DropColumn(
                name: "yield_rate",
                schema: "product_engineering",
                table: "engineering_bom_lines");
        }
    }
}
