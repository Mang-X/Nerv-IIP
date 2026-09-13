using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeSerialAllocation : Migration
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
                comment: "Server-allocated serialized unit identifier encoded in a plain or GS1 label; null only for legacy non-serialized rows.",
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
                comment: "Environment id copied from the owning print batch for scoped serial uniqueness.");

            migrationBuilder.AddColumn<string>(
                name: "organization_id",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Organization tenant id copied from the owning print batch for scoped serial uniqueness.");

            migrationBuilder.CreateTable(
                name: "label_serial_counters",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Label serial counter aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the serial allocation scope."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the serial allocation scope."),
                    current_value = table.Column<long>(type: "bigint", nullable: false, comment: "Highest monotonically allocated counter value in the scope.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_serial_counters", x => x.id);
                },
                comment: "Persistent serial allocation counters scoped by organization and environment.");

            migrationBuilder.Sql(
                """
                UPDATE barcode.label_print_items AS item
                SET organization_id = batch.organization_id,
                    environment_id = batch.environment_id
                FROM barcode.label_print_batches AS batch
                WHERE batch.id = item.label_print_batch_id;
                """);

            migrationBuilder.Sql(
                """
                DO $migration$
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
                        HAVING count(*) > 1
                    ) AS conflicts;

                    IF conflicting_serials IS NOT NULL THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'integrity_constraint_violation',
                            MESSAGE = 'AddBarcodeSerialAllocation aborted: barcode.label_print_items has duplicate serial_number values inside the same organization/environment. Resolve every item explicitly using docs/runbooks/database-release.md, then retry; the migration did not overwrite or renumber data. organization / environment / serial_number: item_id@label_print_batch_id:' || E'\n' || conflicting_serials;
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "environment_id",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Environment id copied from the owning print batch for scoped serial uniqueness.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Environment id copied from the owning print batch for scoped serial uniqueness.");

            migrationBuilder.AlterColumn<string>(
                name: "organization_id",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Organization tenant id copied from the owning print batch for scoped serial uniqueness.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Organization tenant id copied from the owning print batch for scoped serial uniqueness.");

            migrationBuilder.CreateIndex(
                name: "UX_label_print_items_serial_number",
                schema: "barcode",
                table: "label_print_items",
                columns: new[] { "organization_id", "environment_id", "serial_number" },
                unique: true,
                filter: "serial_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_label_serial_counters_scope",
                schema: "barcode",
                table: "label_serial_counters",
                columns: new[] { "organization_id", "environment_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "label_serial_counters",
                schema: "barcode");

            migrationBuilder.DropIndex(
                name: "UX_label_print_items_serial_number",
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
                oldComment: "Server-allocated serialized unit identifier encoded in a plain or GS1 label; null only for legacy non-serialized rows.");
        }
    }
}
