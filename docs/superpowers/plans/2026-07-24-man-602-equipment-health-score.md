# MAN-602 Equipment Health Score Demo Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the #1087 five-rule equipment-health score, two-hop facade, and polling device-detail card without absorbing #1055 post-demo scope.

**Architecture:** IndustrialTelemetry calculates health on demand from its existing alarm-rule, raw-telemetry, device-state, and alarm-event facts through a pure scoring policy. BusinessGateway proxies the scoped read model, generated client artifacts carry the contract, and Business Console renders a focused NvUI card with five-second official auto-refetch. Historical insufficiency remains an explicit non-risk `accumulating` state.

**Tech Stack:** .NET 10, EF Core, FastEndpoints, MediatR, xUnit, BusinessGateway, OpenAPI/Hey API, Vue 3, Pinia Colada, NvUI, Vitest.

---

### Task 1: Pure five-rule scoring policy

**Files:**
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/EquipmentHealth/EquipmentHealthScoringPolicy.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/EquipmentHealthScoringPolicyTests.cs`

- [ ] **Step 1: Write failing policy tests.** Cover threshold high/low proximity boundaries, runtime-hours boundary, active warning/critical and repeated/cleared alarm semantics, sustained-exceedance boundary, trend-growth boundary, both historical insufficient-data branches, score clamping, level boundaries, source evidence, and freshness states.
- [ ] **Step 2: Run the focused domain test and confirm RED.** Run `dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj --filter "FullyQualifiedName~EquipmentHealthScoringPolicyTests" --verbosity minimal`; failure must be caused by missing policy types/behavior.
- [ ] **Step 3: Implement the minimal pure policy.** Add immutable observation/result records, the exact thresholds/penalties from the design, one evaluation per rule, triggered-only `RiskFactors`, deterministic ordering, score/level mapping, and freshness derivation.
- [ ] **Step 4: Run the focused test and confirm GREEN.** Re-run the exact command and require zero failures.
- [ ] **Step 5: Commit.** Commit Task 1 only as `feat(iiot): add explainable health scoring policy`.

### Task 2: IndustrialTelemetry scoped health query and endpoint

**Files:**
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/EquipmentHealthQueries.cs`
- Modify: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Endpoints/Iiot/IndustrialTelemetryEndpoints.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/EquipmentHealthQueryTests.cs`
- Modify: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointContractTests.cs`

- [ ] **Step 1: Write failing query/HTTP tests.** Seed two tenants and devices, enabled/disabled alarm rules, current/historical raw samples, device states, and alarm lifecycles. Require exact scope isolation, five evaluations, traceable source labels, alarm raise/clear score movement, history accumulation, and authorized endpoint contract metadata.
- [ ] **Step 2: Run the focused web tests and confirm RED.** Run the Web.Tests project filtered to `EquipmentHealthQueryTests|IndustrialTelemetryEndpointContractTests` and confirm missing query/route behavior.
- [ ] **Step 3: Implement the fact loader and endpoint.** Query only the requested organization/environment/device, load trailing-24-hour facts plus the runtime carry-in state, reuse `QueryRuntimeHoursQueryHandler`, map enabled alarm rules/raw samples/alarm events to the pure policy, inject `TimeProvider`, and return a stable DTO.
- [ ] **Step 4: Register the endpoint contract.** Add `GET /api/business/v1/iiot/devices/{deviceAssetId}/health`, operation `getBusinessIiotEquipmentHealth`, `TelemetryRead`, and internal-service authorization.
- [ ] **Step 5: Run the focused tests and confirm GREEN.** Require zero failures and no new warnings.
- [ ] **Step 6: Commit.** Commit Task 2 only as `feat(iiot): expose equipment health read model`.

### Task 3: BusinessGateway facade and generated contract

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleTelemetryModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Equipment/BusinessConsoleEquipmentEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Create or modify the focused Gateway equipment proxy test selected from existing test structure.
- Modify: `docs/architecture/facade-coverage-matrix.json`
- Refresh through generators: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Refresh through generators: `frontend/packages/api-client/src/generated/business-console/*`
- Modify: `frontend/packages/api-client/src/business-console.ts`

- [ ] **Step 1: Write failing Gateway tests.** Require telemetry-read permission, resource-scoped authorization, exact organization/environment/device forwarding, downstream DTO mapping, operation ID `getBusinessConsoleEquipmentDeviceHealth`, and OpenAPI visibility.
- [ ] **Step 2: Run focused Gateway tests and confirm RED.** Filter the BusinessGateway Web.Tests project to the new health cases plus authorization/OpenAPI cases.
- [ ] **Step 3: Implement the facade.** Add the downstream client method/DTOs and `GET /api/business-console/v1/equipment/devices/{deviceAssetId}/health` endpoint using the existing authorized equipment proxy base.
- [ ] **Step 4: Register facade coverage.** Mark the service operation `exposed` with the exact Gateway operation and no unrelated matrix changes.
- [ ] **Step 5: Export and regenerate.** Run the governed OpenAPI export path followed by `pnpm -C frontend generate:api`; inspect the diff and add stable operation/DTO exports in `business-console.ts`.
- [ ] **Step 6: Verify GREEN.** Run focused Gateway tests, `dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj`, and generated-client type checks.
- [ ] **Step 7: Commit.** Commit Task 3 only as `feat(gateway): expose equipment health score`.

### Task 4: Business Console health card and product documentation

**Files:**
- Modify: `frontend/apps/business-console/src/composables/useBusinessTelemetry.ts`
- Create: `frontend/apps/business-console/src/components/equipment/EquipmentHealthCard.vue`
- Create: `frontend/apps/business-console/src/components/equipment/EquipmentHealthCard.test.ts`
- Modify: `frontend/apps/business-console/src/pages/equipment/[deviceAssetId].vue`
- Modify: `frontend/apps/business-console/src/pages/equipment/equipmentPages.test.ts`
- Modify: `frontend/apps/docs/docs/roles/equipment-engineer.md`

- [ ] **Step 1: Write failing component/page tests.** Require Chinese level/freshness labels, score, calculation time, all five rule rows, current/threshold/evidence, `历史数据积累中`, no raw GUIDs, preserved data during polling, five-second auto-refetch configuration, and manual-refresh integration.
- [ ] **Step 2: Run focused Vitest and confirm RED.** Run `pnpm -C frontend --filter @nerv-iip/business-console exec vp test run src/components/equipment/EquipmentHealthCard.test.ts src/pages/equipment/equipmentPages.test.ts`; failures must be missing composable/component behavior.
- [ ] **Step 3: Implement the composable and card.** Use the generated query options, business-context gating, `autoRefetch: 5_000`, and existing NvUI/semantic tokens. Keep display helpers in the component and keep the page integration limited to scope wiring and refresh.
- [ ] **Step 4: Update the equipment-engineer guide.** Describe the health card, five rule meanings, five-second refresh, traceable evidence, and honest historical-accumulation state.
- [ ] **Step 5: Run focused tests and per-file formatting.** Require Vitest GREEN and run `pnpm -C frontend exec vp fmt --check` for every touched frontend/docs file.
- [ ] **Step 6: Commit.** Commit Task 4 only as `feat(console): show equipment health score`.

### Task 5: Final verification and ready PR

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`
- Inspect all changed source, test, OpenAPI, generated, and documentation files.

- [ ] **Step 1: Update readiness with delivered facts only.** Record on-demand five-rule health scoring, fail-closed history accumulation, exposed facade, polling card, and the unresolved #1086 real-scenario dependency. Do not claim the simulator acceptance ran.
- [ ] **Step 2: Run backend gates.** Run `dotnet test backend/Nerv.IIP.sln` and the facade-coverage gate with zero failures/no new warnings.
- [ ] **Step 3: Run frontend gates.** Run `pnpm -C frontend typecheck`, `pnpm -C frontend test`, and `pnpm -C frontend build`; run touched-file `vp fmt --check`.
- [ ] **Step 4: Review the diff against #1087.** Confirm exactly five rules, no cross-schema read, no health persistence/migration, no scheduling integration, no model prediction page, no fake seed/demo injector, scoped source evidence, generated two-hop artifacts, and explicit #1086 dependency.
- [ ] **Step 5: Commit final documentation if needed.** Use `docs(iiot): record equipment health readiness`.
- [ ] **Step 6: Push and create a ready PR.** Use `gh`, include `Fixes #1087`, Linear MAN-602 URL, the `exposed` facade declaration, product-doc impact, exact test evidence, and the unexecuted #1086-dependent real scenario. Do not merge.
- [ ] **Step 7: Verify PR state.** Confirm `isDraft=false`, base `main`, head branch, mergeability/check state, linked issue, and then wait for user review.
