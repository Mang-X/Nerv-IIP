using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEngineeringDocumentItemCodeAndReadEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "item_code",
                schema: "product_engineering",
                table: "engineering_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional engineering item code this document revision describes.");

            migrationBuilder.CreateIndex(
                name: "IX_engineering_documents_organization_id_environment_id_item_c~",
                schema: "product_engineering",
                table: "engineering_documents",
                columns: new[] { "organization_id", "environment_id", "item_code", "document_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_engineering_documents_organization_id_environment_id_item_c~",
                schema: "product_engineering",
                table: "engineering_documents");

            migrationBuilder.DropColumn(
                name: "item_code",
                schema: "product_engineering",
                table: "engineering_documents");
        }
    }
}
