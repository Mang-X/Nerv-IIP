# MAN-578 Run-scoped Quality Inspection Plan Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish a real, managed, run-scoped active Quality operation inspection plan before the leader-demo MES production report so the existing consumer creates exactly one matching inspection task.

**Architecture:** Expose the already-existing Quality create/activate plan endpoints through authenticated BusinessGateway facades, updating the two-hop governance registry and generated contract. The Playwright main-chain scenario uses those public facades to create and activate a plan bound to the run's organization, environment, finished SKU, work center, and `operation-task` document type before reporting production; the Quality consumer remains unchanged and fail closed.

**Tech Stack:** .NET 10, FastEndpoints, HttpClient, xUnit, Playwright/TypeScript, PowerShell governed scripts, OpenAPI + Hey API.

---

### Task 1: Lock the BusinessGateway facade contract with failing tests

**Files:**
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`

- [ ] **Step 1: Add proxy tests for exact downstream wire shapes**

Add tests that call `HttpBusinessQualityClient.CreateInspectionPlanAsync` with an operation plan containing one attribute characteristic and assert `POST /api/business/v1/quality/inspection-plans`, exact org/env/category/SKU/work-center/document-type payload, and response `inspectionPlanId`. Add a second test for `ActivateInspectionPlanAsync` asserting `POST /api/business/v1/quality/inspection-plans/{escapedId}/activate` and the route ID in the request body.

- [ ] **Step 2: Add public contract and authorization expectations**

Assert these OpenAPI operations:

```csharp
AssertOperationId(paths, "/api/business-console/v1/quality/inspection-plans", "post", "createBusinessConsoleQualityInspectionPlan");
AssertOperationId(paths, "/api/business-console/v1/quality/inspection-plans/{inspectionPlanId}/activate", "post", "activateBusinessConsoleQualityInspectionPlan");
```

Add the two routes to the authorization matrix with `BusinessGatewayPermissions.QualityInspectionPlansManage`.

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter "FullyQualifiedName~Quality|FullyQualifiedName~OpenApi|FullyQualifiedName~Authorization" --no-restore --nologo
```

Expected: compile/test failures because the public request/response models, client methods, endpoints, permission constant, and OpenAPI operations do not exist.

### Task 2: Implement the narrow create/activate two-hop facade

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Quality/BusinessConsoleQualityEndpoints.cs`

- [ ] **Step 1: Add public DTOs and the existing permission code**

Add `QualityInspectionPlansManage = "business.quality.inspection-plans.manage"` and typed DTOs for plan creation, characteristics, sampling plan, the returned string ID, and activation. Keep `InspectionPlanId` as a route parameter for activation and org/env as explicit body/query scope.

- [ ] **Step 2: Add client methods with exact downstream mapping**

Extend `IBusinessQualityClient` and `HttpBusinessQualityClient` with `CreateInspectionPlanAsync` and `ActivateInspectionPlanAsync`. Create maps directly to `/api/business/v1/quality/inspection-plans`; activate maps to the escaped downstream route and includes the resolved route ID in the request body. Do not change generic response parsing or Quality consumer behavior.

- [ ] **Step 3: Add authenticated FastEndpoints facades**

Add `CreateBusinessConsoleQualityInspectionPlanEndpoint` and `ActivateBusinessConsoleQualityInspectionPlanEndpoint`. Both use `BusinessGatewayPermissions.QualityInspectionPlansManage`; activation derives `ResourceId` and the downstream ID from the route, preserving org/env authorization scope.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the command from Task 1. Expected: all selected tests pass.

### Task 3: Prove the leader-demo prerequisite is run scoped and precedes reporting

**Files:**
- Modify: `frontend/apps/business-console/e2e/leader-demo-main-chain.spec.ts`
- Modify: `scripts/tests/leader-demo.Tests.ps1`

- [ ] **Step 1: Add a static scenario contract test and verify RED**

Extend `leader-demo.Tests.ps1` to require the scenario source to call the public create and activate inspection-plan routes and to require these request fields: `category: 'operation'`, `skuCode: finishedSku`, `workCenterId: workCenterCode`, and `documentType: 'operation-task'`. Require plan setup to occur before the production-report call and reject fixed legacy `FG-*` identifiers.

Run:

```powershell
pwsh -NoProfile -File scripts/tests/leader-demo.Tests.ps1
```

Expected: FAIL because the scenario has no run-scoped inspection-plan setup.

- [ ] **Step 2: Create and activate the exact plan through public HTTP**

After run-scoped SKU/work-center setup and before starting/reporting the MES task, call:

```typescript
const inspectionPlan = asRecord(await create('/api/business-console/v1/quality/inspection-plans', {
  organizationId,
  environmentId,
  planCode: `IP-M524-${suffix}`,
  category: 'operation',
  skuCode: finishedSku,
  partnerId: null,
  workCenterId: workCenterCode,
  deviceAssetId: null,
  documentType: 'operation-task',
  characteristics: [{
    characteristicCode: `ATTR-M524-${suffix}`,
    name: 'MAN-524 operation acceptance',
    method: 'visual',
    severity: 'major',
    required: true,
    samplingRule: '100-percent',
    characteristicType: 'attribute',
  }],
}))
const inspectionPlanId = textOf(inspectionPlan.inspectionPlanId).trim()
await create(`/api/business-console/v1/quality/inspection-plans/${encodeURIComponent(inspectionPlanId)}/activate`, {
  organizationId,
  environmentId,
  inspectionPlanId,
})
```

Fail closed if the create response lacks an ID. Keep the existing consumer and match logic untouched.

- [ ] **Step 3: Tighten evidence to the exact work order and uniqueness**

Poll inspection tasks by the run-scoped SKU, select only `sourceDocumentId === workOrderId` and `sourceDocumentLineId === taskId`, then require exactly one match. Record the plan ID, task ID, work order, operation task, SKU, and work center in the public evidence without credentials. This proves the task belongs to this run rather than a similarly named seeded row.

- [ ] **Step 4: Run the script contract test and frontend checks**

Run:

```powershell
pwsh -NoProfile -File scripts/tests/leader-demo.Tests.ps1
pnpm -C frontend exec vp fmt --check apps/business-console/e2e/leader-demo-main-chain.spec.ts
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

Expected: all pass; any repository-wide pre-existing format failure is reported separately.

### Task 4: Update two-hop governance and generated contracts

**Files:**
- Modify: `docs/architecture/facade-coverage-matrix.json`
- Modify: `docs/architecture/facade-coverage-matrix.md`
- Modify: `docs/architecture/api-contract-and-codegen.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Generate: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Generate: `frontend/packages/api-client/src/generated/business-console/**`
- Modify if generated exports require it: `frontend/packages/api-client/src/index.ts`

- [ ] **Step 1: Promote exactly two Quality rows to exposed**

Change create and activate inspection-plan rows from `deferred` to `exposed`, with gateway operation IDs `createBusinessConsoleQualityInspectionPlan` and `activateBusinessConsoleQualityInspectionPlan`. Update the narrative table and API contract operation table.

- [ ] **Step 2: Record readiness truth**

Document that MAN-578 adds no Quality service endpoint or schema; it exposes existing endpoints and uses them to provision the run-scoped operation plan before production reporting. State that consumer envelope validation, idempotency, duplicate guards, and fail-closed plan matching are unchanged.

- [ ] **Step 3: Export OpenAPI and regenerate; never hand-edit generated files**

Run:

```powershell
pwsh -NoProfile -File scripts/export-gateway-openapi.ps1
pnpm -C frontend generate:api
```

Expected: BusinessGateway snapshot and generated client contain the two new operations.

- [ ] **Step 4: Run facade and drift gates**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj --no-restore --nologo
pwsh -NoProfile -File scripts/verify-openapi-client-drift.ps1 -SkipGeneration
pwsh -NoProfile -File scripts/check-script-governance.ps1
```

Expected: all pass.

### Task 5: Verify the real managed stack and hand off a ready PR

**Files:**
- Runtime-only: `artifacts/fullstack/<sessionId>/manifest.json`
- Runtime-only: `artifacts/fullstack/<sessionId>/leader-demo-main-chain-evidence.json`

- [ ] **Step 1: Run backend gates**

Run:

```powershell
dotnet test backend/Nerv.IIP.sln --nologo
```

Expected: all backend tests pass with no new warnings. If environment process pressure causes an MSBuild node failure, rerun serially with node reuse disabled and report the exact distinction.

- [ ] **Step 2: Run the governed real stack**

Run:

```powershell
.\nerv.ps1 fullstack run -Scenario leader-demo-main-chain
```

Expected for the MAN-578 node: the public plan create and activate calls succeed; MES production report returns HTTP 200; `production-report-quality` is `runtime-confirmed`; exactly one task matches the same work order/operation task and run-scoped SKU. A later first failure is recorded as a separate issue and is not fixed here.

- [ ] **Step 3: Inspect cleanup evidence**

Verify the session manifest is `Stopped`, `cleanup.remaining=[]`, `cleanup.errors=[]`, and no Docker container/network/volume carries the session label. Do not claim runtime confirmation if the browser never reached the Quality node.

- [ ] **Step 4: Commit, push, and create a ready PR**

Commit only MAN-578 files, push `codex/man-578-quality-inspection-plan`, and create a non-draft PR containing scope, tests, runtime evidence, risks, facade declarations, schema/OpenAPI/docs impact, and `Fixes #1046`. Do not merge; wait for review.
