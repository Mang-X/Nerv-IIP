using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpressSingleSourcedSkuCodeAfterBackfillShrink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "sku_code",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "SKU snapshot frozen from the release facts; single-sourced like periodic_inspection_operations.sku_code - always the work-order release SKU, delivered directly or reconstructed by the backfill, and never the staged completion_sku_code.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "SKU snapshot frozen from the release facts; carries the same composite meaning as periodic_inspection_operations.sku_code - release event SKU for directly delivered facts, completion_sku_code when a backfilled reconstruction yielded to authoritative completion facts.");

            migrationBuilder.AlterColumn<string>(
                name: "sku_code",
                schema: "quality",
                table: "periodic_inspection_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "SKU snapshot from the work-order release facts, single-sourced: the release event SKU for directly delivered facts, and for legacy work orders the same work-order SKU reconstructed by the release-projection backfill. An operation whose staged completion_sku_code disagrees is rejected per operation instead of yielding to it. Null until release facts arrive.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "SKU snapshot, composite by source: the work-order release event SKU for directly delivered facts; for legacy work orders backfilled by the release-projection backfill it carries the reconstructed SKU, or - when the operation already had authoritative completion facts that disagreed - completion_sku_code. Null until release facts arrive.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "sku_code",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "SKU snapshot frozen from the release facts; carries the same composite meaning as periodic_inspection_operations.sku_code - release event SKU for directly delivered facts, completion_sku_code when a backfilled reconstruction yielded to authoritative completion facts.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "SKU snapshot frozen from the release facts; single-sourced like periodic_inspection_operations.sku_code - always the work-order release SKU, delivered directly or reconstructed by the backfill, and never the staged completion_sku_code.");

            migrationBuilder.AlterColumn<string>(
                name: "sku_code",
                schema: "quality",
                table: "periodic_inspection_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "SKU snapshot, composite by source: the work-order release event SKU for directly delivered facts; for legacy work orders backfilled by the release-projection backfill it carries the reconstructed SKU, or - when the operation already had authoritative completion facts that disagreed - completion_sku_code. Null until release facts arrive.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "SKU snapshot from the work-order release facts, single-sourced: the release event SKU for directly delivered facts, and for legacy work orders the same work-order SKU reconstructed by the release-projection backfill. An operation whose staged completion_sku_code disagrees is rejected per operation instead of yielding to it. Null until release facts arrive.");
        }
    }
}
