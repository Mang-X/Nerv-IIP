using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceControlChannelBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_control_channel_bindings",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Device control channel binding identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier bound to a control channel."),
                    connector_host_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Connector host that owns the device's control channel."),
                    instance_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Connector instance key routed by the Ops operation task for this device."),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the control channel binding is active and usable for dispatch."),
                    disabled_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "Reason captured when the binding was disabled, retained for audit."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the binding was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the binding was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_control_channel_bindings", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry device control channel routing binding (device to connector host/instance).");

            migrationBuilder.CreateIndex(
                name: "IX_device_control_channel_bindings_organization_id_environment~",
                schema: "industrial_telemetry",
                table: "device_control_channel_bindings",
                columns: new[] { "organization_id", "environment_id", "device_asset_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_control_channel_bindings",
                schema: "industrial_telemetry");
        }
    }
}
