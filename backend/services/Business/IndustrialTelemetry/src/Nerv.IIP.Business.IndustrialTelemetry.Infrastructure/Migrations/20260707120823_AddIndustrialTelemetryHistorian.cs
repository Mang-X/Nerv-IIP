using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIndustrialTelemetryHistorian : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telemetry_raw_samples",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Raw historian sample identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    tag_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Telemetry tag key represented by this raw historian bucket."),
                    bucket_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Inclusive UTC start of the raw historian bucket."),
                    bucket_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Exclusive UTC end of the raw historian bucket."),
                    bucket_end_unix_time_milliseconds = table.Column<long>(type: "bigint", nullable: false, comment: "Exclusive UTC bucket end represented as Unix time milliseconds for provider-neutral retention scans."),
                    sample_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of collector samples represented by the raw historian bucket."),
                    min_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Minimum numeric value in the raw historian bucket."),
                    max_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Maximum numeric value in the raw historian bucket."),
                    average_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Weighted average input value for historian downsampling."),
                    first_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "First observed numeric value in the raw historian bucket."),
                    last_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Last observed numeric value in the raw historian bucket."),
                    source_sequence = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source sequence used for idempotent raw historian ingestion."),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "External source system that produced the raw historian bucket."),
                    source_connector = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Connector instance or adapter that delivered the raw historian bucket."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the raw historian bucket was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_raw_samples", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry raw historian ingest bucket details.");

            migrationBuilder.CreateTable(
                name: "telemetry_rollups",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Telemetry historian rollup identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    tag_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Telemetry tag key represented by this historian rollup."),
                    grain = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Historian rollup grain: Hourly or Daily."),
                    window_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Inclusive UTC start of the historian rollup window."),
                    window_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Exclusive UTC end of the historian rollup window."),
                    window_end_unix_time_milliseconds = table.Column<long>(type: "bigint", nullable: false, comment: "Exclusive UTC rollup end represented as Unix time milliseconds for provider-neutral retention scans."),
                    sample_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of raw samples represented by the historian rollup."),
                    min_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Minimum numeric value in the historian rollup."),
                    max_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Maximum numeric value in the historian rollup."),
                    average_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Weighted average value in the historian rollup."),
                    first_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "First observed numeric value in the historian rollup."),
                    last_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Last observed numeric value in the historian rollup."),
                    source_sequence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Deterministic historian rollup source sequence for idempotent downsampling."),
                    rolled_up_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the historian rollup was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_rollups", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry historian hourly and daily rollups.");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_raw_samples_organization_id_environment_id_devic~1",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "bucket_start_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_raw_samples_organization_id_environment_id_device~",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "bucket_end_unix_time_milliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_raw_samples_organization_id_environment_id_source~",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples",
                columns: new[] { "organization_id", "environment_id", "source_system", "source_connector", "device_asset_id", "tag_key", "source_sequence" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_rollups_organization_id_environment_id_device_as~1",
                schema: "industrial_telemetry",
                table: "telemetry_rollups",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "grain", "window_start_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_rollups_organization_id_environment_id_device_ass~",
                schema: "industrial_telemetry",
                table: "telemetry_rollups",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "grain", "window_end_unix_time_milliseconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telemetry_raw_samples",
                schema: "industrial_telemetry");

            migrationBuilder.DropTable(
                name: "telemetry_rollups",
                schema: "industrial_telemetry");
        }
    }
}
