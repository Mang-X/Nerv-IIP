using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveErpSourceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "opportunity_id",
                schema: "erp",
                table: "quotations",
                type: "uuid",
                nullable: true,
                comment: "Source sales opportunity.");

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_quotation_id",
                schema: "erp",
                table: "purchase_orders",
                type: "uuid",
                nullable: true,
                comment: "Selected supplier quotation for requisition conversion.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "opportunity_id",
                schema: "erp",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "supplier_quotation_id",
                schema: "erp",
                table: "purchase_orders");
        }
    }
}
