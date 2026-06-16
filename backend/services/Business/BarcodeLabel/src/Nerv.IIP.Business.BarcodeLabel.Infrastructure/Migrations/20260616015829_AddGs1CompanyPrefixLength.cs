using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGs1CompanyPrefixLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "gs1_company_prefix_length",
                schema: "barcode",
                table: "barcode_rules",
                type: "integer",
                nullable: true,
                comment: "Explicit GS1 company prefix length used to split SGTIN EPC URI values for GS1 barcode rules.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gs1_company_prefix_length",
                schema: "barcode",
                table: "barcode_rules");
        }
    }
}
