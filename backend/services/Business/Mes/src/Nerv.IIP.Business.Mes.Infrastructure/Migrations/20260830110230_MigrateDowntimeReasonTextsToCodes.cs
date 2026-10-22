using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MigrateDowntimeReasonTextsToCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "mes"."work_center_unavailabilities"
                SET "reason" = CASE "reason"
                    WHEN '换型调整' THEN 'DT-SETUP'
                    WHEN '设备故障' THEN 'DT-MECH'
                    WHEN '缺料待工' THEN 'DT-MATERIAL'
                    WHEN '计划保养' THEN 'DT-PM'
                    WHEN '质量停机' THEN 'DT-QUALITY'
                END
                WHERE "reason" IN ('换型调整', '设备故障', '缺料待工', '计划保养', '质量停机');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible: after Up, a directory code may also have been written by
            // the normal registration path. Replacing every matching code with legacy text would
            // corrupt those legitimate facts. Restore a pre-migration backup for operational rollback.
        }
    }
}
