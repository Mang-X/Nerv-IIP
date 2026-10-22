using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropMesDeviceAssetWorkCenterMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_asset_work_center_mappings",
                schema: "mes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_asset_work_center_mappings",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Device asset work center mapping aggregate id."),
                    device_asset_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Maintenance device asset public id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Environment id; null means the mapping is global."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Organization tenant id; null means the mapping is global."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData work center public id used by MES scheduling.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_asset_work_center_mappings", x => x.id);
                },
                comment: "MES local mapping from Maintenance device asset public ids to MasterData work center public ids.");

            migrationBuilder.CreateIndex(
                name: "ix_asset_wc_mapping_scope_asset",
                schema: "mes",
                table: "device_asset_work_center_mappings",
                columns: new[] { "organization_id", "environment_id", "device_asset_id" },
                unique: true);
        }
    }
}
