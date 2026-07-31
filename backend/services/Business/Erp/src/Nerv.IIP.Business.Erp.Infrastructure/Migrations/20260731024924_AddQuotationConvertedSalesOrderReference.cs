using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationConvertedSalesOrderReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "converted_at_utc",
                schema: "erp",
                table: "quotations",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time the quotation was converted to a sales order.");

            migrationBuilder.AddColumn<string>(
                name: "converted_sales_order_no",
                schema: "erp",
                table: "quotations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Sales order number this quotation has been converted to; null when not converted yet.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "converted_at_utc",
                schema: "erp",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "converted_sales_order_no",
                schema: "erp",
                table: "quotations");
        }
    }
}
