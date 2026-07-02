using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessPartnerCreditLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "credit_currency_code",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                comment: "ISO currency code for the customer credit limit.");

            migrationBuilder.AddColumn<decimal>(
                name: "credit_limit",
                schema: "business_masterdata",
                table: "business_partners",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                comment: "Optional customer credit limit used by ERP sales credit checks.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "credit_currency_code",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "credit_limit",
                schema: "business_masterdata",
                table: "business_partners");
        }
    }
}
