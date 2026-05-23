# Quality Inspection MVP Design

## Goal

Extend the existing Quality service from NCR-only scope to inspection planning and inspection recording without giving Quality ownership of inventory, warehouse, ERP or MES state.

## Current State

Quality already has Domain, Infrastructure, Web, PostgreSQL migration and tests for NonconformanceReport. InspectionPlan and InspectionRecord do not exist yet. The new scope must preserve existing NCR routes, permissions, events and tests.

## Owned Facts

Quality owns:

1. InspectionPlan: inspection rule set for SKU, supplier, customer, process step, work center, device asset or document type.
2. InspectionCharacteristic: measured or checked property with target, tolerance, method, severity and sampling rule.
3. InspectionRecord: execution result for a source document or operation.
4. InspectionResultLine: observed values, pass/fail result and defect classification.
5. QualityDispositionReference: link from failed inspection to NCR when an NCR is opened.

Quality does not own:

1. Inventory stock balance, stock movement or location status.
2. WMS inbound/outbound task state.
3. ERP purchase receipt, sales return or supplier document state.
4. MES work order, operation task or report state.
5. MasterData SKU, partner, work center, device asset or reusable reference values.

## MVP Commands And Queries

| API | Purpose | Notes |
| --- | --- | --- |
| `POST /api/business/v1/quality/inspection-plans` | Create an inspection plan. | Supports receiving, operation, final and maintenance inspection categories. |
| `POST /api/business/v1/quality/inspection-plans/{inspectionPlanId}/activate` | Activate a draft plan. | Activated plans are versioned and immutable for execution fields. |
| `GET /api/business/v1/quality/inspection-plans` | List active and draft plans. | Filter by category, SKU, partner, work center and status. |
| `POST /api/business/v1/quality/inspection-records` | Record an inspection execution. | References a source service and source document ID. |
| `POST /api/business/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr` | Open an NCR from a failed record. | Reuses existing NCR aggregate and keeps the source inspection link. |
| `GET /api/business/v1/quality/inspection-records` | List inspection records. | Filter by source document, SKU, category and result. |

## Plan Rules

1. Draft plans can be edited until activation.
2. Activated plans cannot change characteristics that would affect historical records.
3. A plan can be superseded by a new version.
4. Plan applicability is based on public reference IDs and codes, never cross-schema foreign keys.

## Record Rules

1. An inspection record references one source document or operation.
2. A record result is `passed`, `rejected` or `requiresDisposition`.
3. Failed records may open an NCR, but Quality does not directly create stock movement, warehouse task, purchase return or rework order.
4. Measurements store the observed value, unit, result and optional attachment file IDs.
5. Attachment file IDs refer to FileStorage public IDs only.

## Events

Quality publishes ADR 0011 envelope events:

1. `quality.InspectionPassed`
2. `quality.InspectionRejected`
3. `quality.NcrOpened`
4. `quality.DispositionDecided`
5. `quality.NcrClosed`

Inspection events carry source reference, inspected item references, result summary and inspection record ID. They do not command downstream services to mutate state.

## Permissions

Initial permission codes:

1. `business.quality.inspection-plans.manage`
2. `business.quality.inspection-records.create`
3. `business.quality.inspection-records.read`
4. `business.quality.ncr.manage`

## Persistence

Default schema remains `quality`.

New tables:

1. `inspection_plans`
2. `inspection_plan_characteristics`
3. `inspection_records`
4. `inspection_result_lines`

Existing NCR tables remain in place. The migration must extend the current Quality schema rather than replace the existing migration history.

## Testing

Acceptance requires:

1. Domain tests for plan activation, plan immutability, record pass/fail calculation and NCR-from-inspection creation.
2. Web tests for internal authorization, route shape, validation and operation IDs.
3. Regression tests proving existing NCR endpoints still work.
4. Schema convention tests for new tables and columns.
5. Event converter tests for inspection result events and NCR link events.
