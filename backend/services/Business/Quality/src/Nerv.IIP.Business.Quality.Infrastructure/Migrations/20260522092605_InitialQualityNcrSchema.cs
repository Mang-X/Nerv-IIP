using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialQualityNcrSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "quality");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "quality",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_locks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "cap_published_messages",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_published_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cap_received_messages",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_received_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "nonconformance_reports",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "NCR aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the NCR."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the NCR was opened."),
                    ncr_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Human-readable automatically generated NCR code."),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "NCR source type: receiving, in-process, final or customer-return."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "External source document id such as inspection plan, report or return id."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "SKU code copied as a Quality reference."),
                    defect_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quantity found defective."),
                    defect_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Defect reason code or normalized reason."),
                    batch_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional batch number reference."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number reference."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "NCR lifecycle status."),
                    disposition_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Chosen disposition type."),
                    disposition_approval_chain_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Approval chain id for disposition approval."),
                    rework_work_order_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "MES rework work order id produced by downstream service."),
                    scrap_movement_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Inventory scrap movement id produced by downstream service."),
                    return_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "ERP supplier return document id produced by downstream service."),
                    attachment_file_ids = table.Column<List<string>>(type: "text[]", nullable: false, comment: "File Storage attachment ids."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the NCR was opened."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the NCR was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nonconformance_reports", x => x.id);
                },
                comment: "Quality nonconformance reports and disposition closure facts.");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "quality",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "quality",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "quality",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "quality",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_nonconformance_reports_organization_id_environment_id_ncr_c~",
                schema: "quality",
                table: "nonconformance_reports",
                columns: new[] { "organization_id", "environment_id", "ncr_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_nonconformance_reports_organization_id_environment_id_sourc~",
                schema: "quality",
                table: "nonconformance_reports",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_nonconformance_reports_organization_id_environment_id_status",
                schema: "quality",
                table: "nonconformance_reports",
                columns: new[] { "organization_id", "environment_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "nonconformance_reports",
                schema: "quality");
        }
    }
}
