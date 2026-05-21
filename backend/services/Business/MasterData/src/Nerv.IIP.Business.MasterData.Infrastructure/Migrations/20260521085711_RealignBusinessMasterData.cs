using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RealignBusinessMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "capacity_unit",
                schema: "business_masterdata",
                table: "work_centers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Unit for nominal resource capacity, for example minute, liter or kilogram.");

            migrationBuilder.AddColumn<string>(
                name: "default_calendar_code",
                schema: "business_masterdata",
                table: "work_centers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Default work calendar code used for planning capacity.");

            migrationBuilder.AddColumn<bool>(
                name: "finite_capacity",
                schema: "business_masterdata",
                table: "work_centers",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Flag that indicates planning should treat the work center as finite capacity.");

            migrationBuilder.AddColumn<string>(
                name: "line_code",
                schema: "business_masterdata",
                table: "work_centers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Production line code where the work center belongs.");

            migrationBuilder.AddColumn<string>(
                name: "plant_code",
                schema: "business_masterdata",
                table: "work_centers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Plant code where the work center belongs.");

            migrationBuilder.AddColumn<string>(
                name: "resource_type",
                schema: "business_masterdata",
                table: "work_centers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Resource type such as work-center, process-unit, labor-cell or equipment-group.");

            migrationBuilder.AddColumn<string>(
                name: "base_uom_code",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Base unit of measure code used for material master identity.");

            migrationBuilder.AddColumn<string>(
                name: "batch_tracking_policy",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Policy that states whether lot, heat, date code or expiry tracking is required.");

            migrationBuilder.AddColumn<string>(
                name: "default_barcode_rule_code",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Default barcode rule code consumed by BarcodeLabel.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_uom_code",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Default inventory unit of measure code.");

            migrationBuilder.AddColumn<string>(
                name: "manufacturing_uom_code",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Default manufacturing unit of measure code.");

            migrationBuilder.AddColumn<string>(
                name: "material_type",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Material type such as raw material, finished good, packaging or service.");

            migrationBuilder.AddColumn<string>(
                name: "purchase_uom_code",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Default purchasing unit of measure code.");

            migrationBuilder.AddColumn<bool>(
                name: "quality_required",
                schema: "business_masterdata",
                table: "skus",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Flag that indicates Quality must inspect or release this SKU before unrestricted use.");

            migrationBuilder.AddColumn<string>(
                name: "sales_uom_code",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Default sales unit of measure code.");

            migrationBuilder.AddColumn<string>(
                name: "serial_tracking_policy",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Policy that states whether serial number tracking is required.");

            migrationBuilder.AddColumn<string>(
                name: "shelf_life_policy_code",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Shelf life policy code used by Inventory and Quality.");

            migrationBuilder.AddColumn<string>(
                name: "storage_condition_code",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Storage condition code such as ambient, cold or hazardous.");

            migrationBuilder.AddColumn<string>(
                name: "asset_class_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Asset class code used for equipment grouping and maintenance policy.");

            migrationBuilder.AddColumn<string>(
                name: "capacity_uom_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Unit of measure code for static equipment capacity.");

            migrationBuilder.AddColumn<string>(
                name: "criticality",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "",
                comment: "Maintenance and planning criticality code.");

            migrationBuilder.AddColumn<bool>(
                name: "maintainable",
                schema: "business_masterdata",
                table: "device_assets",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Flag that indicates Maintenance can create work orders for this asset.");

            migrationBuilder.AddColumn<string>(
                name: "manufacturer",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                comment: "Equipment manufacturer name.");

            migrationBuilder.AddColumn<decimal>(
                name: "maximum_capacity",
                schema: "business_masterdata",
                table: "device_assets",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Maximum static processing capacity in capacity_uom_code.");

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_capacity",
                schema: "business_masterdata",
                table: "device_assets",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Minimum static processing capacity in capacity_uom_code.");

            migrationBuilder.AddColumn<string>(
                name: "serial_no",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                comment: "Manufacturer serial number or asset serial reference.");

            migrationBuilder.AddColumn<bool>(
                name: "telemetry_enabled",
                schema: "business_masterdata",
                table: "device_assets",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Flag that indicates IndustrialTelemetry may map tags to this asset.");

            migrationBuilder.CreateTable(
                name: "production_lines",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Production line aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the production line."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the production line is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique production line code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Production line display name."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Site or plant code that contains the production line."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the production line from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the production line was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the production line was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_lines", x => x.id);
                },
                comment: "Business master data production lines within a site or plant.");

            migrationBuilder.CreateTable(
                name: "reference_data_codes",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Reference data code aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the reference data code."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the reference data code is valid."),
                    code_set = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Reference code set name such as material-form, asset-class or storage-condition."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique code inside the code set."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Reference data code display name."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the reference code from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the reference code was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the reference code was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_data_codes", x => x.id);
                },
                comment: "Business master data controlled reference codes shared by business domains.");

            migrationBuilder.CreateTable(
                name: "shifts",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Shift aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the shift."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the shift is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique shift code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Shift display name."),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false, comment: "Local start time of the shift."),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false, comment: "Local end time of the shift."),
                    crosses_midnight = table.Column<bool>(type: "boolean", nullable: false, comment: "Flag that indicates the shift ends on the next local day."),
                    paid_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Paid or planned working minutes in the shift."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the shift from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the shift was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the shift was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shifts", x => x.id);
                },
                comment: "Business master data shift definitions used by calendars, teams and execution planning.");

            migrationBuilder.CreateTable(
                name: "sites",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Site aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the site."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the site is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique site or plant code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Site or plant display name."),
                    timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "IANA or business timezone used for local calendars."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the site from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the site was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the site was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sites", x => x.id);
                },
                comment: "Business master data sites or plants used as industrial resource hierarchy roots.");

            migrationBuilder.CreateTable(
                name: "units_of_measure",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Unit of measure aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the unit of measure."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the unit of measure is valid."),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Business unique unit of measure code."),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, comment: "Unit of measure display name."),
                    dimension_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "Physical or business dimension such as mass, volume, count, time or potency."),
                    precision = table.Column<int>(type: "integer", nullable: false, comment: "Decimal precision allowed when values are expressed in this unit."),
                    rounding_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "Rounding mode used when converting values to this unit."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the unit from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the unit was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the unit was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units_of_measure", x => x.id);
                },
                comment: "Business master data units of measure used by material, inventory, quality, planning and execution.");

            migrationBuilder.CreateTable(
                name: "uom_conversions",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "UOM conversion aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the UOM conversion."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the UOM conversion is valid."),
                    from_uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Source unit of measure code."),
                    to_uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Target unit of measure code."),
                    factor = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false, comment: "Positive multiplicative factor used for conversion."),
                    offset = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false, comment: "Additive offset applied after the conversion factor."),
                    precision = table.Column<int>(type: "integer", nullable: false, comment: "Decimal precision used for converted values."),
                    rounding_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "Rounding mode used for converted values."),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false, comment: "Business date from which the conversion rule is effective."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the conversion rule was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the conversion rule was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uom_conversions", x => x.id);
                },
                comment: "Business master data unit conversion rules with effective dates and rounding policy.");

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "production_lines",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_site_code_disabled",
                schema: "business_masterdata",
                table: "production_lines",
                columns: new[] { "site_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_reference_data_codes_code_set_disabled",
                schema: "business_masterdata",
                table: "reference_data_codes",
                columns: new[] { "code_set", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_reference_data_codes_organization_id_environment_id_code_se~",
                schema: "business_masterdata",
                table: "reference_data_codes",
                columns: new[] { "organization_id", "environment_id", "code_set", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shifts_disabled",
                schema: "business_masterdata",
                table: "shifts",
                column: "disabled");

            migrationBuilder.CreateIndex(
                name: "IX_shifts_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "shifts",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sites_disabled",
                schema: "business_masterdata",
                table: "sites",
                column: "disabled");

            migrationBuilder.CreateIndex(
                name: "IX_sites_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "sites",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_units_of_measure_dimension_type_disabled",
                schema: "business_masterdata",
                table: "units_of_measure",
                columns: new[] { "dimension_type", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_units_of_measure_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "units_of_measure",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_uom_conversions_from_uom_code_to_uom_code",
                schema: "business_masterdata",
                table: "uom_conversions",
                columns: new[] { "from_uom_code", "to_uom_code" });

            migrationBuilder.CreateIndex(
                name: "IX_uom_conversions_organization_id_environment_id_from_uom_cod~",
                schema: "business_masterdata",
                table: "uom_conversions",
                columns: new[] { "organization_id", "environment_id", "from_uom_code", "to_uom_code", "effective_from" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "production_lines",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "reference_data_codes",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "shifts",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "sites",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "units_of_measure",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "uom_conversions",
                schema: "business_masterdata");

            migrationBuilder.DropColumn(
                name: "capacity_unit",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "default_calendar_code",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "finite_capacity",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "line_code",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "plant_code",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "resource_type",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "base_uom_code",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "batch_tracking_policy",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "default_barcode_rule_code",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "inventory_uom_code",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "manufacturing_uom_code",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "material_type",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "purchase_uom_code",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "quality_required",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "sales_uom_code",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "serial_tracking_policy",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "shelf_life_policy_code",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "storage_condition_code",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "asset_class_code",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "capacity_uom_code",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "criticality",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "maintainable",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "manufacturer",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "maximum_capacity",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "minimum_capacity",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "serial_no",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "telemetry_enabled",
                schema: "business_masterdata",
                table: "device_assets");
        }
    }
}
