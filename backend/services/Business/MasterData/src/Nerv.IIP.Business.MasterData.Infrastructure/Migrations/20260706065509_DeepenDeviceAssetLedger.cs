using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeepenDeviceAssetLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "parent_device_id",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Parent device asset public id when this asset is a child component or sub-asset.");

            migrationBuilder.AddColumn<decimal>(
                name: "purchase_cost",
                schema: "business_masterdata",
                table: "device_assets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                comment: "Original purchase cost amount in purchase_currency_code.");

            migrationBuilder.AddColumn<string>(
                name: "purchase_currency_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                comment: "Currency code for purchase_cost.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "purchase_date",
                schema: "business_masterdata",
                table: "device_assets",
                type: "date",
                nullable: true,
                comment: "Business date when the asset was purchased.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "retired_on",
                schema: "business_masterdata",
                table: "device_assets",
                type: "date",
                nullable: true,
                comment: "Business date when the asset was retired from active use.");

            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Site code where the device asset is installed.");

            migrationBuilder.AddColumn<string>(
                name: "station_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Station code or local position inside the production line.");

            migrationBuilder.AddColumn<string>(
                name: "supplier_partner_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "BusinessPartner code for the equipment supplier.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "warranty_expires_on",
                schema: "business_masterdata",
                table: "device_assets",
                type: "date",
                nullable: true,
                comment: "Business date when supplier warranty expires.");

            migrationBuilder.AddColumn<string>(
                name: "workshop_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Workshop code where the device asset is installed.");

            migrationBuilder.CreateTable(
                name: "device_asset_components",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Device asset component row id."),
                    component_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Component code within the parent device asset."),
                    component_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Operator-readable component name."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Component quantity installed in the parent asset."),
                    critical = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the component is critical for maintenance decisions."),
                    device_asset_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning device asset id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_asset_components", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_asset_components_device_assets_device_asset_id",
                        column: x => x.device_asset_id,
                        principalSchema: "business_masterdata",
                        principalTable: "device_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Child component rows owned by a business master data device asset.");

            migrationBuilder.CreateIndex(
                name: "IX_device_assets_site_code_workshop_code_line_code_disabled",
                schema: "business_masterdata",
                table: "device_assets",
                columns: new[] { "site_code", "workshop_code", "line_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_device_asset_components_device_asset_id_component_code",
                schema: "business_masterdata",
                table: "device_asset_components",
                columns: new[] { "device_asset_id", "component_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_asset_components",
                schema: "business_masterdata");

            migrationBuilder.DropIndex(
                name: "IX_device_assets_site_code_workshop_code_line_code_disabled",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "parent_device_id",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "purchase_cost",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "purchase_currency_code",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "purchase_date",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "retired_on",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "station_code",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "supplier_partner_code",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "warranty_expires_on",
                schema: "business_masterdata",
                table: "device_assets");

            migrationBuilder.DropColumn(
                name: "workshop_code",
                schema: "business_masterdata",
                table: "device_assets");
        }
    }
}
