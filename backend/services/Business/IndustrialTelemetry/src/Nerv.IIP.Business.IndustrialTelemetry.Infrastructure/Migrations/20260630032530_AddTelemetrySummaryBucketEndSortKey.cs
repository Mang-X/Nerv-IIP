using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetrySummaryBucketEndSortKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_device_a~",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                newName: "IX_telemetry_summaries_organization_id_environment_id_device_~1");

            migrationBuilder.AddColumn<long>(
                name: "bucket_end_unix_time_milliseconds",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Exclusive UTC bucket end represented as Unix time milliseconds for provider-neutral ordering and late-bucket checks.");

            migrationBuilder.Sql(
                """
                UPDATE industrial_telemetry.telemetry_summaries
                SET bucket_end_unix_time_milliseconds = (EXTRACT(EPOCH FROM bucket_end_utc) * 1000)::bigint;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_device_a~",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "bucket_end_unix_time_milliseconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_device_a~",
                schema: "industrial_telemetry",
                table: "telemetry_summaries");

            migrationBuilder.DropColumn(
                name: "bucket_end_unix_time_milliseconds",
                schema: "industrial_telemetry",
                table: "telemetry_summaries");

            migrationBuilder.RenameIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_device_~1",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                newName: "IX_telemetry_summaries_organization_id_environment_id_device_a~");
        }
    }
}
