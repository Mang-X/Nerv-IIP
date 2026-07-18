using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorManifestExactOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "manifest_observed_at_utc",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests",
                type: "timestamp with time zone",
                nullable: false,
                comment: "UTC source observation time displayed for the accepted manifest revision.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "Source observation time ordering accepted manifest revisions.");

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests",
                type: "bigint",
                nullable: true,
                comment: "Application-managed optimistic concurrency version incremented by accepted root mutations.");

            migrationBuilder.AddColumn<long>(
                name: "manifest_observed_at_utc_ticks",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests",
                type: "bigint",
                nullable: true,
                comment: "Exact .NET UTC ticks ordering accepted manifest revisions without timestamptz precision loss.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "activation_observed_at_utc",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                type: "timestamp with time zone",
                nullable: false,
                comment: "UTC source observation time displayed for the latest activation update.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "Source observation time ordering activation updates.");

            migrationBuilder.AddColumn<long>(
                name: "activation_observed_at_utc_ticks",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                type: "bigint",
                nullable: true,
                comment: "Exact .NET UTC ticks ordering activation updates without timestamptz precision loss.");

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                type: "bigint",
                nullable: true,
                comment: "Application-managed optimistic concurrency version incremented by binding mutations.");

            migrationBuilder.Sql("""
                UPDATE industrial_telemetry.connector_tag_manifests
                SET manifest_observed_at_utc_ticks = (
                        621355968000000000 + ROUND(EXTRACT(EPOCH FROM manifest_observed_at_utc) * 10000000)
                    )::bigint,
                    concurrency_version = 1;

                UPDATE industrial_telemetry.connector_tag_bindings
                SET activation_observed_at_utc_ticks = (
                        621355968000000000 + ROUND(EXTRACT(EPOCH FROM activation_observed_at_utc) * 10000000)
                    )::bigint,
                    concurrency_version = 1;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "manifest_observed_at_utc_ticks",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests",
                type: "bigint",
                nullable: false,
                comment: "Exact .NET UTC ticks ordering accepted manifest revisions without timestamptz precision loss.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "Exact .NET UTC ticks ordering accepted manifest revisions without timestamptz precision loss.");

            migrationBuilder.AlterColumn<long>(
                name: "concurrency_version",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests",
                type: "bigint",
                nullable: false,
                comment: "Application-managed optimistic concurrency version incremented by accepted root mutations.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "Application-managed optimistic concurrency version incremented by accepted root mutations.");

            migrationBuilder.AlterColumn<long>(
                name: "activation_observed_at_utc_ticks",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                type: "bigint",
                nullable: false,
                comment: "Exact .NET UTC ticks ordering activation updates without timestamptz precision loss.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "Exact .NET UTC ticks ordering activation updates without timestamptz precision loss.");

            migrationBuilder.AlterColumn<long>(
                name: "concurrency_version",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                type: "bigint",
                nullable: false,
                comment: "Application-managed optimistic concurrency version incremented by binding mutations.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "Application-managed optimistic concurrency version incremented by binding mutations.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "concurrency_version",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests");

            migrationBuilder.DropColumn(
                name: "manifest_observed_at_utc_ticks",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests");

            migrationBuilder.DropColumn(
                name: "activation_observed_at_utc_ticks",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "manifest_observed_at_utc",
                schema: "industrial_telemetry",
                table: "connector_tag_manifests",
                type: "timestamp with time zone",
                nullable: false,
                comment: "Source observation time ordering accepted manifest revisions.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "UTC source observation time displayed for the accepted manifest revision.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "activation_observed_at_utc",
                schema: "industrial_telemetry",
                table: "connector_tag_bindings",
                type: "timestamp with time zone",
                nullable: false,
                comment: "Source observation time ordering activation updates.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "UTC source observation time displayed for the latest activation update.");
        }
    }
}
