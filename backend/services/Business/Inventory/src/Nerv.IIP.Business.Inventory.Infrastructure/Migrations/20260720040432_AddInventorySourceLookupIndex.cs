using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventorySourceLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_organization_id_environment_id_source_serv~1",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "organization_id", "environment_id", "source_service", "source_document_id", "source_document_line_id", "posted_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_movements_organization_id_environment_id_source_serv~1",
                schema: "inventory",
                table: "stock_movements");
        }
    }
}
