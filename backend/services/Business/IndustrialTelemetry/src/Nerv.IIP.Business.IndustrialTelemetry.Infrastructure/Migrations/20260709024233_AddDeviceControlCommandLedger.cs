using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceControlCommandLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_control_commands",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Device control command ledger identifier."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Ops operation task identifier; the external command id resolved by the read-face."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    connector_host_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Connector host that owns the target device control channel."),
                    instance_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Connector instance key routed by the Ops operation task."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    command_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Device control command type such as write-tag, start-stop or parameter-set."),
                    tag_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Telemetry tag key targeted by single-tag commands; null for parameter-set commands."),
                    value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Requested control value for single-tag commands; null for parameter-set commands."),
                    parameters_json = table.Column<string>(type: "text", nullable: true, comment: "JSON object of parameter-set command inputs (tag key to value); null for single-tag commands."),
                    requested_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Authenticated principal recorded as the command requester."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Operator-supplied reason captured for the control command audit."),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Idempotency key bound to the Ops operation task creation."),
                    correlation_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Correlation identifier propagated to the Ops operation task."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Dispatch-time Ops task status snapshot; the single-command read-face refreshes live status from Ops."),
                    approval_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Dispatch-time Ops approval status snapshot when the command required approval."),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the command was dispatched to Ops."),
                    requested_at_unix_time_milliseconds = table.Column<long>(type: "bigint", nullable: false, comment: "Requested UTC time as Unix time milliseconds for provider-neutral history range filtering and ordering."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the ledger row was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_control_commands", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry device control command ledger projecting Ops operation tasks for result/history read-face.");

            migrationBuilder.CreateIndex(
                name: "IX_device_control_commands_operation_task_id",
                schema: "industrial_telemetry",
                table: "device_control_commands",
                column: "operation_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_control_commands_organization_id_environment_id_devi~",
                schema: "industrial_telemetry",
                table: "device_control_commands",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "requested_at_unix_time_milliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_device_control_commands_organization_id_environment_id_requ~",
                schema: "industrial_telemetry",
                table: "device_control_commands",
                columns: new[] { "organization_id", "environment_id", "requested_at_unix_time_milliseconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_control_commands",
                schema: "industrial_telemetry");
        }
    }
}
