# Business Wave 1 Agent Session Design

## Context

The business issue roadmap has been reorganized so epics stay broad and execution work happens in child issues. Wave 1 is the first parallel development wave after that cleanup. Its purpose is to unlock downstream planning, warehouse, ERP and full-chain work without creating shared-file merge pressure.

This design covers the first five execution sessions:

1. #127 ProductEngineering gap completion.
2. #131 Inventory MVP.
3. #132 Quality inspection MVP.
4. #135 MES CleanDDD persistence.
5. #140 Business service registration, verify script pattern and readiness tracking.

## Source Facts

As of 2026-05-23:

1. BusinessMasterData is the Layer 0 reference source and has Domain, Infrastructure, Web, migrations, tests, realignment APIs and a verify script.
2. ProductEngineering has Domain, Infrastructure, Web, migration and tests, but current scope is mainly ProductionVersion.
3. Quality has Domain, Infrastructure, Web, migration and tests, but current scope is mainly NonconformanceReport.
4. MES has only a Web project and Web tests with in-memory scheduling, rush order and reschedule behavior.
5. Inventory has no service directory.
6. Business services are not registered in the platform AppHost.
7. Only `scripts/verify-business-master-data-realignment.ps1` exists for business-specific verification.

## Goals

1. Give each Wave 1 agent a self-contained implementation plan.
2. Make ProductEngineering and Inventory APIs stable enough for DemandPlanning, WMS and ERP follow-up sessions.
3. Extend Quality without regressing existing NCR behavior.
4. Move MES from in-memory Web state to CleanDDD Domain and Infrastructure while preserving current endpoint behavior.
5. Keep shared integration edits in #140 so implementation sessions can run in parallel with low merge conflict risk.

## Non-Goals

1. Do not start DemandPlanning #128 in Wave 1.
2. Do not start WMS #136 or ERP #137 to #139 until ProductEngineering and Inventory contracts are stable.
3. Do not implement BarcodeLabel #133 or BusinessApproval #134 in this first documentation batch.
4. Do not include Gantt/RFC #78.
5. Do not put business rules in PlatformGateway, IAM, AppHub or Ops.

## Session Boundaries

| Session | Issue | Owns | Must Not Own |
| --- | --- | --- | --- |
| PE-GAP | #127 | ProductEngineering engineering documents, items, EBOM, MBOM, routing, ECO/ECN and release events. | Inventory, MES work orders, MRP calculation, FileStorage internals. |
| INV-MVP | #131 | Inventory stock locations, ledger, movements, availability and counts. | WMS execution, ERP valuation, MES material issue execution, cross-schema foreign keys. |
| QI-MVP | #132 | Quality inspection plans and inspection records, plus inspection result events. | Inventory mutation, WMS task status, ERP purchase receipt state, MES operation state. |
| MES-PERSIST | #135 | MES Domain/Infrastructure persistence and durable work order/schedule/report facts. | ProductEngineering version authoring, Inventory balance, WMS inbound execution. |
| BIZ-INTEG | #140 | Shared solution/AppHost registration, verify script pattern, readiness and documentation updates. | Domain feature scope owned by service sessions. |

## Shared File Policy

Implementation sessions should avoid shared files unless the plan explicitly says otherwise. Shared files include:

1. `backend/Nerv.IIP.sln`
2. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
3. `docs/architecture/authorization-matrix.md`
4. `docs/architecture/database-schema-catalog.md`
5. `docs/architecture/implementation-readiness.md`
6. `README.md`
7. `scripts/verify-business-*.ps1`

When a service session needs a shared change, it should record the exact requested addition in its PR summary under `Shared Changes Needed`. The #140 session owns applying those additions after service work is merged or ready to integrate.

## Merge Gates

Each service session must provide:

1. Focused domain tests for aggregate invariants.
2. Focused Web tests for FastEndpoints routes, authorization expectations, request validation and stable operation IDs.
3. PostgreSQL migration and schema convention tests for persisted services.
4. Integration event converter tests when the service publishes events.
5. A list of permissions to register in IAM seed and `authorization-matrix.md`.
6. A list of AppHost service registration facts for #140.

The #140 session must provide:

1. Shared solution entries for all merged Wave 1 service projects.
2. AppHost registration for services whose Web projects compile.
3. Root verify scripts using `scripts/lib/ScriptAutomation.ps1` helpers.
4. Readiness documentation updates after the verification commands are known.

## Dependency Rules

1. #127 and #131 are the highest priority sessions because #128, #136 and #137 depend on their contracts.
2. #132 can run independently because it extends existing Quality NCR scope and only emits results.
3. #135 can run independently if it preserves current MES API behavior and uses ProductEngineering/Inventory references as IDs until real integration is available.
4. #140 should start after at least one service session has a ready branch, but it can prepare the verify pattern immediately.

## Acceptance

Wave 1 documentation is complete when:

1. #127, #131, #132, #135 and #140 each have a dedicated session plan.
2. Inventory and Quality inspection have explicit specs because they define new domain facts.
3. ProductEngineering and MES have delta plans that start from current code facts instead of old from-scratch plans.
4. The plans identify shared-file coordination rules and verification commands.
5. `implementation-readiness.md` points future agents to the Wave 1 handoff documents.
