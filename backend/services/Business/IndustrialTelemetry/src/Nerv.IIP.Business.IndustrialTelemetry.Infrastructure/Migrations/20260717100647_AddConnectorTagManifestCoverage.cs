using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorTagManifestCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "collection_connector_id",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Canonical collection connector identity used for manifest coverage joins when supplied.");

            migrationBuilder.AddColumn<string>(
                name: "collection_connector_id",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Canonical collection connector identity retained as nullable sample provenance.");

            migrationBuilder.CreateTable(
                name: "connector_tag_manifests",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Connector tag manifest identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    collection_connector_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Canonical collection connector identity owning this manifest."),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source system that observed the accepted manifest."),
                    manifest_revision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Lowercase SHA-256 revision of the accepted manifest payload."),
                    manifest_observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Source observation time ordering accepted manifest revisions.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_tag_manifests", x => x.id);
                },
                comment: "BusinessIndustrialTelemetry accepted connector tag manifest revisions.");

            migrationBuilder.CreateTable(
                name: "connector_tag_bindings",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Connector tag binding identifier."),
                    connector_tag_manifest_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning connector tag manifest identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier duplicated for scoped lookups."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier duplicated for scoped lookups."),
                    collection_connector_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Canonical collection connector identity owning the binding."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    tag_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Normalized telemetry tag key exposed by the connector."),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the manifest enables collection for the binding."),
                    protocol_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional protocol-native source address for diagnostics."),
                    is_current = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the binding is present in the accepted manifest revision."),
                    retired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Accepted manifest observation time that retired the binding."),
                    activation_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Latest independently ordered activation status."),
                    activation_observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Source observation time ordering activation updates."),
                    activation_error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Sanitized bounded connector activation error code."),
                    activation_error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Sanitized bounded connector activation error message.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_tag_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_connector_tag_bindings_connector_tag_manifests_connector_ta~",
                        column: x => x.connector_tag_manifest_id,
                        principalSchema: "industrial_telemetry",
                        principalTable: "connector_tag_manifests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "BusinessIndustrialTelemetry current and retired connector device-tag bindings.");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_summaries_connector_coverage",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                columns: new[] { "organization_id", "environment_id", "collection_connector_id", "device_asset_id", "tag_key", "bucket_end_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_connector_tag_bindings_connector_tag_manifest_id_is_current",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                columns: new[] { "connector_tag_manifest_id", "is_current" });

            migrationBuilder.CreateIndex(
                name: "IX_connector_tag_bindings_organization_id_environment_id_colle~",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                columns: new[] { "organization_id", "environment_id", "collection_connector_id", "device_asset_id", "tag_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_connector_tag_manifests_organization_id_environment_id_coll~",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests",
                columns: new[] { "organization_id", "environment_id", "collection_connector_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connector_tag_bindings",
                schema: "industrial_telemetry");

            migrationBuilder.DropTable(
                name: "connector_tag_manifests",
                schema: "industrial_telemetry");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_summaries_connector_coverage",
                schema: "industrial_telemetry",
                table: "telemetry_summaries");

            migrationBuilder.DropColumn(
                name: "collection_connector_id",
                schema: "industrial_telemetry",
                table: "telemetry_summaries");

            migrationBuilder.DropColumn(
                name: "collection_connector_id",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples");
        }
    }
}
