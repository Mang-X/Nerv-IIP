# WMS Execution MVP Design

## Goal

Build WMS as the warehouse execution fact source for inbound, outbound, putaway, picking, count execution and WCS adapter task mapping.

WMS owns warehouse workflow state. Inventory remains the only stock balance and stock movement fact source.

## Current State

WMS has no service directory. Wave 1 now provides Inventory stock movement/availability and MES finished-goods receipt request facts. BarcodeLabel will be added in Wave 2 for scan records, but WMS can keep scan references as source device/value strings until BarcodeLabel is available.

## Owned Facts

WMS owns:

1. InboundOrder: receiving/inbound execution header, source document reference and inbound lines.
2. PutawayTask: warehouse task for moving inbound goods to a stock location.
3. OutboundOrder: shipment/outbound execution header, source document reference and outbound lines.
4. PickingTask: warehouse task for picking outbound goods.
5. PackReview: outbound verification and packaging completion result.
6. CountExecution: warehouse count execution facts and variance output.
7. WcsTask: adapter task mapping, external task ID, payload, status, retry and diagnostic facts.
8. InventoryMovementRequest: WMS-owned request metadata for posting Inventory movements through public boundaries.

WMS does not own:

1. Inventory stock balances, stock ledgers or stock movement facts.
2. ERP purchase, sales, invoice or finance state.
3. MES work order or production report state.
4. Quality inspection result ownership.
5. External WCS scheduling internals.

## Inventory Boundary

WMS posts Inventory changes only through public boundaries:

1. Inbound completion requests an Inventory inbound movement with an idempotency key.
2. Outbound completion requests an Inventory outbound movement with an idempotency key.
3. Outbound picking reserves stock through Inventory's public reservation API and stores only the returned public reservation id; outbound completion carries that id so Inventory allocates the reservation during posting.
4. Count completion can request an Inventory count adjustment or emit a count variance event for Inventory/Approval to process.
5. WMS never reads or writes Inventory tables.
6. WMS tests should use an in-process Inventory client fake and verify the request payload shape.

## API Surface

| API | Purpose | Permission |
| --- | --- | --- |
| `POST /api/business/v1/wms/inbound-orders` | Create inbound order from purchase receipt, production receipt request or manual source. | `business.wms.receipts.manage` |
| `GET /api/business/v1/wms/inbound-orders` | List inbound orders. | `business.wms.receipts.read` |
| `POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks` | Create putaway tasks. | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/complete` | Complete inbound and request Inventory movement. | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/outbound-orders` | Create outbound order from delivery request or manual source. | `business.wms.shipments.manage` |
| `GET /api/business/v1/wms/outbound-orders` | List outbound orders. | `business.wms.shipments.read` |
| `POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks` | Create picking tasks. | `business.wms.shipments.manage` |
| `POST /api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/progress` | Record putaway/picking task executed quantity. | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/complete` | Complete putaway/picking task by setting executed quantity to planned quantity. | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/complete` | Complete pack review and request Inventory movement. | `business.wms.shipments.manage` |
| `POST /api/business/v1/wms/count-executions` | Create count execution. | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/count-executions/{countExecutionId}/complete` | Complete count and produce variance output. | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch` | Dispatch WCS adapter task. | `business.wms.automation.manage` |
| `POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/complete` | Record WCS completion callback. | `business.wms.automation.manage` |
| `POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/fail` | Record WCS failure callback. | `business.wms.automation.manage` |

## Rules

1. Completed inbound/outbound orders are immutable.
2. Completion requires an idempotency key.
3. Picked quantity cannot exceed requested outbound quantity.
4. Putaway quantity cannot exceed received inbound quantity.
5. WCS dispatch is idempotent by warehouse task and adapter type.
6. WCS failures store diagnostic code and message and remain compensatable.
7. No WMS table may contain on-hand, available or stock-balance columns.
8. Inventory posting failures must be visible through WMS movement request status.
9. Inventory business posting rejection is represented by public `inventory.StockMovementPostingFailed`; WMS consumes it and marks the matching movement request `Failed`.
10. WMS may persist Inventory public reservation ids for outbound allocation, but must not maintain on-hand, available or reserved balance columns.
11. WCS complete/fail callbacks must match by organization, environment and external task id.

## Events

WMS publishes ADR 0011 envelope events:

1. `wms.InboundOrderCompleted`
2. `wms.OutboundOrderCompleted`
3. `wms.CountExecutionCompleted`
4. `wms.WcsTaskDispatched`
5. `wms.WcsTaskFailed`

Events carry public order/task references, SKU/UOM/location dimensions, quantities and correlation IDs. They must not carry Inventory database IDs or external WCS secrets.

## Permissions

Initial permission codes:

1. `business.wms.receipts.read`
2. `business.wms.receipts.manage`
3. `business.wms.shipments.read`
4. `business.wms.shipments.manage`
5. `business.wms.automation.manage`

## Persistence

Default schema: `wms`.

Required tables:

1. `inbound_orders`
2. `inbound_order_lines`
3. `outbound_orders`
4. `outbound_order_lines`
5. `warehouse_tasks`
6. `count_executions`
7. `wcs_tasks`
8. `inventory_movement_requests`

Each table and business column requires schema comments. PostgreSQL migrations history must use `wms.__EFMigrationsHistory`.

## Testing

Acceptance requires:

1. Domain tests for inbound completion, putaway bounds, outbound picking, pack review, immutability and idempotency.
2. Domain tests for WCS dispatch/complete/fail lifecycle and diagnostics.
3. Web tests for route shape, authorization, validation and operation IDs.
4. Inventory client fake tests verifying movement request payload and idempotency key shape.
5. Schema convention tests using `Nerv.IIP.Testing`.
6. Integration event converter/serialization tests for WMS events.
7. Tests proving WMS schema does not introduce stock balance columns.

## Receiving quality-gate product flow

The Business Console receiving page consumes the WMS quality-gate read model as
the source of truth for the operator-facing flow. Each inbound order presents
the server-returned path `收货 → 待检 → 合格上架/不合格隔离退供`:

1. `pending`/`inspection` blocks putaway and explains that inspection must be
   completed before the action is available.
2. `not-required` honestly skips the inspection step and releases the line for
   putaway; no inspection task is invented for exempt lines.
3. `conditional-release` keeps putaway available only as a visibly restricted
   action and states that it is not unconditional acceptance.
4. `rejected` blocks putaway and displays the real quarantine location,
   disposition reason and supplier-return number when WMS has returned one.
5. The inspection-task link uses the stable source-document contract
   `/quality/inspection-tasks?sourceDocumentNo=<inboundOrderNo>`. Until WMS or
   Quality supplies a stable `inspectionTaskId` in this read model, the UI does
   not infer one from SKU, line or inspection record.

After a receiving mutation, the page refreshes inbound orders, quality gates
and supplier returns from the server. It never uses local optimistic status to
claim that a gate or putaway has completed.
