using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryTagDisplayNameAndSoftDisable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disabled_at_utc",
                schema: "industrial_telemetry",
                table: "telemetry_tags",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the telemetry tag was disabled; null while enabled.");

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                schema: "industrial_telemetry",
                table: "telemetry_tags",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional human-readable telemetry tag name maintained in the product; the tag key stays the connector-facing identifier.");

            migrationBuilder.AddColumn<bool>(
                name: "is_enabled",
                schema: "industrial_telemetry",
                table: "telemetry_tags",
                type: "boolean",
                nullable: false,
                // 既有点位全部是在用点位：回填 true，否则迁移后所有历史点位都会被当成已停用、计数中断。
                defaultValue: true,
                comment: "Soft-disable flag; disabled tags stop counting production and are hidden from tag catalogs while historical samples stay traceable.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "disabled_at_utc",
                schema: "industrial_telemetry",
                table: "telemetry_tags");

            migrationBuilder.DropColumn(
                name: "display_name",
                schema: "industrial_telemetry",
                table: "telemetry_tags");

            migrationBuilder.DropColumn(
                name: "is_enabled",
                schema: "industrial_telemetry",
                table: "telemetry_tags");
        }
    }
}
