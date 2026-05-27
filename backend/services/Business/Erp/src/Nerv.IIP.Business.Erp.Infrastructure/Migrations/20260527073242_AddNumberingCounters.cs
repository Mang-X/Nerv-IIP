using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNumberingCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "numbering_counters",
                schema: "erp",
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
                schema: "erp",
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
                name: "ux_numbering_counters_scope",
                schema: "erp",
                table: "numbering_counters",
                columns: new[] { "organization_id", "environment_id", "document_type", "site_code", "date_segment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_numbering_idempotency_keys_scope",
                schema: "erp",
                table: "numbering_idempotency_keys",
                columns: new[] { "organization_id", "environment_id", "document_type", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "numbering_counters",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "numbering_idempotency_keys",
                schema: "erp");
        }
    }
}
