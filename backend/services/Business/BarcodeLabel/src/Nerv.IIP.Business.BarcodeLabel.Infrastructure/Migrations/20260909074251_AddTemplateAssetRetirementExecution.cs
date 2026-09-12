using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAssetRetirementExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "client_window_seconds",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "bigint",
                nullable: true,
                comment: "Requested client replay horizon in seconds, frozen on first send.");

            migrationBuilder.AddColumn<Guid>(
                name: "execution_lease_id",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "uuid",
                nullable: true,
                comment: "Current durable execution lease identity.");

            migrationBuilder.AddColumn<long>(
                name: "executor_lease_seconds",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "bigint",
                nullable: true,
                comment: "Retirement lease duration in seconds, frozen on first send.");

            migrationBuilder.AddColumn<long>(
                name: "executor_max_backoff_seconds",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "bigint",
                nullable: true,
                comment: "Retirement retry backoff in seconds, frozen on first send.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_sent_at_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC first outbound attempt, frozen before signing.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC lease expiry or earliest retry time.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "quota_released_at_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC quota release time reported by FileStorage, absent for unknown outcomes.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recovery_until_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC permanent unknown boundary, seven days after first send.");

            migrationBuilder.AddColumn<long>(
                name: "replay_horizon_seconds",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "bigint",
                nullable: true,
                comment: "Frozen replay horizon in seconds returned by FileStorage.");

            migrationBuilder.AddColumn<long>(
                name: "replay_policy_version",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "bigint",
                nullable: true,
                comment: "Replay policy version frozen on first send.");

            migrationBuilder.CreateIndex(
                name: "IX_template_asset_retirement_decisions_status_next_attempt_at_~",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                columns: new[] { "status", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_template_asset_retirement_decisions_status_recovery_until_u~",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                columns: new[] { "status", "recovery_until_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_template_asset_retirement_decisions_status_next_attempt_at_~",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropIndex(
                name: "IX_template_asset_retirement_decisions_status_recovery_until_u~",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "client_window_seconds",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "execution_lease_id",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "executor_lease_seconds",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "executor_max_backoff_seconds",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "first_sent_at_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "next_attempt_at_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "quota_released_at_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "recovery_until_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "replay_horizon_seconds",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "replay_policy_version",
                schema: "barcode",
                table: "template_asset_retirement_decisions");
        }
    }
}
