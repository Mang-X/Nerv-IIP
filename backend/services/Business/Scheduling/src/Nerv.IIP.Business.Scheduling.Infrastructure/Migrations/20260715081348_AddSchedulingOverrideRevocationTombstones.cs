using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingOverrideRevocationTombstones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "schedule_operation_overrides",
                schema: "scheduling",
                comment: "Operation override projections, including active locks and inactive MES revocation tombstones.",
                oldComment: "Current fixed operation assignments created by manual Scheduling adjustments or MES dispatch.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cleared_at_utc",
                schema: "scheduling",
                table: "schedule_operation_overrides",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Optional MES timestamp at which the manual dispatch was cleared.");

            migrationBuilder.AddColumn<string>(
                name: "cleared_reason_code",
                schema: "scheduling",
                table: "schedule_operation_overrides",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Optional MES reason code that made this projection an inactive tombstone.");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "scheduling",
                table: "schedule_operation_overrides",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                comment: "Whether this override currently contributes an active scheduling lock.");

            migrationBuilder.AddColumn<long>(
                name: "source_revision",
                schema: "scheduling",
                table: "schedule_operation_overrides",
                type: "bigint",
                nullable: true,
                comment: "Optional positive MES manual-dispatch lifecycle revision used as the ordering watermark.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cleared_at_utc",
                schema: "scheduling",
                table: "schedule_operation_overrides");

            migrationBuilder.DropColumn(
                name: "cleared_reason_code",
                schema: "scheduling",
                table: "schedule_operation_overrides");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "scheduling",
                table: "schedule_operation_overrides");

            migrationBuilder.DropColumn(
                name: "source_revision",
                schema: "scheduling",
                table: "schedule_operation_overrides");

            migrationBuilder.AlterTable(
                name: "schedule_operation_overrides",
                schema: "scheduling",
                comment: "Current fixed operation assignments created by manual Scheduling adjustments or MES dispatch.",
                oldComment: "Operation override projections, including active locks and inactive MES revocation tombstones.");
        }
    }
}
