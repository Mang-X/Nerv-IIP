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
                    serial_number_length = table.Column<int>(type: "integer", nullable: false, comment: "Fixed Base62 serial text width that defines an exact collision partition."),
                    current_value = table.Column<long>(type: "bigint", nullable: false, comment: "Highest monotonically allocated counter value in the collision partition.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_serial_counters", x => x.id);
                },
                comment: "Persistent serial allocation counters partitioned by organization, environment, and exact serial text width.");

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

            migrationBuilder.Sql(
                """
                DO $migration$
                DECLARE
                    exhausted_spaces text;
                BEGIN
                    WITH historical_serials AS (
                        SELECT item.organization_id,
                               item.environment_id,
                               item.serial_number,
                               char_length(item.serial_number) AS serial_number_length
                        FROM barcode.label_print_items AS item
                        WHERE item.serial_number IS NOT NULL
                          AND item.serial_number COLLATE "C" ~ '^[0-9A-Za-z]{2,20}$'
                    ),
                    decoded_serials AS (
                        SELECT historical.organization_id,
                               historical.environment_id,
                               historical.serial_number,
                               historical.serial_number_length,
                               sum(
                                   (strpos(
                                       '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz',
                                       substr(historical.serial_number, digit_position.value, 1)) - 1)::numeric
                                   * power(62::numeric, historical.serial_number_length - digit_position.value)) AS numeric_value
                        FROM historical_serials AS historical
                        CROSS JOIN LATERAL generate_series(1, historical.serial_number_length) AS digit_position(value)
                        GROUP BY historical.organization_id,
                                 historical.environment_id,
                                 historical.serial_number,
                                 historical.serial_number_length
                    ),
                    reachable_serials AS (
                        SELECT decoded.*
                        FROM decoded_serials AS decoded
                        WHERE decoded.numeric_value BETWEEN 1 AND 9223372036854775807::numeric
                    )
                    SELECT string_agg(
                               format('%s / %s / width %s / %s',
                                      organization_id,
                                      environment_id,
                                      serial_number_length,
                                      serial_number),
                               E'\n' ORDER BY organization_id, environment_id, serial_number_length)
                    INTO exhausted_spaces
                    FROM reachable_serials
                    WHERE numeric_value = CASE
                        WHEN serial_number_length <= 10
                            THEN power(62::numeric, serial_number_length) - 1
                        ELSE 9223372036854775807::numeric
                    END;

                    IF exhausted_spaces IS NOT NULL THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'integrity_constraint_violation',
                            MESSAGE = 'AddBarcodeSerialAllocation aborted: a historical serial_number already occupies the final value in a generated width partition. Resolve the affected scope explicitly using docs/runbooks/database-release.md, then retry; the migration did not overwrite or renumber data. organization / environment / width / serial_number:' || E'\n' || exhausted_spaces;
                    END IF;

                    INSERT INTO barcode.label_serial_counters (
                        id,
                        organization_id,
                        environment_id,
                        serial_number_length,
                        current_value)
                    WITH historical_serials AS (
                        SELECT item.organization_id,
                               item.environment_id,
                               item.serial_number,
                               char_length(item.serial_number) AS serial_number_length
                        FROM barcode.label_print_items AS item
                        WHERE item.serial_number IS NOT NULL
                          AND item.serial_number COLLATE "C" ~ '^[0-9A-Za-z]{2,20}$'
                    ),
                    decoded_serials AS (
                        SELECT historical.organization_id,
                               historical.environment_id,
                               historical.serial_number_length,
                               sum(
                                   (strpos(
                                       '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz',
                                       substr(historical.serial_number, digit_position.value, 1)) - 1)::numeric
                                   * power(62::numeric, historical.serial_number_length - digit_position.value)) AS numeric_value
                        FROM historical_serials AS historical
                        CROSS JOIN LATERAL generate_series(1, historical.serial_number_length) AS digit_position(value)
                        GROUP BY historical.organization_id,
                                 historical.environment_id,
                                 historical.serial_number,
                                 historical.serial_number_length
                    )
                    SELECT uuidv7(),
                           decoded.organization_id,
                           decoded.environment_id,
                           decoded.serial_number_length,
                           max(decoded.numeric_value)::bigint
                    FROM decoded_serials AS decoded
                    WHERE decoded.numeric_value BETWEEN 1 AND 9223372036854775807::numeric
                    GROUP BY decoded.organization_id,
                             decoded.environment_id,
                             decoded.serial_number_length;
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
                columns: new[] { "organization_id", "environment_id", "serial_number_length" },
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
