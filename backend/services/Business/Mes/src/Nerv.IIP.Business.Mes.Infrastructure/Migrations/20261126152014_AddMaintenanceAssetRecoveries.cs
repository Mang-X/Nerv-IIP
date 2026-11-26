using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceAssetRecoveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_asset_recoveries",
                schema: "mes",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "组织标识。"),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "环境标识。"),
                    maintenance_work_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "维修工单标识。"),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "设备标识。"),
                    restored_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "首次设备恢复时间。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_asset_recoveries", x => new { x.organization_id, x.environment_id, x.maintenance_work_order_id });
                },
                comment: "维保设备恢复事实，用于关闭已登记或乱序到达的停机窗口。");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_asset_recoveries",
                schema: "mes");
        }
    }
}
