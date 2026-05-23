# Business Wave 2 Agent Session Design

## Context

Business Wave 1 closed the blocking foundation for the next execution wave:

1. ProductEngineering now owns released EBOM, MBOM, Routing, ProductionVersion and engineering change facts.
2. Inventory now owns stock locations, ledgers, movements, availability and count adjustments.
3. Quality now owns inspection plans, inspection records and NCR behavior.
4. MES now has durable CleanDDD Domain/Infrastructure/Web state for work orders, operation tasks, reports, schedules and finished-goods receipt requests.
5. BusinessMasterData, ProductEngineering, Inventory, Quality and MES are in `backend/Nerv.IIP.sln`, Aspire AppHost and Wave 1 verify scripts.

Wave 2 is the first downstream business execution wave. It should unlock planning, warehouse execution, barcode/scan workflows and business approval without starting ERP, IIoT or Maintenance too early.

## Wave 2 Scope

Wave 2 includes these execution issues:

1. #128 DemandPlanning MVP.
2. #133 BarcodeLabel MVP.
3. #134 BusinessApproval MVP.
4. #136 WMS execution MVP.

Sidecar design-system work:

1. #143 Frontend component gap closure should be governed by `frontend/DESIGN`, with a Superpowers plan only as an execution checklist.

Deferred:

1. #142 FileStorage MinIO/S3 multipart object-storage integration remains post-MVP and should not block business service development.
2. ERP #137 to #139 should start after DemandPlanning suggestions and WMS execution contracts are stable.
3. IndustrialTelemetry #129 and Maintenance #130 should start after the core order/planning/warehouse chain has a service-level baseline, unless a device-maintenance demo becomes the priority.

## Goals

1. Give each Wave 2 agent a self-contained implementation handoff.
2. Keep planning and warehouse facts explainable through ProductEngineering and Inventory instead of fixture-only shortcuts.
3. Add BarcodeLabel and BusinessApproval as independent Layer 1 services so later WMS, ERP, MES and ProductEngineering work can reference labels, scans and approvals.
4. Keep shared-file conflict pressure low by separating service-local implementation from Wave 2 registration/readiness integration.
5. Keep frontend component work aligned with the design system instead of turning #143 into ad hoc plan fragments.

## Non-Goals

1. Do not implement ERP Procurement, Sales or Finance in Wave 2.
2. Do not build APS, finite-capacity optimization, Gantt or scheduling visualizations.
3. Do not introduce MinIO/S3 multipart as a prerequisite for business attachments or uploads.
4. Do not let WMS own stock balances or let DemandPlanning create formal purchase orders or work orders.
5. Do not import shadcn-vue internals directly into application pages.

## Session Boundaries

| Session | Issue | Owns | Must Not Own |
| --- | --- | --- | --- |
| DP-MRP | #128 | DemandSource, MPS, MrpRun, PlanningSuggestion, daily-bucket MRP, pegging and planning events. | ERP requisitions, MES work orders, Inventory balances, ProductEngineering version authoring. |
| BARCODE | #133 | Barcode rules, label templates, print batches, scan records and idempotent print/scan workflows. | Inventory quantities, WMS task state, FileStorage object keys, business document status. |
| APPROVAL | #134 | Approval templates, approval chains, approval steps, approval records and business approval events. | Ops operation approvals, IAM roles/permissions, audit log ownership. |
| WMS | #136 | Inbound/outbound execution, putaway/pick/count tasks, WCS adapter task mapping and warehouse completion events. | Stock balances, purchase/sales/work-order state, external WCS internals. |
| WAVE2-INTEG | #77 follow-up | Shared solution entries, AppHost resources, verify scripts, schema/permission/readiness docs after service branches are ready. | Domain behavior owned by service sessions. |
| DS-READY | #143 | DESIGN docs, shadcn primitive exports, upload/chart/date/sheet/tabs component contracts. | Business domain logic, MinIO/S3 multipart, page-specific styling. |

## Dependency Rules

1. #128 can start now because ProductEngineering release facts and Inventory availability exist. It should use fixture-backed adapters first, then stable service/API contracts.
2. #133 and #134 have no hard backend dependency and can run immediately in parallel.
3. #136 can start now because Inventory movement/availability and MES finished-goods receipt request facts exist. WMS should keep Inventory posting behind an internal client/adapter so service-local tests do not require another service process.
4. WAVE2-INTEG should run after at least one Wave 2 service compiles. It should integrate only services that are actually present and passing focused tests.
5. DS-READY can run in parallel with backend work, but it should update `frontend/DESIGN` first and implement components only after the design contract is written.

## Shared File Policy

Service sessions should primarily write under their own directories:

1. `backend/services/Business/DemandPlanning`
2. `backend/services/Business/BarcodeLabel`
3. `backend/services/Business/Approval`
4. `backend/services/Business/Wms`
5. optional public contracts under `backend/common/Contracts/Nerv.IIP.Contracts.{Context}`

Shared files should be coordinated by WAVE2-INTEG:

1. `backend/Nerv.IIP.sln`
2. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
3. `docs/architecture/authorization-matrix.md`
4. `docs/architecture/database-schema-catalog.md`
5. `docs/architecture/implementation-readiness.md`
6. `scripts/verify-business-*.ps1`

If a service session must touch a shared file to run locally, it should keep the change minimal and include a `Shared Changes Needed` section in its handoff.

## Merge Gates

Each service session must provide:

1. Domain tests for aggregate invariants and immutability rules.
2. Web/API contract tests for route shape, authorization expectations, validation and operation IDs.
3. Schema convention tests for PostgreSQL-backed services.
4. Integration event converter or contract serialization tests for published events.
5. Permission codes and schema catalog entries ready for WAVE2-INTEG.
6. A dedicated verify script request, even if the script is created by WAVE2-INTEG.

WAVE2-INTEG must provide:

1. Solution membership for all ready Wave 2 projects.
2. AppHost database and service registrations for all ready Web projects.
3. Per-service verify scripts and `scripts/verify-business-wave2-execution.ps1`.
4. Readiness documentation showing which services are ready, skipped or blocked.
5. Confirmation that Wave 1 aggregate verification still passes after registration.

## #142 Decision

MinIO/S3 multipart does not block the next business wave. The current FileStorage baseline already provides metadata contracts, SDK, PostgreSQL-backed metadata and a local tus endpoint. Business services should store only `fileId` or `FileReference` values and should not care whether bytes are currently local tus, server-proxy or S3 multipart.

Deferring #142 is low-risk if the following rules stay true:

1. Public contracts never expose object storage keys or long-lived object URLs.
2. Upload sessions keep provider/upload-mode fields as internal FileStorage decisions.
3. FileUpload UI talks to FileStorage upload-session and tus/download-grant endpoints, never directly to MinIO.
4. Later S3 multipart work remains an Upload Provider adapter behind FileStorage-controlled grants.

Start #142 only when multi-node deployment, large-file direct upload, production object-store retention, or external client direct-to-object-storage becomes a near-term requirement.

## #143 Decision

#143 is a design-system readiness issue. The canonical spec belongs in `frontend/DESIGN`, not only in `docs/superpowers/plans`.

Immediate stance:

1. Use shadcn-vue registry primitives for Tabs, Sheet, Popover, Calendar/RangeCalendar and Chart where possible.
2. Build FileUpload as a Nerv-IIP wrapper with shadcn visual structure and FileStorage semantics.
3. Prefer Uppy core/headless plus `@uppy/tus` for resumable tus uploads when real upload progress, retry and pause/resume are needed. Do not make the Uppy Dashboard visual skin the design baseline.
4. A hand-written tus client is acceptable only for a narrow single-file local upload path; it should not become the default if resumability and protocol compatibility matter.
5. Component exports must go through `@nerv-iip/ui`, with DESIGN component docs updated before application consumption.

## Recommended Agent Order

Start these in parallel:

1. DP-MRP (#128)
2. BARCODE (#133)
3. APPROVAL (#134)
4. WMS (#136)

Run these as sidecar or follow-up sessions:

1. DS-READY (#143) when frontend business console work is about to begin.
2. WAVE2-INTEG after the first two backend services compile, and again after all ready services are available.

Do not start ERP #137 to #139 until DP-MRP has stable suggestion APIs/events and WMS has stable inbound/outbound completion contracts.

