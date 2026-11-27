using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceAlarmClearances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_alarm_clearances",
                schema: "maintenance",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "告警所属组织。"),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "告警所属环境。"),
                    alarm_event_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "遥测告警记录 ID。"),
                    cleared_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "遥测记录的告警清除时间。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_alarm_clearances", x => new { x.organization_id, x.environment_id, x.alarm_event_id });
                },
                comment: "维修工单关联所需的告警清除事实。");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_alarm_clearances",
                schema: "maintenance");
        }
    }
}
