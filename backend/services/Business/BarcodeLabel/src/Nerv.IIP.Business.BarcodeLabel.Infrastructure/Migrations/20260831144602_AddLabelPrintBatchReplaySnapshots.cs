using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelPrintBatchReplaySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "barcode_type_snapshot",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                comment: "Nullable barcode type snapshot used by the renderer; null only for legacy rows.");

            migrationBuilder.AddColumn<string>(
                name: "renderer_contract_version",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                comment: "Nullable deterministic renderer contract version; null only for legacy rows.");

            migrationBuilder.AddColumn<string>(
                name: "template_asset_sha256",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(71)",
                maxLength: 71,
                nullable: true,
                comment: "Nullable canonical sha256-prefixed template asset snapshot; null only for legacy rows and never synthesized.");

            migrationBuilder.AddColumn<string>(
                name: "template_file_id_snapshot",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Nullable FileStorage template file id snapshot; null only for legacy rows created before replay snapshots.");

            migrationBuilder.AddColumn<string>(
                name: "variable_schema_json_snapshot",
                schema: "barcode",
                table: "label_print_batches",
                type: "text",
                nullable: true,
                comment: "Nullable variable schema JSON snapshot produced by BarcodeLabel for deterministic replay; null legacy rows are not replayable.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_label_print_batches_replay_snapshot_complete",
                schema: "barcode",
                table: "label_print_batches",
                sql: "(template_file_id_snapshot IS NULL AND template_asset_sha256 IS NULL AND variable_schema_json_snapshot IS NULL AND barcode_type_snapshot IS NULL AND renderer_contract_version IS NULL) OR (template_file_id_snapshot IS NOT NULL AND template_asset_sha256 IS NOT NULL AND variable_schema_json_snapshot IS NOT NULL AND barcode_type_snapshot IS NOT NULL AND renderer_contract_version IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_label_print_batches_replay_snapshot_complete",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "barcode_type_snapshot",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "renderer_contract_version",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "template_asset_sha256",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "template_file_id_snapshot",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "variable_schema_json_snapshot",
                schema: "barcode",
                table: "label_print_batches");
        }
    }
}
