using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseRequisitionConversionToPurchaseOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "converted_at_utc",
                schema: "erp",
                table: "purchase_requisitions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when this requisition was converted to a purchase order.");

            migrationBuilder.AddColumn<string>(
                name: "converted_purchase_order_no",
                schema: "erp",
                table: "purchase_requisitions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Purchase order number generated from this requisition.");

            migrationBuilder.CreateTable(
                name: "purchase_order_line_sources",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "Purchase order line source row id.")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_requisition_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source purchase requisition number."),
                    purchase_requisition_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source purchase requisition line number."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quantity pegged from the source requisition line."),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning purchase order line id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_line_sources", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_line_sources_purchase_order_lines_purchase_o~",
                        column: x => x.purchase_order_line_id,
                        principalSchema: "erp",
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP purchase order line source purchase requisition references.");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_line_sources_purchase_order_line_id_purchase~",
                schema: "erp",
                table: "purchase_order_line_sources",
                columns: new[] { "purchase_order_line_id", "purchase_requisition_no", "purchase_requisition_line_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_line_sources",
                schema: "erp");

            migrationBuilder.DropColumn(
                name: "converted_at_utc",
                schema: "erp",
                table: "purchase_requisitions");

            migrationBuilder.DropColumn(
                name: "converted_purchase_order_no",
                schema: "erp",
                table: "purchase_requisitions");
        }
    }
}
