using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.FileStorage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredFilesTenantListIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_stored_files_organization_id_environment_id_completed_at_utc",
                schema: "filestorage",
                table: "stored_files",
                columns: new[] { "organization_id", "environment_id", "completed_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stored_files_organization_id_environment_id_completed_at_utc",
                schema: "filestorage",
                table: "stored_files");
        }
    }
}
