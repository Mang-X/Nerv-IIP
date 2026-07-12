using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlarmShelveIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "shelve_idempotency_key",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Idempotency key of the last applied shelve operation; a delayed duplicate delivery with the same key no-ops regardless of window.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shelve_idempotency_key",
                schema: "industrial_telemetry",
                table: "alarm_events");
        }
    }
}
