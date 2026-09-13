using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeSerialReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "serial_number",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "BarcodeLabel allocated serialized unit identifier encoded in this label.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "Serialized unit identifier encoded in the generated GS1 label.");

            migrationBuilder.AddColumn<string>(
                name: "environment_id",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Environment id owning the generated serial.");

            migrationBuilder.AddColumn<string>(
                name: "organization_id",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Organization tenant id owning the generated serial.");

            migrationBuilder.Sql(
                """
                UPDATE barcode.label_print_items AS item
                SET organization_id = batch.organization_id,
                    environment_id = batch.environment_id
                FROM barcode.label_print_batches AS batch
                WHERE batch.id = item.label_print_batch_id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "environment_id",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Environment id owning the generated serial.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Environment id owning the generated serial.");

            migrationBuilder.AlterColumn<string>(
                name: "organization_id",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Organization tenant id owning the generated serial.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Organization tenant id owning the generated serial.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "Truthful print batch lifecycle status: reserved, ready-to-print, sent-to-printer, delivery-unknown, printed or failed; concurrent lifecycle writes must match the observed status.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "Truthful print batch lifecycle status: pending, sent-to-printer, printed or failed.");

            migrationBuilder.AddColumn<string>(
                name: "production_report_id",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Nullable MES production report id associated by activation.");

            migrationBuilder.AddColumn<string>(
                name: "production_report_no",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Nullable MES production report number associated by activation.");

            migrationBuilder.Sql(
                """
                UPDATE barcode.label_print_batches
                SET status = 'ready-to-print'
                WHERE status = 'pending';
                """);

            migrationBuilder.CreateTable(
                name: "label_serial_counters",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Serial counter aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id owning the serial namespace."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id owning the serial namespace."),
                    barcode_rule_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Barcode rule owning the serial namespace."),
                    current_value = table.Column<long>(type: "bigint", nullable: false, comment: "Last value atomically reserved in this serial namespace.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_serial_counters", x => x.id);
                    table.CheckConstraint("ck_label_serial_counters_current_value_positive", "current_value > 0");
                },
                comment: "Persistent per-rule allocator state for generated unit serial numbers.");

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    conflicting_serials text;
                BEGIN
                    SELECT string_agg(
                               format('%s / %s / %s: %s',
                                      organization_id, environment_id, serial_number, item_refs),
                               E'\n' ORDER BY organization_id, environment_id, serial_number)
                    INTO conflicting_serials
                    FROM (
                        SELECT item.organization_id,
                               item.environment_id,
                               item.serial_number,
                               string_agg(
                                   format('%s@%s', item.id, item.label_print_batch_id),
                                   ', ' ORDER BY item.label_print_batch_id, item.id) AS item_refs
                        FROM barcode.label_print_items AS item
                        WHERE item.serial_number IS NOT NULL
                        GROUP BY item.organization_id, item.environment_id, item.serial_number
                        HAVING COUNT(*) > 1
                    ) AS conflicts;

                    IF conflicting_serials IS NOT NULL THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'integrity_constraint_violation',
                            MESSAGE = 'AddBarcodeSerialReservation aborted: barcode.label_print_items has duplicate serial_number values inside the same organization/environment. Resolve every item explicitly (see docs/runbooks/database-release.md §6.5) and re-run the migration. organization / environment / serial_number: item_id@label_print_batch_id:' || E'\n' || conflicting_serials;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_label_print_items_scope_serial_number",
                schema: "barcode",
                table: "label_print_items",
                columns: new[] { "organization_id", "environment_id", "serial_number" },
                unique: true,
                filter: "serial_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_label_serial_counters_organization_id_environment_id_barcod~",
                schema: "barcode",
                table: "label_serial_counters",
                columns: new[] { "organization_id", "environment_id", "barcode_rule_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "label_serial_counters",
                schema: "barcode");

            migrationBuilder.DropIndex(
                name: "UX_label_print_items_scope_serial_number",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "environment_id",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "organization_id",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "production_report_id",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "production_report_no",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.AlterColumn<string>(
                name: "serial_number",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Serialized unit identifier encoded in the generated GS1 label.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "BarcodeLabel allocated serialized unit identifier encoded in this label.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "Truthful print batch lifecycle status: pending, sent-to-printer, printed or failed.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "Truthful print batch lifecycle status: reserved, ready-to-print, sent-to-printer, delivery-unknown, printed or failed.");
        }
    }
}
