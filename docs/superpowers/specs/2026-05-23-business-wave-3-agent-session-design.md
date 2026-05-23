# Business Wave 3 Agent Session Design

## Context

Wave 1, Wave 2 and the Equipment Reliability side wave have closed the non-ERP business service baseline:

1. #127 ProductEngineering is closed and the service owns EBOM, MBOM, Routing, ProductionVersion and ECO/ECN facts.
2. #128 DemandPlanning is closed and the service owns MPS/MRP runs, pegging and planning suggestions.
3. #129 IndustrialTelemetry and #130 Maintenance are closed and connected through public integration contracts.
4. #131, #132, #133, #134, #135 and #136 are closed. Inventory, Quality inspection, BarcodeLabel, BusinessApproval, MES persistence and WMS execution are present in code.
5. `docs/architecture/implementation-readiness.md` records ports 5107 through 5117 and the verify scripts for all completed business services.

The only open business execution children after this are ERP #137, #138 and #139. Full-chain acceptance #77 remains blocked until ERP is complete.

## Wave 3 Scope

Wave 3 includes these execution issues:

1. #137 ERP Procurement MVP - requisitions, RFQ, purchase orders and receipts.
2. #138 ERP Sales MVP - opportunity, quotation, sales order and delivery request.
3. #139 ERP Finance MVP - receivables, payables, vouchers and cost candidates.

Wave 3 shared integration includes:

1. `backend/Nerv.IIP.sln` membership for the ERP service and tests.
2. Aspire AppHost registration for `business-erp` on the next local port, 5118 unless the port matrix changes before implementation.
3. IAM seed, authorization matrix, database schema catalog and implementation readiness updates for ERP.
4. ERP focused verify scripts and a final `verify-business-erp-procurement-sales-finance-mvp.ps1`.

Wave 4 starts after Wave 3 passes and covers Full-chain acceptance #77.

## Goals

1. Build ERP as one CleanDDD business service under `backend/services/Business/Erp`.
2. Keep Procurement, Sales and Finance executable as issue-sized slices without pretending they are conflict-free parallel code streams.
3. Let ERP consume DemandPlanning suggestions, WMS completion facts, Inventory movement facts and MES production facts through public APIs/events only.
4. Preserve the service ownership rule: ERP owns commercial and finance documents, not WMS execution state or Inventory balances.
5. Provide a clear handoff from ERP completion to Full-chain acceptance.

## Non-Goals

1. Do not reopen closed Wave 1, Wave 2 or Equipment Reliability service MVP issues.
2. Do not create standalone SRM, CRM, CPQ or OMS services in this wave.
3. Do not implement full general ledger month-end close, tax engine or statutory reporting.
4. Do not let ERP write Inventory stock balances, WMS warehouse tasks or MES work orders directly.
5. Do not start Full-chain acceptance against fixture-only ERP behavior.

## Session Boundaries

| Session | Issue | Owns | Must Not Own |
| --- | --- | --- | --- |
| ERP-PROC | #137 | ERP scaffold, shared ERP base types, Procurement/SRM-lite aggregates, procurement endpoints, initial `erp` schema. | Sales order lifecycle, Finance posting, AppHost-wide integration if the service branch is not ready. |
| ERP-SALES | #138 | Opportunity, quotation, sales order, delivery order, sales endpoints and delivery release events. | Inventory allocation ownership, WMS picking/packing execution, Finance receivable posting internals. |
| ERP-FIN | #139 | Account receivable, account payable, voucher and cost candidate aggregates; balanced voucher guardrails; finance event consumers/converters. | Full ledger close, WMS/Inventory/MES internal tables, tax or bank settlement. |
| ERP-INTEG | #76/#77 follow-up | Solution, AppHost, port 5118, IAM seed, authorization matrix, schema catalog, readiness, README and ERP verify scripts. | Domain behavior owned by #137 to #139. |
| FULLCHAIN | #77 | Acceptance harness and seven critical chain tests after ERP passes. | Service-local domain fixes unless a blocking defect is found and assigned back to the owner. |

## Dependency Rules

1. ERP-PROC must run first because it creates the service directory, shared project structure, DbContext and baseline migration.
2. ERP-SALES starts after ERP-PROC compiles or after a stable scaffold branch is available.
3. ERP-FIN can design domain tests in parallel, but final implementation should wait for procurement receipt and sales delivery event shapes.
4. ERP-INTEG should run after at least one ERP slice compiles and must finish only after #137, #138 and #139 are ready.
5. FULLCHAIN starts after `verify-business-erp-procurement-sales-finance-mvp.ps1` and all completed business service verify scripts pass.

## Shared File Policy

ERP service sessions primarily write under:

1. `backend/services/Business/Erp`
2. optional public contracts under `backend/common/Contracts/Nerv.IIP.Contracts.Erp`

Shared files are coordinated by ERP-INTEG:

1. `backend/Nerv.IIP.sln`
2. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
3. `docs/architecture/authorization-matrix.md`
4. `docs/architecture/database-schema-catalog.md`
5. `docs/architecture/implementation-readiness.md`
6. `README.md`
7. `scripts/verify-business-erp-*.ps1`

If a service slice touches a shared file to run locally, it must include a `Shared Changes Needed` section in its final handoff.

## Merge Gates

Each ERP slice must provide:

1. Domain tests for aggregate invariants and lifecycle transitions.
2. Web/API contract tests for routes, operation IDs, authorization policy and validation.
3. Schema convention tests for all mapped `erp` tables.
4. Integration event converter or contract serialization tests for published events.
5. Evidence that no ERP table owns stock balance, warehouse execution steps or MES production task state.

## Pre-Acceptance Hardening Checks

Worker audit found these non-blocking risks in completed services. ERP can start, but Full-chain acceptance should not claim final closure until they are reviewed or explicitly deferred:

1. WMS currently keeps integration events in Web-local contracts and uses a replaceable/no-op Inventory movement client by default. Before Full-chain, decide whether to promote WMS public events to `backend/common/Contracts` and wire a real Inventory adapter for the acceptance profile.
2. MES has durable Domain/Infrastructure state, but its public query surface is thinner than other services. Full-chain tests may need read APIs for work order, operation task, report and receipt request status.
3. Some earlier business endpoints rely on permission checks without an explicit internal-service policy. Verify MasterData, ProductEngineering and Quality endpoint authorization contracts before using them as acceptance entrypoints.
4. ProductEngineering's design-time DbContext factory should be checked against the current migrations history table convention before the ERP migration batch uses it as a reference pattern.

ERP-INTEG must provide:

1. Solution membership for ready ERP projects and tests.
2. AppHost database and service registration.
3. IAM seed and authorization matrix entries for Procurement, Sales and Finance permissions.
4. ERP verify scripts and implementation readiness updates.
5. Confirmation that Wave 1, Wave 2 and Equipment Reliability aggregate verify scripts remain the prerequisites for Full-chain acceptance.

## Recommended Order

1. Start ERP-PROC (#137).
2. Start ERP-SALES (#138) after the ERP scaffold and common domain conventions exist.
3. Start ERP-FIN (#139) after receipt, delivery and production cost input events are stable.
4. Run ERP-INTEG as soon as the service is ready for AppHost registration, then again after all ERP slices pass.
5. Start FULLCHAIN (#77) only after ERP-INTEG produces the final ERP verify script.
