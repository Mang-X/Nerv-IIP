using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CarryWmsReceiptBatchOnInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "wms_receipt_batch_id",
                schema: "quality",
                table: "inspection_tasks",
                type: "uuid",
                nullable: true,
                comment: "Original WMS acceptance batch for ERP receiving inspections.");

            migrationBuilder.AddColumn<Guid>(
                name: "wms_receipt_batch_id",
                schema: "quality",
                table: "inspection_records",
                type: "uuid",
                nullable: true,
                comment: "Original WMS acceptance batch for ERP receiving inspections.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "wms_receipt_batch_id",
                schema: "quality",
                table: "inspection_tasks");

            migrationBuilder.DropColumn(
                name: "wms_receipt_batch_id",
                schema: "quality",
                table: "inspection_records");
        }
    }
}
