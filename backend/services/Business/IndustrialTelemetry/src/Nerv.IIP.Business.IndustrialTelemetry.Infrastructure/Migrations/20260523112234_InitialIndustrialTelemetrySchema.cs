using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIndustrialTelemetrySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "industrial_telemetry");

            migrationBuilder.CreateTable(
                name: "alarm_events",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Alarm event identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    alarm_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "External or normalized alarm code."),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Alarm severity level."),
                    raised_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the alarm was raised."),
                    external_alarm_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "External alarm identifier used for idempotent ingestion."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Alarm lifecycle status."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the alarm was recorded."),
                    cleared_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the alarm was cleared."),
                    cleared_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Actor or system that cleared the alarm."),
                    clear_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "Reason recorded when the alarm was cleared.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alarm_events", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry controlled alarm lifecycle events.");

            migrationBuilder.CreateTable(
                name: "CAPLock",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CAPLock", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CAPPublishedMessage",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CAPPublishedMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CAPReceivedMessage",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CAPReceivedMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_state_snapshots",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Device state snapshot identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "Normalized device state fact."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the device state was observed."),
                    source_sequence = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source sequence used for idempotent state ingestion."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the state snapshot was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_state_snapshots", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry controlled device state snapshots.");

            migrationBuilder.CreateTable(
                name: "telemetry_summaries",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Telemetry summary identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    tag_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Telemetry tag key summarized by this bucket."),
                    bucket_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Inclusive UTC start of the summary bucket."),
                    bucket_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Exclusive UTC end of the summary bucket."),
                    sample_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of raw samples represented by the summary."),
                    min_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Minimum numeric value in the bucket."),
                    max_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Maximum numeric value in the bucket."),
                    average_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Average numeric value in the bucket."),
                    source_sequence = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Source sequence used for idempotent summary ingestion."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the summary was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_summaries", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry coarse telemetry summary buckets.");

            migrationBuilder.CreateTable(
                name: "telemetry_tags",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Telemetry tag identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    tag_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Telemetry tag key unique within a device stream."),
                    value_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Telemetry value type such as number, bool, or text."),
                    unit_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Unit of measure code for summarized telemetry values."),
                    sampling_policy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Configured ingestion sampling policy."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the tag mapping was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the tag mapping was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_tags", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry telemetry tag mapping metadata.");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_id~",
                schema: "industrial_telemetry",
                table: "alarm_events",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "raised_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_alarm_events_organization_id_environment_id_external_alarm_~",
                schema: "industrial_telemetry",
                table: "alarm_events",
                columns: new[] { "organization_id", "environment_id", "external_alarm_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "industrial_telemetry",
                table: "CAPPublishedMessage",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "industrial_telemetry",
                table: "CAPPublishedMessage",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "industrial_telemetry",
                table: "CAPReceivedMessage",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "industrial_telemetry",
                table: "CAPReceivedMessage",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_device_state_snapshots_organization_id_environment_id_devi~1",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "source_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_state_snapshots_organization_id_environment_id_devic~",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_device_~1",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "source_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_device_a~",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "bucket_start_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_tags_organization_id_environment_id_device_asset_~",
                schema: "industrial_telemetry",
                table: "telemetry_tags",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarm_events",
                schema: "industrial_telemetry");

            migrationBuilder.DropTable(
                name: "CAPLock",
                schema: "industrial_telemetry");

            migrationBuilder.DropTable(
                name: "CAPPublishedMessage",
                schema: "industrial_telemetry");

            migrationBuilder.DropTable(
                name: "CAPReceivedMessage",
                schema: "industrial_telemetry");

            migrationBuilder.DropTable(
                name: "device_state_snapshots",
                schema: "industrial_telemetry");

            migrationBuilder.DropTable(
                name: "telemetry_summaries",
                schema: "industrial_telemetry");

            migrationBuilder.DropTable(
                name: "telemetry_tags",
                schema: "industrial_telemetry");
        }
    }
}
