using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReservationExpiration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at_utc",
                schema: "inventory",
                table: "stock_reservations",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC deadline after which an open reservation is automatically released.");

            migrationBuilder.Sql("""
                UPDATE inventory.stock_reservations
                SET expires_at_utc = CURRENT_TIMESTAMP + CASE
                    WHEN lower(source_service) IN ('wms', 'business-wms') THEN INTERVAL '2 hours'
                    WHEN lower(source_service) IN ('mes', 'business-mes') THEN INTERVAL '8 hours'
                    ELSE INTERVAL '4 hours'
                END
                WHERE expires_at_utc IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                schema: "inventory",
                table: "stock_reservations",
                type: "timestamp with time zone",
                nullable: false,
                comment: "UTC deadline after which an open reservation is automatically released.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "UTC deadline after which an open reservation is automatically released.");

            migrationBuilder.AddColumn<int>(
                name: "row_version",
                schema: "inventory",
                table: "stock_reservations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Optimistic row version for concurrent reservation renewal and expiration.");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_expiration_scan",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "organization_id", "environment_id", "expires_at_utc", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_expiration_scan",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "expires_at_utc",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "inventory",
                table: "stock_reservations");
        }
    }
}
