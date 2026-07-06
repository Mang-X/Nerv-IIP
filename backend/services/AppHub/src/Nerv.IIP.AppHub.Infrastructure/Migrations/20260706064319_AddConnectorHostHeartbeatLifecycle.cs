using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.AppHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorHostHeartbeatLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "apphub",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending or Replayed.");

            migrationBuilder.AddColumn<string>(
                name: "ConnectorHostId",
                schema: "apphub",
                table: "application_instances",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                comment: "Connector Host protocol identity that owns the instance heartbeat and state reports.");

            migrationBuilder.CreateIndex(
                name: "IX_application_instances_OrganizationId_EnvironmentId_Connecto~",
                schema: "apphub",
                table: "application_instances",
                columns: new[] { "OrganizationId", "EnvironmentId", "ConnectorHostId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_application_instances_OrganizationId_EnvironmentId_Connecto~",
                schema: "apphub",
                table: "application_instances");

            migrationBuilder.DropColumn(
                name: "ConnectorHostId",
                schema: "apphub",
                table: "application_instances");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "apphub",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending or Replayed.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.");
        }
    }
}
