# ProductEngineering Release Version Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make MBOM and routing release responses provide identities that production-version creation can consume directly through ProductEngineering and BusinessGateway.

**Architecture:** Introduce a dedicated versioned release result for only MBOM and routing, preserve the existing aggregate `id`, and add canonical `versionId = code:revision`. Mirror that response across BusinessGateway, regenerate the public client, and make the governed leader-demo consume only the returned identities.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, xUnit, BusinessGateway OpenAPI, Hey API TypeScript codegen, Playwright, Aspire-managed full-stack verification.

---

### Task 1: Lock ProductEngineering release-to-production-version behavior

**Files:**
- Modify: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/ProductEngineering/ProductEngineeringReleaseEndpoints.cs`

- [ ] **Step 1: Write the failing contract test**

Add a test that releases an MBOM and routing, asserts returned identities are
`MBOM-CHAIN:A` and `ROUTE-CHAIN:A`, and passes those exact values to
`CreateProductionVersionCommandHandler` against the same in-memory database.

- [ ] **Step 2: Run the focused test and verify RED**

Run:
`dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --filter Release_results_feed_production_version_identity_without_caller_construction`

Expected: FAIL because release commands currently return only `EntityCommandResult.Id`.

- [ ] **Step 3: Implement the minimal versioned command and endpoint result**

Add a `ReleasedEngineeringVersionResult` containing `Id` and `VersionId`, return it from
the MBOM/routing handlers for both new and idempotent paths, and expose a dedicated
`ReleasedEngineeringVersionResponse` from the two endpoints.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2.

Expected: PASS with the returned values accepted by production-version creation.

### Task 2: Preserve version identity through BusinessGateway

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/ProductEngineering/BusinessConsoleProductEngineeringEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`

- [ ] **Step 1: Write the failing facade continuity test**

Extend the ProductEngineering write-client test so the mocked MBOM/routing releases return
distinct `versionId` values, capture them, use them in `CreateProductionVersionAsync`, and
assert the forwarded production-version JSON contains those values verbatim.

- [ ] **Step 2: Run the focused Gateway test and verify RED**

Run:
`dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Product_engineering_http_client_forwards_write_facades_to_product_engineering_routes`

Expected: FAIL because both release methods currently deserialize the generic response with
no `VersionId`.

- [ ] **Step 3: Implement the dedicated Gateway response path**

Add `BusinessConsoleReleasedEngineeringVersionResponse`, change only MBOM/routing client
methods and endpoints to use it, and keep all other engineering writes on
`BusinessConsoleEngineeringEntityResponse`.

- [ ] **Step 4: Run the focused Gateway test and verify GREEN**

Run the command from Step 2.

Expected: PASS and forwarded production-version JSON contains the two release-returned
identities.

### Task 3: Synchronize public contract and governed scenario

**Files:**
- Regenerate: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Regenerate: `frontend/packages/api-client/src/generated/business-console/`
- Modify: `frontend/apps/business-console/e2e/leader-demo-main-chain.spec.ts`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Export BusinessGateway OpenAPI and regenerate the client**

Run the repository's governed OpenAPI export command discovered in
`docs/architecture/api-contract-and-codegen.md`, then run
`pnpm -C frontend generate:api`.

Expected: generated MBOM/routing release response types contain optional TypeScript
`versionId?: string` in accordance with the OpenAPI serializer.

- [ ] **Step 2: Update the leader-demo consumer**

Read `mbom.versionId` and `routing.versionId`, trim both, throw explicit missing-contract
errors when empty, and pass them directly to the production-version request.

- [ ] **Step 3: Update readiness documentation**

Record that ProductEngineering's MBOM/routing release facade now returns stable
revision-qualified version identities suitable for production-version creation.

- [ ] **Step 4: Verify generated and scenario files**

Run targeted format checks for the edited TypeScript/specification files and
`git diff --check`.

Expected: no formatting or whitespace errors.

### Task 4: Run static and real-stack verification

**Files:**
- Inspect: governed full-stack evidence under the session output location reported by `nerv.ps1`
- Update only if needed: GitHub/Linear follow-up issue for the next earliest main-chain breakpoint

- [ ] **Step 1: Run ProductEngineering, Gateway, facade, and full backend tests**

Run the focused tests first, then `dotnet test backend/Nerv.IIP.sln`.

Expected: zero failures and no new warnings.

- [ ] **Step 2: Run frontend gates**

Run `pnpm -C frontend typecheck`, `pnpm -C frontend test`, and
`pnpm -C frontend build`.

Expected: zero failures; pre-existing repository-wide formatting caveats are reported
separately from touched-file checks.

- [ ] **Step 3: Run the governed leader-demo main chain**

Run `./nerv.ps1 fullstack run -Scenario leader-demo-main-chain`.

Expected: the evidence ledger passes the production-version checkpoint. If a later request
fails, identify the earliest new responsibility and register a separate GitHub/Linear issue
without changing its business code in this branch.

- [ ] **Step 4: Verify managed cleanup**

Inspect the same session's evidence JSON, Aspire status, and Docker resources carrying the
session label.

Expected: no session-owned containers, networks, or volumes remain and no AppHost from the
run remains active.

### Task 5: Review and publish the ready PR

**Files:**
- Review: all changes relative to `b3d7ce8230ba10f80a0837a8de3f5e454a8ce848`

- [ ] **Step 1: Review scope and requirements**

Inspect `git diff --stat`, `git diff --check`, generated-code provenance, and every changed
file. Confirm no later main-chain business fix entered the branch.

- [ ] **Step 2: Commit and push**

Stage only the scoped files, commit with `fix(product-engineering): expose released version identities`,
and push `codex/man-564-release-version-contract`.

- [ ] **Step 3: Create a ready PR**

Create a non-draft PR targeting `main`, include `Fixes #1024`, list facade declaration as
`exposed` with no matrix classification change, document API/codegen impact and verification,
and state the product-doc impact accurately.

- [ ] **Step 4: Stop for review**

Report the ready PR URL and live checks state. Do not merge.

