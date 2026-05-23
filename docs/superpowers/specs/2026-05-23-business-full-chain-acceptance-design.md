# Business Full-Chain Acceptance Design

## Context

The business platform now has code for MasterData, ProductEngineering, Inventory, Quality, MES, DemandPlanning, BarcodeLabel, BusinessApproval, WMS, IndustrialTelemetry and Maintenance. ERP is the remaining business service. Full-chain acceptance #77 should start only after ERP #137, #138 and #139 pass their final verify script.

This design refreshes the older 2026-05-20 full-chain plan with current code facts and issue state.

## Goals

1. Create an acceptance test project outside individual business services.
2. Verify seven critical business chains through public HTTP APIs and integration-event-visible facts.
3. Use authorized clients and service-level contracts, not service database reads, for primary assertions.
4. Produce a single `scripts/verify-business-full-chain-acceptance.ps1` entrypoint.
5. Record enough document IDs and event names in failures to make cross-service defects diagnosable.

## Non-Goals

1. Do not implement missing service domain behavior in the acceptance project.
2. Do not read service tables directly for primary assertions.
3. Do not add a visual Gantt or scheduling UI.
4. Do not use production object storage, external PLC/DCS/SCADA or real WCS hardware.
5. Do not require RabbitMQ unless the verify run explicitly chooses `Messaging:Provider=RabbitMQ`.

## Prerequisites

1. `scripts/verify-business-wave1-foundation.ps1`
2. `scripts/verify-business-wave2-execution.ps1`
3. `scripts/verify-business-equipment-reliability.ps1`
4. `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`
5. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`

## Pre-Acceptance Review

Before writing the seven chain tests, inspect and either fix or document these audit findings:

1. WMS Inventory posting must be real enough for acceptance. A no-op movement client is acceptable for service-local tests but not for procure-to-pay or order-to-cash proof.
2. WMS public event contracts should be consumable outside WMS. If the events remain Web-local, the acceptance harness must use public HTTP outcomes instead of pretending a shared contract exists.
3. MES may need public query endpoints for work order, operation task, production report, schedule and finished-goods receipt request facts.
4. MasterData, ProductEngineering and Quality endpoint authorization should match the repository rule for internal service APIs.
5. CAP/outbox delivery can remain InMemory for the default local profile, but the event assertions must be written so a RabbitMQ profile can be added later without changing domain expectations.

## Acceptance Harness

The test project lives at:

```text
backend/tests/Nerv.IIP.Business.Acceptance.Tests/
```

The harness should:

1. Start service test hosts through existing WebApplicationFactory patterns where available.
2. Use isolated test databases or in-memory profiles consistent with each service's existing tests.
3. Seed IAM/internal-service authorization through existing test helpers.
4. Expose typed or minimal `HttpClient` wrappers for each service.
5. Capture integration events through public event converters, test bus hooks, or visible service API outcomes.
6. Reset state between tests without reaching into another service's production database.

## Chains

| Chain | Required Assertions |
| --- | --- |
| Engineering to manufacturing | Released ProductionVersion references MBOM and Routing; MES work order references ProductionVersion and released route facts. |
| Plan to procure/produce | MRP creates planned purchase and planned work order suggestions; ERP and MES accept them with downstream document IDs; DemandPlanning marks suggestions accepted idempotently. |
| Procure to inventory to payable | ERP receipt, Quality inspection, WMS inbound completion and Inventory movement produce an AP candidate with matching quantity and amount. |
| Order to delivery to receivable | ERP sales order and delivery order, WMS outbound completion and Inventory movement produce an AR candidate with matching shipped quantity and amount. |
| Production execution to cost | MES operation report and finished goods receipt request flow through WMS/Inventory and produce an ERP cost candidate. |
| Equipment to maintenance to capacity | IndustrialTelemetry alarm opens Maintenance work order; Maintenance asset unavailable/restored events are visible to MES scheduling constraints. |
| WMS to WCS adapter | WMS dispatches WCS task, records failure diagnostics, retries and completes task; Inventory movement is not posted before warehouse completion. |

## Test Rules

1. Tests use public APIs and documented integration event contracts.
2. Assertions should prefer stable IDs, statuses, quantities, event names and downstream references.
3. Database reads are allowed only in service-local fixtures already used by that service's own tests, and not as the cross-service acceptance proof.
4. Tests should make failures actionable by including source document ID, downstream document ID, event name and chain name.
5. Tests should be grouped so partial acceptance can run by chain while the full verify script runs everything.

## Issue Mapping

| Issue | Role |
| --- | --- |
| #77 | Full-chain acceptance epic and final gate. |
| #76, #137, #138, #139 | ERP prerequisite for finance and commercial chains. |
| #75, #136 | WMS prerequisite for warehouse chains. |
| #74, #135 | MES prerequisite for manufacturing and production-to-cost chains. |
| #129, #130 | Equipment reliability prerequisite for equipment-to-maintenance chain. |

## Acceptance

1. `backend/tests/Nerv.IIP.Business.Acceptance.Tests` is in `backend/Nerv.IIP.sln`.
2. Each of the seven chains has at least one focused test.
3. `scripts/verify-business-full-chain-acceptance.ps1` runs all prerequisites and the acceptance test project.
4. Readiness docs and README point to the new verify script.
5. #77 can be closed only after the verify script passes in the target local profile.
