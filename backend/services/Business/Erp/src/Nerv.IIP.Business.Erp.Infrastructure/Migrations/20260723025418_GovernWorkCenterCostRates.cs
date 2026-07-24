using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GovernWorkCenterCostRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var migrationChangedAtUtc = new DateTimeOffset(2026, 7, 23, 2, 54, 18, TimeSpan.Zero);

            migrationBuilder.DropIndex(
                name: "IX_work_center_cost_rates_organization_id_environment_id_work_~",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.AlterTable(
                name: "work_center_cost_rates",
                schema: "erp",
                comment: "ERP append-only, effective-dated labor hourly-rate revision history by work center.",
                oldComment: "ERP phase-one actual labor hourly rates by work center.");

            migrationBuilder.AlterColumn<decimal>(
                name: "hourly_rate",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Positive actual labor rate per hour.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Actual labor rate per hour in local currency.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "changed_at_utc",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: migrationChangedAtUtc,
                comment: "UTC audit instant at which this revision was configured.");

            migrationBuilder.AddColumn<string>(
                name: "changed_by",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "system:migration",
                comment: "Canonical authenticated actor that configured this revision.");

            migrationBuilder.AddColumn<string>(
                name: "currency_code",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "CNY",
                comment: "Normalized ISO-style three-letter uppercase currency code.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "effective_from_utc",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: DateTimeOffset.UnixEpoch,
                comment: "Inclusive UTC instant from which this revision may apply.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "effective_to_utc",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Optional exclusive UTC instant after which this revision no longer applies.");

            migrationBuilder.AddColumn<string>(
                name: "reason",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "legacy cost-rate migration",
                comment: "Non-empty business reason for this immutable revision.");

            migrationBuilder.AddColumn<int>(
                name: "revision",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Monotonically increasing revision within organization, environment, and work center.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "changed_at_utc",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "timestamp with time zone",
                nullable: false,
                comment: "UTC audit instant at which this revision was configured.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValue: migrationChangedAtUtc,
                oldComment: "UTC audit instant at which this revision was configured.");

            migrationBuilder.AlterColumn<string>(
                name: "changed_by",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "Canonical authenticated actor that configured this revision.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldDefaultValue: "system:migration",
                oldComment: "Canonical authenticated actor that configured this revision.");

            migrationBuilder.AlterColumn<string>(
                name: "currency_code",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                comment: "Normalized ISO-style three-letter uppercase currency code.",
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3,
                oldDefaultValue: "CNY",
                oldComment: "Normalized ISO-style three-letter uppercase currency code.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "effective_from_utc",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "timestamp with time zone",
                nullable: false,
                comment: "Inclusive UTC instant from which this revision may apply.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValue: DateTimeOffset.UnixEpoch,
                oldComment: "Inclusive UTC instant from which this revision may apply.");

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "Non-empty business reason for this immutable revision.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldDefaultValue: "legacy cost-rate migration",
                oldComment: "Non-empty business reason for this immutable revision.");

            migrationBuilder.AlterColumn<int>(
                name: "revision",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "integer",
                nullable: false,
                comment: "Monotonically increasing revision within organization, environment, and work center.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1,
                oldComment: "Monotonically increasing revision within organization, environment, and work center.");

            migrationBuilder.CreateIndex(
                name: "ix_work_center_cost_rates_effective_lookup",
                schema: "erp",
                table: "work_center_cost_rates",
                columns: new[] { "organization_id", "environment_id", "work_center_id", "effective_from_utc", "effective_to_utc", "revision" });

            migrationBuilder.CreateIndex(
                name: "ux_work_center_cost_rates_scope_revision",
                schema: "erp",
                table: "work_center_cost_rates",
                columns: new[] { "organization_id", "environment_id", "work_center_id", "revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Downgrade intentionally discards newer audit history because the legacy schema permits one row per scope.
                DELETE FROM erp.work_center_cost_rates AS candidate
                USING erp.work_center_cost_rates AS winner
                WHERE candidate.organization_id = winner.organization_id
                  AND candidate.environment_id = winner.environment_id
                  AND candidate.work_center_id = winner.work_center_id
                  AND candidate.revision < winner.revision;
                """);

            migrationBuilder.DropIndex(
                name: "ix_work_center_cost_rates_effective_lookup",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.DropIndex(
                name: "ux_work_center_cost_rates_scope_revision",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.DropColumn(
                name: "changed_at_utc",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.DropColumn(
                name: "changed_by",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.DropColumn(
                name: "currency_code",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.DropColumn(
                name: "effective_from_utc",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.DropColumn(
                name: "effective_to_utc",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.DropColumn(
                name: "reason",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "erp",
                table: "work_center_cost_rates");

            migrationBuilder.AlterTable(
                name: "work_center_cost_rates",
                schema: "erp",
                comment: "ERP phase-one actual labor hourly rates by work center.",
                oldComment: "ERP append-only, effective-dated labor hourly-rate revision history by work center.");

            migrationBuilder.AlterColumn<decimal>(
                name: "hourly_rate",
                schema: "erp",
                table: "work_center_cost_rates",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Actual labor rate per hour in local currency.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Positive actual labor rate per hour.");

            migrationBuilder.CreateIndex(
                name: "IX_work_center_cost_rates_organization_id_environment_id_work_~",
                schema: "erp",
                table: "work_center_cost_rates",
                columns: new[] { "organization_id", "environment_id", "work_center_id" },
                unique: true);
        }
    }
}
