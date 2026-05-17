using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.AppHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SchemaGovernanceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "registration_idempotency",
                schema: "apphub",
                comment: "AppHub registration idempotency records used to deduplicate connector retries.");

            migrationBuilder.AlterTable(
                name: "managed_nodes",
                schema: "apphub",
                comment: "AppHub managed connector host or runtime node catalog entries.");

            migrationBuilder.AlterTable(
                name: "instance_status_changes",
                schema: "apphub",
                comment: "AppHub reported status transition history for managed application instances.");

            migrationBuilder.AlterTable(
                name: "instance_state_history",
                schema: "apphub",
                comment: "AppHub observed application instance state history for diagnostics and status timelines.");

            migrationBuilder.AlterTable(
                name: "instance_heartbeat",
                schema: "apphub",
                comment: "AppHub latest heartbeat facts for managed application instances.");

            migrationBuilder.AlterTable(
                name: "applications",
                schema: "apphub",
                comment: "AppHub application catalog aggregate roots scoped by organization and environment.");

            migrationBuilder.AlterTable(
                name: "application_versions",
                schema: "apphub",
                comment: "AppHub application versions owned by an application catalog aggregate.");

            migrationBuilder.AlterTable(
                name: "application_instances",
                schema: "apphub",
                comment: "AppHub managed application instance aggregate roots reported by connector hosts.");

            migrationBuilder.AlterColumn<string>(
                name: "Metadata",
                schema: "apphub",
                table: "application_instances",
                type: "text",
                nullable: false,
                comment: "JSON dictionary produced by Connector Host registration and state reporting, consumed by AppHub and Gateway readers; additive optional keys are compatible, removing or changing key semantics requires Connector Protocol versioning.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Capabilities",
                schema: "apphub",
                table: "application_instances",
                type: "text",
                nullable: false,
                comment: "JSON capability descriptors produced by Connector Host discovery, consumed by Gateway and Ops action routing; additive capabilities are compatible, removing or changing action semantics requires Connector Protocol versioning.",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "registration_idempotency",
                schema: "apphub",
                oldComment: "AppHub registration idempotency records used to deduplicate connector retries.");

            migrationBuilder.AlterTable(
                name: "managed_nodes",
                schema: "apphub",
                oldComment: "AppHub managed connector host or runtime node catalog entries.");

            migrationBuilder.AlterTable(
                name: "instance_status_changes",
                schema: "apphub",
                oldComment: "AppHub reported status transition history for managed application instances.");

            migrationBuilder.AlterTable(
                name: "instance_state_history",
                schema: "apphub",
                oldComment: "AppHub observed application instance state history for diagnostics and status timelines.");

            migrationBuilder.AlterTable(
                name: "instance_heartbeat",
                schema: "apphub",
                oldComment: "AppHub latest heartbeat facts for managed application instances.");

            migrationBuilder.AlterTable(
                name: "applications",
                schema: "apphub",
                oldComment: "AppHub application catalog aggregate roots scoped by organization and environment.");

            migrationBuilder.AlterTable(
                name: "application_versions",
                schema: "apphub",
                oldComment: "AppHub application versions owned by an application catalog aggregate.");

            migrationBuilder.AlterTable(
                name: "application_instances",
                schema: "apphub",
                oldComment: "AppHub managed application instance aggregate roots reported by connector hosts.");

            migrationBuilder.AlterColumn<string>(
                name: "Metadata",
                schema: "apphub",
                table: "application_instances",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "JSON dictionary produced by Connector Host registration and state reporting, consumed by AppHub and Gateway readers; additive optional keys are compatible, removing or changing key semantics requires Connector Protocol versioning.");

            migrationBuilder.AlterColumn<string>(
                name: "Capabilities",
                schema: "apphub",
                table: "application_instances",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "JSON capability descriptors produced by Connector Host discovery, consumed by Gateway and Ops action routing; additive capabilities are compatible, removing or changing action semantics requires Connector Protocol versioning.");
        }
    }
}
