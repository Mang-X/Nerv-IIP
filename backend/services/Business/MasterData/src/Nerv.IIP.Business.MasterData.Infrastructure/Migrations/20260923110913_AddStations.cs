using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <summary>
    /// 新增工位主数据，并从存量设备上的 <c>station_code</c> 回填工位记录。
    /// 同一工位编码若挂在不同产线的设备上，迁移直接失败并列出冲突，不替业务挑选归属；
    /// 工作中心只是可选关联，设备间不一致时留空。
    /// </summary>
    public partial class AddStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "station_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Code of the stations master data row the device is bound to; blank when not bound to a station.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Station code or local position inside the production line.");

            migrationBuilder.CreateTable(
                name: "stations",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Station aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the station."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the station is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique station code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Station display name."),
                    line_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Parent production line code; site and workshop are inherited from the line."),
                    work_center_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional capacity/cost work center association; not a hierarchy parent."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the station from new device and execution references."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the station was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the station was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.id);
                },
                comment: "Business master data stations (work units) under a production line.");

            migrationBuilder.CreateIndex(
                name: "IX_stations_line_code_disabled",
                schema: "business_masterdata",
                table: "stations",
                columns: new[] { "line_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_stations_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "stations",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stations_work_center_code_disabled",
                schema: "business_masterdata",
                table: "stations",
                columns: new[] { "work_center_code", "disabled" });

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    conflicts text;
                BEGIN
                    SELECT string_agg(
                               format('%s/%s/%s -> lines [%s]', organization_id, environment_id, station_code, line_codes),
                               '; ' ORDER BY organization_id, environment_id, station_code)
                    INTO conflicts
                    FROM (
                        SELECT organization_id,
                               environment_id,
                               station_code,
                               string_agg(DISTINCT line_code, ', ' ORDER BY line_code) AS line_codes
                        FROM business_masterdata.device_assets
                        WHERE station_code <> ''
                        GROUP BY organization_id, environment_id, station_code
                        HAVING COUNT(DISTINCT line_code) > 1
                    ) AS conflicting;

                    IF conflicts IS NOT NULL THEN
                        RAISE EXCEPTION 'Station codes are attached to devices on different production lines; resolve before migrating: %', conflicts;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO business_masterdata.stations
                    (id, organization_id, environment_id, code, name, line_code, work_center_code, disabled, created_at_utc, updated_at_utc)
                SELECT uuidv7(),
                       organization_id,
                       environment_id,
                       station_code,
                       station_code,
                       MIN(line_code),
                       CASE WHEN COUNT(DISTINCT work_center_code) = 1 THEN MIN(work_center_code) END,
                       BOOL_AND(disabled),
                       MIN(created_at_utc),
                       MAX(updated_at_utc)
                FROM business_masterdata.device_assets
                WHERE station_code <> ''
                GROUP BY organization_id, environment_id, station_code;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stations",
                schema: "business_masterdata");

            migrationBuilder.AlterColumn<string>(
                name: "station_code",
                schema: "business_masterdata",
                table: "device_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Station code or local position inside the production line.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Code of the stations master data row the device is bound to; blank when not bound to a station.");
        }
    }
}
