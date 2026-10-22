using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicInspectionQuantityContinuationFairness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_periodic_inspection_runtime_status_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_periodic_inspection_runtime_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.AddColumn<DateTime>(
                name: "quantity_continuation_next_attempt_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Persisted fair-scheduling time after which the pending quantity backlog may claim another bounded batch.");

            migrationBuilder.Sql(
                """
                UPDATE quality.periodic_inspection_runtime_contexts
                SET quantity_continuation_next_attempt_at_utc = quantity_generation_anchor_at_utc
                WHERE quantity_generation_anchor_at_utc IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_periodic_inspection_runtime_quantity_continuation_due",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                columns: new[] { "quantity_continuation_next_attempt_at_utc", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_periodic_inspection_runtime_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                sql: "(quantity_generation_anchor_at_utc IS NULL AND quantity_continuation_next_attempt_at_utc IS NULL) OR (quantity_generation_anchor_at_utc IS NOT NULL AND quantity_continuation_next_attempt_at_utc IS NOT NULL AND status IN ('active', 'closed') AND quantity_interval IS NOT NULL AND uom_code IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_periodic_inspection_runtime_quantity_continuation_due",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_periodic_inspection_runtime_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropColumn(
                name: "quantity_continuation_next_attempt_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.CreateIndex(
                name: "ix_periodic_inspection_runtime_status_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                columns: new[] { "status", "quantity_generation_anchor_at_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_periodic_inspection_runtime_quantity_continuation",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                sql: "quantity_generation_anchor_at_utc IS NULL OR (status = 'active' AND quantity_interval IS NOT NULL AND uom_code IS NOT NULL)");
        }
    }
}
