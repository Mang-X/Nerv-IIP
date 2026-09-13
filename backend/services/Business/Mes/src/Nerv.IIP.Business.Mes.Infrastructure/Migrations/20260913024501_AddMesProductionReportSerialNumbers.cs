using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesProductionReportSerialNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "production_report_serial_numbers",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Production report serial fact id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant scope."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment scope."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Forward MES production report number owning the serial assignment."),
                    sequence_no = table.Column<int>(type: "integer", nullable: false, comment: "One-based BarcodeLabel allocation order within the production report."),
                    serial_number = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Trimmed unit serial number compared with ordinal case-sensitive semantics.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_report_serial_numbers", x => x.id);
                    table.CheckConstraint("ck_production_report_serial_numbers_sequence_positive", "sequence_no > 0");
                    table.ForeignKey(
                        name: "fk_production_report_serial_numbers_reports",
                        columns: x => new { x.organization_id, x.environment_id, x.report_no },
                        principalSchema: "mes",
                        principalTable: "production_reports",
                        principalColumns: new[] { "organization_id", "environment_id", "report_no" },
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Immutable MES unit serial facts assigned to forward production reports.");

            // 旧单值只属于正向报工。首尾空白归一化后，同 org/env 内的重复值无法确定哪笔报工
            // 应拥有序列号，因此整条 migration fail-closed，不改号、不丢行，也不让冲销再占一次号。
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    duplicate_rows text;
                BEGIN
                    -- 与 .NET string.Trim() 对齐：移除 Unicode White_Space 集合中的首尾字符，
                    -- 不让 tab、NBSP 等历史值绕过空值过滤或 scoped 唯一预检。
                    WITH normalized_reports AS (
                        SELECT report.organization_id,
                               report.environment_id,
                               report.report_no,
                               btrim(
                                   report.serial_no,
                                   U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000') COLLATE "C" AS serial_number
                        FROM mes.production_reports AS report
                        WHERE report.reversed_report_no IS NULL
                    )
                    SELECT string_agg(
                               format('%s / %s / %s / %s',
                                      duplicate.organization_id,
                                      duplicate.environment_id,
                                      duplicate.serial_number,
                                      duplicate.report_nos),
                               E'\n' ORDER BY
                                   duplicate.organization_id COLLATE "C",
                                   duplicate.environment_id COLLATE "C",
                                   duplicate.serial_number COLLATE "C")
                      INTO duplicate_rows
                    FROM (
                        SELECT report.organization_id COLLATE "C" AS organization_id,
                               report.environment_id COLLATE "C" AS environment_id,
                               report.serial_number,
                               string_agg(report.report_no, ', ' ORDER BY report.report_no COLLATE "C") AS report_nos
                        FROM normalized_reports AS report
                        WHERE NULLIF(report.serial_number, '') IS NOT NULL
                        GROUP BY report.organization_id COLLATE "C",
                                 report.environment_id COLLATE "C",
                                 report.serial_number
                        HAVING count(*) > 1
                    ) AS duplicate;

                    IF duplicate_rows IS NOT NULL THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'integrity_constraint_violation',
                            MESSAGE = 'AddMesProductionReportSerialNumbers aborted: forward production reports share a trimmed serial number within the same organization and environment. Resolve each conflict explicitly (see docs/runbooks/database-release.md §6.5) and re-run the migration. OrganizationId / EnvironmentId / SerialNumber / ReportNos:' || E'\n' || duplicate_rows;
                    END IF;
                END
                $$;

                WITH normalized_reports AS (
                    SELECT report.id,
                           report.organization_id,
                           report.environment_id,
                           report.report_no,
                           btrim(
                               report.serial_no,
                               U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000') AS serial_number
                    FROM mes.production_reports AS report
                    WHERE report.reversed_report_no IS NULL
                )
                INSERT INTO mes.production_report_serial_numbers
                    (id, organization_id, environment_id, report_no, sequence_no, serial_number)
                SELECT report.id,
                       report.organization_id,
                       report.environment_id,
                       report.report_no,
                       1,
                       report.serial_number
                FROM normalized_reports AS report
                WHERE NULLIF(report.serial_number, '') IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_production_report_serial_numbers_scope_report_sequence",
                schema: "mes",
                table: "production_report_serial_numbers",
                columns: new[] { "organization_id", "environment_id", "report_no", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_production_report_serial_numbers_scope_serial",
                schema: "mes",
                table: "production_report_serial_numbers",
                columns: new[] { "organization_id", "environment_id", "serial_number" },
                unique: true)
                .Annotation("Relational:Collation", new[] { "C", "C", "C" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "production_report_serial_numbers",
                schema: "mes");
        }
    }
}
