using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesFinishedGoodsExpiryCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "date",
                nullable: true,
                comment: "Optional finished-goods batch expiry date carried to Inventory FEFO.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "production_date",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "date",
                nullable: true,
                comment: "Optional finished-goods batch production date carried to Inventory.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "production_date",
                schema: "mes",
                table: "finished_goods_receipt_requests");
        }
    }
}
