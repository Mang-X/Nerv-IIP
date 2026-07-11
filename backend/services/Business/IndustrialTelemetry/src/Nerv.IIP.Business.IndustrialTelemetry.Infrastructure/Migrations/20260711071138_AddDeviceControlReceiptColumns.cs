using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceControlReceiptColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "device_receipt_code",
                schema: "industrial_telemetry",
                table: "device_control_commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Device-reported receipt code from the connector attempt output (e.g. Good/BadOutOfRange); null when no attempt output was captured.");

            migrationBuilder.AddColumn<string>(
                name: "device_receipt_message",
                schema: "industrial_telemetry",
                table: "device_control_commands",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Human-readable device receipt message from the connector attempt output; null otherwise.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "device_receipt_code",
                schema: "industrial_telemetry",
                table: "device_control_commands");

            migrationBuilder.DropColumn(
                name: "device_receipt_message",
                schema: "industrial_telemetry",
                table: "device_control_commands");
        }
    }
}
