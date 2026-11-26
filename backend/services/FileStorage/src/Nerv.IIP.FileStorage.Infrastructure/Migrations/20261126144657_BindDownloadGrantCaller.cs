using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.FileStorage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BindDownloadGrantCaller : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "caller",
                schema: "filestorage",
                table: "download_grants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                comment: "Authenticated caller that may redeem the download grant.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "caller",
                schema: "filestorage",
                table: "download_grants");
        }
    }
}
