using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.FileStorage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFileStorageScanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE filestorage.stored_files
                SET status = 'deleted',
                    deleted_at_utc = COALESCE(deleted_at_utc, CURRENT_TIMESTAMP),
                    physical_delete_after_utc = COALESCE(physical_delete_after_utc, CURRENT_TIMESTAMP),
                    deletion_reason = LEFT(
                        CASE
                            WHEN deletion_reason IS NULL OR deletion_reason = ''
                                THEN 'scan-removal:' || COALESCE(scan_status, 'unknown')
                            ELSE deletion_reason || ';scan-removal:' || COALESCE(scan_status, 'unknown')
                        END,
                        256)
                WHERE scan_status IS DISTINCT FROM 'clean';
                """);

            migrationBuilder.DropIndex(
                name: "IX_stored_files_scan_status_status",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "scan_detail",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "scan_status",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "scanned_at_utc",
                schema: "filestorage",
                table: "stored_files");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scan_detail",
                schema: "filestorage",
                table: "stored_files",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                comment: "Scanner result summary or degradation reason produced by FileStorage scanning.");

            migrationBuilder.AddColumn<string>(
                name: "scan_status",
                schema: "filestorage",
                table: "stored_files",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "clean",
                comment: "Malware or content scan status for the stored file.");

            migrationBuilder.Sql(
                "ALTER TABLE filestorage.stored_files ALTER COLUMN scan_status DROP DEFAULT;");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scanned_at_utc",
                schema: "filestorage",
                table: "stored_files",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp when malware or content scanning last completed.");

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_scan_status_status",
                schema: "filestorage",
                table: "stored_files",
                columns: new[] { "scan_status", "status" });
        }
    }
}
