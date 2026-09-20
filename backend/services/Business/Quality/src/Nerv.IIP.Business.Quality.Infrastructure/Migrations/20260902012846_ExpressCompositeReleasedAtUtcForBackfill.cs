using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpressCompositeReleasedAtUtcForBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "released_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "timestamp with time zone",
                nullable: false,
                comment: "UTC work-order release time frozen from the release snapshot; carries the same composite meaning as periodic_inspection_operations.released_at_utc - event time for directly delivered facts, reconstructed lower bound for backfilled legacy work orders.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "UTC work-order release time.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "released_at_utc",
                schema: "quality",
                table: "periodic_inspection_operations",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC work-order release time, composite by source: the MES release event time for directly delivered facts, or a reconstructed lower bound (earliest operation creation or earliest production report of the work order) for legacy work orders backfilled by the release-projection backfill. Null while source facts are staged out of order.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "UTC time when MES released the work order; null while source facts are staged out of order.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "released_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "timestamp with time zone",
                nullable: false,
                comment: "UTC work-order release time.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "UTC work-order release time frozen from the release snapshot; carries the same composite meaning as periodic_inspection_operations.released_at_utc - event time for directly delivered facts, reconstructed lower bound for backfilled legacy work orders.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "released_at_utc",
                schema: "quality",
                table: "periodic_inspection_operations",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when MES released the work order; null while source facts are staged out of order.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "UTC work-order release time, composite by source: the MES release event time for directly delivered facts, or a reconstructed lower bound (earliest operation creation or earliest production report of the work order) for legacy work orders backfilled by the release-projection backfill. Null while source facts are staged out of order.");
        }
    }
}
