using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.FileStorage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileStorageSecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at_utc",
                schema: "filestorage",
                table: "stored_files",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp when FileStorage soft-deleted the file metadata.");

            migrationBuilder.AddColumn<string>(
                name: "deletion_reason",
                schema: "filestorage",
                table: "stored_files",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "Reason FileStorage marked the file deleted.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "physical_delete_after_utc",
                schema: "filestorage",
                table: "stored_files",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp after which FileStorage may physically remove file metadata and bytes.");

            migrationBuilder.AddColumn<string>(
                name: "scan_detail",
                schema: "filestorage",
                table: "stored_files",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                comment: "Scanner result summary or degradation reason produced by FileStorage scanning.");

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

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_status_physical_delete_after_utc",
                schema: "filestorage",
                table: "stored_files",
                columns: new[] { "status", "physical_delete_after_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stored_files_scan_status_status",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropIndex(
                name: "IX_stored_files_status_physical_delete_after_utc",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "deletion_reason",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "physical_delete_after_utc",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "scan_detail",
                schema: "filestorage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "scanned_at_utc",
                schema: "filestorage",
                table: "stored_files");
        }
    }
}
