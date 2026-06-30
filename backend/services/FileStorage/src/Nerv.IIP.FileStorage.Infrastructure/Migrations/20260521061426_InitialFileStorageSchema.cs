using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.FileStorage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialFileStorageSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "filestorage");

            migrationBuilder.CreateTable(
                name: "stored_files",
                schema: "filestorage",
                columns: table => new
                {
                    file_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Stable file identifier returned by the public FileStorage API."),
                    organization_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Organization identifier that owns the file."),
                    environment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Environment identifier that scopes the file."),
                    owner_service = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that owns the file metadata."),
                    owner_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Owner resource type within the owning service."),
                    owner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Owner resource identifier within the owning service."),
                    file_purpose = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Purpose policy key used to validate and route file usage."),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Original file name supplied by the caller."),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Media type declared for the stored file."),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false, comment: "Stored object size in bytes."),
                    checksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Optional caller-provided checksum for integrity tracking."),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false, comment: "Internal object storage key; never exposed through public FileStorage responses."),
                    scan_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Malware or content scan status for the stored file."),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "File lifecycle status visible through metadata responses."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the file metadata was created."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the file became available.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_files", x => x.file_id);
                },
                comment: "FileStorage completed file metadata for internally stored objects.");

            migrationBuilder.CreateTable(
                name: "upload_sessions",
                schema: "filestorage",
                columns: table => new
                {
                    upload_session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Stable upload session identifier returned by the public FileStorage API."),
                    file_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "File identifier reserved for the upload session."),
                    organization_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Organization identifier that owns the upload session."),
                    environment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Environment identifier that scopes the upload session."),
                    owner_service = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that owns the eventual file metadata."),
                    owner_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Owner resource type within the owning service."),
                    owner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Owner resource identifier within the owning service."),
                    file_purpose = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Purpose policy key used to validate and route the upload."),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Original file name supplied by the caller."),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Media type declared for the upload."),
                    expected_size_bytes = table.Column<long>(type: "bigint", nullable: false, comment: "Expected object size in bytes supplied during session creation."),
                    checksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Optional caller-provided checksum for integrity tracking."),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false, comment: "Internal object storage key reserved for this upload session."),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Upload provider used for this session."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the upload session was created."),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the upload session expires."),
                    completed = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the upload session has been completed."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the upload session was completed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_sessions", x => x.upload_session_id);
                },
                comment: "FileStorage upload session metadata created before object bytes are completed.");

            migrationBuilder.CreateTable(
                name: "download_grants",
                schema: "filestorage",
                columns: table => new
                {
                    download_grant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Stable download grant identifier used to serve a short-lived download."),
                    file_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "File identifier the download grant authorizes."),
                    organization_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Organization identifier that owns the download grant."),
                    environment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Environment identifier that scopes the download grant."),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Download provider used for this grant."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the download grant was created."),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the download grant expires.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_grants", x => x.download_grant_id);
                    table.ForeignKey(
                        name: "FK_download_grants_stored_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "filestorage",
                        principalTable: "stored_files",
                        principalColumn: "file_id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "FileStorage short-lived download grant metadata for server-proxy downloads.");

            migrationBuilder.CreateIndex(
                name: "IX_download_grants_file_id",
                schema: "filestorage",
                table: "download_grants",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "IX_download_grants_organization_id_environment_id_file_id_expi~",
                schema: "filestorage",
                table: "download_grants",
                columns: new[] { "organization_id", "environment_id", "file_id", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_object_key",
                schema: "filestorage",
                table: "stored_files",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_organization_id_environment_id_owner_service_o~",
                schema: "filestorage",
                table: "stored_files",
                columns: new[] { "organization_id", "environment_id", "owner_service", "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_file_id",
                schema: "filestorage",
                table: "upload_sessions",
                column: "file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_object_key",
                schema: "filestorage",
                table: "upload_sessions",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_organization_id_environment_id_expires_at_u~",
                schema: "filestorage",
                table: "upload_sessions",
                columns: new[] { "organization_id", "environment_id", "expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "download_grants",
                schema: "filestorage");

            migrationBuilder.DropTable(
                name: "upload_sessions",
                schema: "filestorage");

            migrationBuilder.DropTable(
                name: "stored_files",
                schema: "filestorage");
        }
    }
}
