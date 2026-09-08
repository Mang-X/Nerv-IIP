using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RejectBlankLabelPrintBatchReplaySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_label_print_batches_replay_snapshot_complete",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.AddCheckConstraint(
                name: "ck_label_print_batches_replay_snapshot_complete",
                schema: "barcode",
                table: "label_print_batches",
                sql: "(template_file_id_snapshot IS NULL AND template_asset_sha256 IS NULL AND variable_schema_json_snapshot IS NULL AND barcode_type_snapshot IS NULL AND renderer_contract_version IS NULL) OR (template_file_id_snapshot IS NOT NULL AND trim(template_file_id_snapshot, ' \t\n\v\f\r\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000') <> '' AND template_asset_sha256 IS NOT NULL AND trim(template_asset_sha256, ' \t\n\v\f\r\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000') <> '' AND variable_schema_json_snapshot IS NOT NULL AND trim(variable_schema_json_snapshot, ' \t\n\v\f\r\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000') <> '' AND barcode_type_snapshot IS NOT NULL AND trim(barcode_type_snapshot, ' \t\n\v\f\r\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000') <> '' AND renderer_contract_version IS NOT NULL AND trim(renderer_contract_version, ' \t\n\v\f\r\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000') <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_label_print_batches_replay_snapshot_complete",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.AddCheckConstraint(
                name: "ck_label_print_batches_replay_snapshot_complete",
                schema: "barcode",
                table: "label_print_batches",
                sql: "(template_file_id_snapshot IS NULL AND template_asset_sha256 IS NULL AND variable_schema_json_snapshot IS NULL AND barcode_type_snapshot IS NULL AND renderer_contract_version IS NULL) OR (template_file_id_snapshot IS NOT NULL AND template_asset_sha256 IS NOT NULL AND variable_schema_json_snapshot IS NOT NULL AND barcode_type_snapshot IS NOT NULL AND renderer_contract_version IS NOT NULL)");
        }
    }
}
