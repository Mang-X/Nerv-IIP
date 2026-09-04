using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpressCompositeSkuCodeForBackfillYield : Migration
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
                comment: "SKU snapshot frozen from the release facts; carries the same composite meaning as periodic_inspection_operations.sku_code - release event SKU for directly delivered facts, completion_sku_code when a backfilled reconstruction yielded to authoritative completion facts.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "SKU snapshot from the release event.");

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
                oldComment: "SKU snapshot from the work-order release event; null until release arrives.");
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
                comment: "SKU snapshot from the release event.",
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
                comment: "SKU snapshot from the work-order release event; null until release arrives.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "SKU snapshot, composite by source: the work-order release event SKU for directly delivered facts; for legacy work orders backfilled by the release-projection backfill it carries the reconstructed SKU, or - when the operation already had authoritative completion facts that disagreed - completion_sku_code. Null until release facts arrive.");
        }
    }
}
