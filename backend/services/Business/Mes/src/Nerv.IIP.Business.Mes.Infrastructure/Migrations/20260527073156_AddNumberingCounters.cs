using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNumberingCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "downtime_event_no",
                schema: "mes",
                table: "work_center_unavailabilities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "MES business downtime event number allocated by the service numbering counter.");

            migrationBuilder.AddColumn<string>(
                name: "report_no",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "MES business production report number allocated by the service numbering counter.");

            migrationBuilder.AddColumn<string>(
                name: "request_no",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "MES business finished goods receipt request number allocated by the service numbering counter.");

            migrationBuilder.Sql("""
                UPDATE mes.work_center_unavailabilities
                SET downtime_event_no = 'DOWNTIME-' || replace(id::text, '-', '')
                WHERE downtime_event_no = '';

                UPDATE mes.production_reports
                SET report_no = 'PRPT-' || replace(id::text, '-', '')
                WHERE report_no = '';

                UPDATE mes.finished_goods_receipt_requests
                SET request_no = 'FGR-' || replace(id::text, '-', '')
                WHERE request_no = '';

                ALTER TABLE mes.work_center_unavailabilities ALTER COLUMN downtime_event_no DROP DEFAULT;
                ALTER TABLE mes.production_reports ALTER COLUMN report_no DROP DEFAULT;
                ALTER TABLE mes.finished_goods_receipt_requests ALTER COLUMN request_no DROP DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "numbering_counters",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false, comment: "Numbering counter surrogate identifier.")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization scope for the numbering counter."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment scope for the numbering counter."),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business document type governed by this counter."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Optional site or plant scope; empty string means global within organization and environment."),
                    date_segment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Date segment used by the numbering rule, formatted as yyyyMMdd for the current baseline."),
                    prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Document number prefix emitted before the date segment."),
                    current_value = table.Column<long>(type: "bigint", nullable: false, comment: "Last allocated sequence value within the counter scope."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Optimistic concurrency token incremented whenever the counter advances.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_numbering_counters", x => x.Id);
                },
                comment: "Service-local numbering counters scoped by organization, environment, document type, optional site and date segment.");

            migrationBuilder.CreateTable(
                name: "numbering_idempotency_keys",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false, comment: "Numbering idempotency record surrogate identifier.")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization scope for the idempotency key."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment scope for the idempotency key."),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business document type governed by the idempotency key."),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Client supplied stable idempotency key for ordinary create requests."),
                    number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Allocated business document number returned for this idempotency key."),
                    payload_fingerprint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Canonical request payload fingerprint used to reject key reuse with different create data."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the idempotency key was first recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_numbering_idempotency_keys", x => x.Id);
                },
                comment: "Service-local idempotency records that bind create request keys to allocated document numbers.");

            migrationBuilder.CreateIndex(
                name: "ux_wc_unavailability_scope_downtime_no",
                schema: "mes",
                table: "work_center_unavailabilities",
                columns: new[] { "organization_id", "environment_id", "downtime_event_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_production_reports_scope_report_no",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "report_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_receipt_requests_scope_request_no",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                columns: new[] { "organization_id", "environment_id", "request_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_numbering_counters_scope",
                schema: "mes",
                table: "numbering_counters",
                columns: new[] { "organization_id", "environment_id", "document_type", "site_code", "date_segment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_numbering_idempotency_keys_scope",
                schema: "mes",
                table: "numbering_idempotency_keys",
                columns: new[] { "organization_id", "environment_id", "document_type", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "numbering_counters",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "numbering_idempotency_keys",
                schema: "mes");

            migrationBuilder.DropIndex(
                name: "ux_wc_unavailability_scope_downtime_no",
                schema: "mes",
                table: "work_center_unavailabilities");

            migrationBuilder.DropIndex(
                name: "ux_production_reports_scope_report_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropIndex(
                name: "ux_receipt_requests_scope_request_no",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "downtime_event_no",
                schema: "mes",
                table: "work_center_unavailabilities");

            migrationBuilder.DropColumn(
                name: "report_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "request_no",
                schema: "mes",
                table: "finished_goods_receipt_requests");
        }
    }
}
