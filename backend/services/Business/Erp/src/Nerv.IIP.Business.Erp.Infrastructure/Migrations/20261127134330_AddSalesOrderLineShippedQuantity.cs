using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderLineShippedQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "shipped_quantity",
                schema: "erp",
                table: "sales_order_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Actual WMS shipped quantity across ERP delivery orders for this sales order line.");

            // The earlier delivery projection migration added zero defaults without historical quantities.
            // A legacy completion inbox entry identifies the order, but does not preserve shipped quantities.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    invalid_delivery record;
                BEGIN
                    SELECT d.organization_id, d.environment_id, d.delivery_order_no, d.status
                    INTO invalid_delivery
                    FROM erp.delivery_orders d
                    CROSS JOIN LATERAL (
                        SELECT count(*) AS line_count,
                               bool_or(l.shipped_quantity > 0) AS has_shipment,
                               bool_or(l.shipped_quantity < l.quantity) AS has_remaining,
                               bool_or(l.quantity <= 0 OR l.shipped_quantity < 0
                                   OR l.shipped_quantity > l.quantity) AS invalid_quantity
                        FROM erp.delivery_order_lines l
                        WHERE l.delivery_order_id = d.id
                    ) evidence
                    WHERE evidence.invalid_quantity
                       OR CASE d.status
                              WHEN 'completed' THEN evidence.line_count = 0 OR evidence.has_remaining
                              WHEN 'partially-shipped' THEN evidence.line_count = 0
                                  OR NOT evidence.has_shipment OR NOT evidence.has_remaining
                              WHEN 'released' THEN evidence.has_shipment
                                  OR d.shipped_at_utc IS NOT NULL OR d.completed_at_utc IS NOT NULL
                              WHEN 'cancelled' THEN evidence.has_shipment
                                  OR d.shipped_at_utc IS NOT NULL OR d.completed_at_utc IS NOT NULL
                              ELSE true
                          END
                       OR (d.status IN ('released', 'cancelled') AND EXISTS (
                              SELECT 1
                              FROM erp.processed_integration_events i
                              WHERE i."ConsumerName" = 'business-erp.wms-outbound-completed-ar-accrual'
                                AND i."IdempotencyKey" = concat('wms:outbound-completed:',
                                    d.organization_id, ':', d.environment_id, ':', d.delivery_order_no)
                          ))
                    LIMIT 1;

                    IF FOUND THEN
                        RAISE EXCEPTION 'Cannot reconcile shipment quantities for ERP delivery %/%/% (status %).',
                            invalid_delivery.organization_id, invalid_delivery.environment_id,
                            invalid_delivery.delivery_order_no, invalid_delivery.status
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    SELECT d.organization_id, d.environment_id, d.delivery_order_no, l.sales_order_line_no
                    INTO invalid_delivery
                    FROM erp.delivery_order_lines l
                    JOIN erp.delivery_orders d ON d.id = l.delivery_order_id
                    WHERE l.shipped_quantity > 0 AND 1 <> (
                        SELECT count(*)
                        FROM erp.sales_orders so
                        JOIN erp.sales_order_lines sl ON sl.sales_order_id = so.id
                        WHERE so.organization_id = d.organization_id
                          AND so.environment_id = d.environment_id
                          AND so.sales_order_no = d.sales_order_no
                          AND sl.line_no = l.sales_order_line_no
                    )
                    LIMIT 1;

                    IF FOUND THEN
                        RAISE EXCEPTION 'Cannot uniquely match ERP delivery %/%/% to sales order line %.',
                            invalid_delivery.organization_id, invalid_delivery.environment_id,
                            invalid_delivery.delivery_order_no, invalid_delivery.sales_order_line_no
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                END $$;

                WITH totals AS (
                    SELECT so.id AS sales_order_id, l.sales_order_line_no,
                           sum(l.shipped_quantity) AS shipped_quantity
                    FROM erp.delivery_order_lines l
                    JOIN erp.delivery_orders d ON d.id = l.delivery_order_id
                    JOIN erp.sales_orders so ON so.organization_id = d.organization_id
                        AND so.environment_id = d.environment_id AND so.sales_order_no = d.sales_order_no
                    WHERE l.shipped_quantity > 0
                    GROUP BY so.id, l.sales_order_line_no
                )
                UPDATE erp.sales_order_lines sl
                SET shipped_quantity = totals.shipped_quantity
                FROM totals
                WHERE sl.sales_order_id = totals.sales_order_id AND sl.line_no = totals.sales_order_line_no;

                DO $$
                DECLARE
                    invalid_line uuid;
                BEGIN
                    SELECT id INTO invalid_line
                    FROM erp.sales_order_lines
                    WHERE shipped_quantity > delivered_quantity
                       OR shipped_quantity > ordered_quantity
                       OR (cancelled AND shipped_quantity > 0)
                    LIMIT 1;

                    IF FOUND THEN
                        RAISE EXCEPTION 'ERP sales order line % has shipment quantities inconsistent with its allocation or state.',
                            invalid_line USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shipped_quantity",
                schema: "erp",
                table: "sales_order_lines");
        }
    }
}
