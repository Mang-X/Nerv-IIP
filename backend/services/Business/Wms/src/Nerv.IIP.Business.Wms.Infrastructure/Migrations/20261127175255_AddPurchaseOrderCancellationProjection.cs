using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderCancellationProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_order_cancellations",
                schema: "wms",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    purchase_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "ERP purchase order number unique within the organization and environment."),
                    purchase_order_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Stable ERP purchase order identity for consistency checks."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Site owning the cancelled purchase order."),
                    purchase_order_version = table.Column<int>(type: "integer", nullable: false, comment: "ERP aggregate version at the committed cancellation."),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time of the committed ERP cancellation."),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Reason for closing the purchase order receiving source.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_cancellations", x => new { x.organization_id, x.environment_id, x.purchase_order_no });
                },
                comment: "Terminal ERP cancellation projection for WMS-owned purchase orders.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_cancellations",
                schema: "wms");
        }
    }
}
