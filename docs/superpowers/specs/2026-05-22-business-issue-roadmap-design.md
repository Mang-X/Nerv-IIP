# Business Issue Roadmap Design

## Context

The business-platform GitHub issues currently mix old epic-style scope, completed work, partially completed infrastructure, and newer slice-specific issues. This makes it hard to decide what to execute next because the issue list no longer matches the code and documentation facts.

This design organizes the current issues into epics and executable child issues before changing ADRs, architecture docs, specs, or implementation plans.

## Source Facts

Code facts as of 2026-05-22:

1. `backend/services/Business/MasterData` exists with Domain, Infrastructure, Web, migrations and tests.
2. `backend/services/Business/ProductEngineering` exists with Domain, Infrastructure, Web, migrations and tests, but the implemented scope is currently ProductionVersion only.
3. `backend/services/Business/Quality` exists with Domain, Infrastructure, Web, migrations and tests, but the implemented scope is currently NonconformanceReport.
4. `backend/services/Business/Mes` exists only as Web plus Web tests, with in-memory planning and reschedule behavior.
5. `Inventory`, `DemandPlanning`, `Wms`, `Erp`, `IndustrialTelemetry`, `Maintenance`, `BarcodeLabel` and `Approval` service directories do not exist.
6. `infra/aspire/Nerv.IIP.AppHost` does not register business services.
7. `scripts/verify-business-master-data-realignment.ps1` exists; the other business verify scripts referenced by #77 do not exist.
8. Notification service, Gateway notification facade, Console notifications UI, FileStorage contracts/SDK, PostgreSQL metadata and local tus upload/download MVP are already present.

Documentation facts:

1. ADR 0012 remains the correct domain layering decision.
2. ADR 0013 remains the correct BusinessMasterData governance decision.
3. `docs/architecture/implementation-readiness.md` is the canonical current-state source and already records BusinessMasterData realignment and FileStorage MVP facts.
4. `docs/architecture/business-platform-domain-architecture.md` correctly defines the key chain model, but it does not yet map GitHub issues to executable slices.
5. Existing plans under `docs/superpowers/plans/2026-05-20-business-*.md` are useful inputs, but some are stale because code landed after they were written.

## Goals

1. Make every open non-Gantt issue map to either an epic, an executable child issue, or a known future follow-up.
2. Preserve useful historical issues instead of closing them prematurely.
3. Close only issues whose scope is fully superseded or complete.
4. Keep GitHub issues aligned with ADR 0012, ADR 0013, implementation readiness and actual code facts.
5. Prepare clean inputs for later architecture updates, specs and plans.

## Non-Goals

1. Do not implement service code in this step.
2. Do not edit generated API clients.
3. Do not use issue cleanup to change business boundaries.
4. Do not reopen #72.
5. Do not include #78 Gantt/RFC work in this roadmap.

## Issue Treatment

| Issue | Action | Reason |
| --- | --- | --- |
| #70 基础设施收尾（一期） | Keep open, rewrite as an infrastructure completion epic | Notification/FileStorage/UI scope is partially complete; the body is stale. |
| #71 基础设施收尾（二期） | Keep open, rewrite as a production-readiness epic | The scope remains valid but needs child issues and current facts. |
| #72 共享基础域（Layer 0） | Leave closed | BusinessMasterData realignment is complete enough for Layer 0 tracking; follow-up belongs in downstream issues. |
| #73 通用能力域（Layer 1） | Keep open, rewrite as an epic | It should track Inventory, Quality inspection, BarcodeLabel and BusinessApproval child issues. |
| #74 MES | Keep open, rewrite as an epic | MES has partial in-memory Web implementation; CleanDDD persistence and execution need child issues. |
| #75 WMS | Keep open, rewrite as an epic | No code exists yet; one or more WMS execution child issues should close it. |
| #76 ERP | Keep open, rewrite as an epic | Scope is too large for one execution issue; split Procurement, Sales and Finance. |
| #77 Full-chain acceptance | Keep open, rewrite as acceptance epic | It must remain blocked until all business MVP verify scripts pass. |
| #78 Gantt RFC | Excluded | User explicitly asked to ignore Gantt-related issue. |
| #127 ProductEngineering MVP | Keep open as execution issue | This is the current child issue for ProductEngineering completion. |
| #128 DemandPlanning MVP | Keep open as execution issue | This is the current child issue for MPS/MRP. |
| #129 IndustrialTelemetry MVP | Keep open as execution issue | This is the current child issue for IIoT/Telemetry. |
| #130 Maintenance MVP | Keep open as execution issue | This is the current child issue for CMMS-lite. |

## New Child Issues To Create

Create these child issues before large code work starts:

1. `feat: Inventory MVP - stock ledger, movement, availability and counts`
   - Parent: #73
   - Labels: `enhancement`, `business-platform`
   - Depends on: #72 completed, MasterData resolve/validate APIs
   - Plan input: `docs/superpowers/plans/2026-05-20-business-common-capability-foundation.md`

2. `feat: Quality inspection MVP - inspection plan, record and receiving/operation inspection`
   - Parent: #73
   - Labels: `enhancement`, `business-platform`, `quality`
   - Depends on: current Quality NCR implementation
   - Scope: add InspectionPlan and InspectionRecord without moving NCR backward.

3. `feat: BarcodeLabel MVP - rules, templates, print batches and scans`
   - Parent: #73
   - Labels: `enhancement`, `business-platform`
   - Depends on: MasterData SKU/barcode policy and FileStorage for template references if needed.

4. `feat: BusinessApproval MVP - templates, approval chains and approval records`
   - Parent: #73
   - Labels: `enhancement`, `business-platform`
   - Depends on: IAM user/context only; must not replace Ops approval.

5. `feat: MES CleanDDD persistence and execution MVP`
   - Parent: #74
   - Labels: `enhancement`, `business-platform`
   - Depends on: current MES Web tests, ProductEngineering ProductionVersion contract.
   - Scope: introduce Domain/Infrastructure and PostgreSQL schema, then migrate current in-memory scheduler behavior.

6. `feat: WMS execution MVP - inbound, outbound, count and WCS adapter boundary`
   - Parent: #75
   - Labels: `enhancement`, `business-platform`
   - Depends on: Inventory movement API.

7. `feat: ERP Procurement MVP - requisitions, RFQ, purchase orders and receipts`
   - Parent: #76
   - Labels: `enhancement`, `business-platform`
   - Depends on: DemandPlanning planned purchase suggestions, WMS inbound boundary.

8. `feat: ERP Sales MVP - opportunity, quotation, sales order and delivery request`
   - Parent: #76
   - Labels: `enhancement`, `business-platform`
   - Depends on: WMS outbound boundary, Inventory availability query.

9. `feat: ERP Finance MVP - receivables, payables, vouchers and cost candidates`
   - Parent: #76
   - Labels: `enhancement`, `business-platform`
   - Depends on: Procurement/Sales/WMS/Inventory facts.

10. `chore: Business service registration, verify script pattern and readiness tracking`
    - Parent: #77
    - Labels: `enhancement`, `business-platform`
    - Scope: AppHost registration strategy, solution membership checks, verification script template and readiness status updates for each business service.

11. `feat: FileStorage tus hardening - size, checksum, expiration and protocol compatibility`
    - Parent: #70
    - Labels: `enhancement`
    - Depends on: current FileStorage local tus MVP.

12. `feat: FileStorage object storage integration - MinIO/S3 multipart post-MVP`
    - Parent: #70
    - Labels: `enhancement`
    - Depends on: FileStorage hardening and deployment profile.

13. `feat: Frontend component gap closure for business console readiness`
    - Parent: #70
    - Labels: `enhancement`, `area:frontend`
    - Scope: missing Sheet, Tabs, date picker, file upload and chart primitives; Table/Dialog/Select/Pagination already exist.

## Epic Rewrite Templates

### #70 Replacement Body

```markdown
## Current Facts

This issue is now an infrastructure completion epic, not a from-scratch implementation task.

Already present:
- Notification service Domain/Infrastructure/Web, contracts, SDK, Gateway facade and Console notifications UI.
- FileStorage contracts/SDK, PostgreSQL metadata service, schema convention tests and local tus HEAD/PATCH upload plus download content endpoint.
- Core shadcn-vue primitives including Table, Dialog, AlertDialog, Select, Pagination and Empty.

## Remaining Scope

1. FileStorage tus hardening: size validation, checksum validation, expiration cleanup and broader tus compatibility.
2. FileStorage object-storage deployment integration: MinIO/S3 multipart remains post-MVP.
3. Frontend component gaps needed by business-console readiness: Sheet, Tabs, date/date-range, file upload and chart primitives.
4. Notification follow-ups only where they are not already implemented: preferences, external providers or additional event consumers.

## Child Issues

- `feat: FileStorage tus hardening - size, checksum, expiration and protocol compatibility`
- `feat: FileStorage object storage integration - MinIO/S3 multipart post-MVP`
- `feat: Frontend component gap closure for business console readiness`

## Out Of Scope

- Gantt and scheduling visualization (#78).
- Rebuilding already delivered Notification/FileStorage MVP capabilities.
```

### #71 Replacement Body

```markdown
## Current Facts

This issue tracks production-readiness work that spans platform and business services.

Already present:
- PostgreSQL migration baseline for AppHub, Ops, IAM, FileStorage, Notification and selected business services.
- Schema convention tests for migrated services.
- Messaging provider can default to InMemory and use RabbitMQ only when configured.

## Remaining Scope

1. CAP/outbox publish-subscribe acceptance across business services.
2. IntegrationEvent consumer idempotency, version checks, DLQ/replay guidance and tests.
3. IAM ExternalClient and AuthorizationGrant completion.
4. Security hardening: TLS/CORS/secrets/token lifecycle/audit integrity.
5. Production deployment artifacts: Compose, install/start scripts and AppHost coverage.
6. Performance baseline for high-write stock movement and high-read work/order lists.

## Child Issues

Child issues should be created per workstream before implementation. This epic should close only after all child issues and readiness docs are complete.
```

### #73 Replacement Body

```markdown
## Current Facts

This is the Layer 1 common-capability epic.

Already present:
- BusinessQuality service exists with NonconformanceReport aggregate and APIs.

Not present yet:
- Inventory service.
- BarcodeLabel service.
- BusinessApproval service.
- Quality InspectionPlan and InspectionRecord.

## Child Issues

- `feat: Inventory MVP - stock ledger, movement, availability and counts`
- `feat: Quality inspection MVP - inspection plan, record and receiving/operation inspection`
- `feat: BarcodeLabel MVP - rules, templates, print batches and scans`
- `feat: BusinessApproval MVP - templates, approval chains and approval records`

## Acceptance

1. Inventory is the only stock balance and stock movement fact source.
2. Quality inspection and NCR do not directly mutate inventory, WMS, ERP or MES.
3. Barcode commands are idempotent for print/scan workflows.
4. BusinessApproval handles business document approval and does not replace Ops.
5. IAM seed, authorization matrix, schema catalog, migrations and verify scripts are updated per service.
```

### #74 Replacement Body

```markdown
## Current Facts

This is the MES execution epic.

Already present:
- `backend/services/Business/Mes` Web project and Web tests.
- In-memory scheduling, rush work order and maintenance asset event handler behavior.

Not present yet:
- MES Domain project.
- MES Infrastructure project.
- PostgreSQL schema and migration.
- Persistent WorkOrder, OperationTask, ProductionReport and FinishedGoodsReceiptRequest facts.

## Child Issues

- `feat: MES CleanDDD persistence and execution MVP`
- Later MES integration issues may be created for Planning, WMS, Quality, Telemetry and Maintenance wiring.

## Acceptance

1. Current scheduler behavior is preserved or intentionally adjusted by tests.
2. MES stores durable work order, operation task, report and schedule facts.
3. Work orders reference ProductEngineering ProductionVersion rather than duplicating engineering facts.
4. Finished goods receipt requests integrate with WMS through API/event boundaries, not shared tables.
```

### #75 Replacement Body

```markdown
## Current Facts

This is the WMS execution epic. No WMS service code exists yet.

## Child Issues

- `feat: WMS execution MVP - inbound, outbound, count and WCS adapter boundary`

## Acceptance

1. WMS owns inbound/outbound execution facts and WCS adapter task mapping.
2. WMS does not store stock balances.
3. WMS requests Inventory movements after inbound/outbound completion.
4. WCS adapter failures are diagnosable and compensatable.
```

### #76 Replacement Body

```markdown
## Current Facts

This is the ERP epic. No ERP service code exists yet. The scope is too large for a single execution issue.

## Child Issues

- `feat: ERP Procurement MVP - requisitions, RFQ, purchase orders and receipts`
- `feat: ERP Sales MVP - opportunity, quotation, sales order and delivery request`
- `feat: ERP Finance MVP - receivables, payables, vouchers and cost candidates`

## Acceptance

1. Procurement can accept planned purchase suggestions and record purchase receipts.
2. Sales can create delivery requests for WMS fulfillment.
3. Finance creates receivable/payable/voucher/cost candidates from business facts and enforces balanced voucher entries.
4. ERP does not own WMS execution or Inventory balances.
```

### #77 Replacement Body

```markdown
## Current Facts

This issue is the final business full-chain acceptance epic. It must not start until all business MVP verify scripts exist and pass.

## Blocking Verify Scripts

- `scripts/verify-business-master-data-realignment.ps1` exists.
- The remaining business verify scripts must be created by their owning slice issues.

## Acceptance Chains

1. Engineering to manufacturing.
2. Planning to procurement/production.
3. Procurement to inventory to accounts payable.
4. Order to delivery to accounts receivable.
5. Production execution to cost.
6. Equipment to maintenance to capacity.
7. WMS to WCS adapter.

## Test Rules

Acceptance tests live under `backend/tests/Nerv.IIP.Business.Acceptance.Tests/` and verify only public HTTP APIs plus IntegrationEvent-visible results. They must not read service databases directly.
```

## Execution Order

1. Rewrite #70, #71 and #73-#77 as epics.
2. Create the missing child issues listed above.
3. Add a comment to #127-#130 linking them to their business slice, dependency facts and current plan.
4. Update `docs/architecture/business-platform-domain-architecture.md` with issue-to-slice mapping.
5. Update `docs/architecture/implementation-readiness.md` with the business service code fact table.
6. Update or create focused specs/plans in this order:
   - ProductEngineering completion for #127.
   - Common Capability v2 for #73 children.
   - DemandPlanning for #128 after ProductEngineering and Inventory minimum contracts.
   - MES persistence for #74.
   - WMS, ERP, IndustrialTelemetry and Maintenance in dependency order.

## Validation

After issue cleanup:

1. `gh issue list --state open --label business-platform --limit 200` should show epics plus executable child issues.
2. Every open business-platform issue should include at least one of: parent epic, blocking dependency, plan path, or explicit future status.
3. No open issue except #78 should describe stale from-scratch scope for work already implemented.
4. `implementation-readiness.md` should remain the canonical code-fact summary.

## Open Decisions

1. Keep #70 open as an epic until FileStorage/UI follow-up child issues complete. Close it only after the child issues are closed and readiness docs name the remaining post-MVP exclusions.
2. Create separate BarcodeLabel and BusinessApproval child issues because they have different fact ownership and different downstream consumers.
3. Track business service AppHost registration in a dedicated cross-cutting issue to avoid repeated merge conflicts in each service slice.
