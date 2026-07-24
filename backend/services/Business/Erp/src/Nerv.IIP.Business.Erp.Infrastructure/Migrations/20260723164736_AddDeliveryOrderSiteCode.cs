using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryOrderSiteCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "erp",
                table: "delivery_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Authoritative sales fulfillment site copied from the source sales order.");

            migrationBuilder.Sql(
                """
                UPDATE erp.delivery_orders AS delivery
                SET site_code = sales.site_code
                FROM erp.sales_orders AS sales
                WHERE sales.organization_id = delivery.organization_id
                  AND sales.environment_id = delivery.environment_id
                  AND sales.sales_order_no = delivery.sales_order_no;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    missing_site_count bigint;
                BEGIN
                    SELECT COUNT(*)
                    INTO missing_site_count
                    FROM erp.delivery_orders
                    WHERE site_code IS NULL;

                    IF missing_site_count > 0 THEN
                        RAISE EXCEPTION
                            'Cannot require erp.delivery_orders.site_code: % delivery rows have no matching sales order site in the same organization and environment.',
                            missing_site_count
                            USING ERRCODE = '23502';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "site_code",
                schema: "erp",
                table: "delivery_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Authoritative sales fulfillment site copied from the source sales order.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Authoritative sales fulfillment site copied from the source sales order.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "erp",
                table: "delivery_orders");
        }
    }
}
