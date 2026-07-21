# MAN-571 Real Material Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the leader-demo raw material through the real public ERP -> WMS -> Inventory chain so MES release succeeds only when exact run-scoped inventory is available.

**Architecture:** Reuse all existing public facades and event contracts. Add WMS same-key replay behavior at command-handler boundaries, make the existing Approval -> ERP and WMS -> ERP consumers own their persistence boundary, then replace the scenario's direct Inventory seed with run-scoped procurement, approval, receiving, putaway, posting, and exact availability observation.

**Tech Stack:** .NET 10, EF Core, FastEndpoints, Vue workspace Vitest, Playwright, Aspire managed full-stack runner.

---

### Task 1: Make WMS receiving commands replay-safe

**Files:**
- Modify: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsInventoryBoundaryTests.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/WmsCommands.cs`

- [x] Add failing handler tests that repeat `CreateInboundOrderCommand`, `CreatePutawayTaskCommand`, and `CompleteInboundOrderCommand` and assert the same ids plus one persisted row per business fact.
- [x] Run the focused WMS tests and verify they fail because the current handlers attempt duplicate creation or reject completed inbound replay.
- [x] Query existing tenant-scoped inbound/task/movement-request facts before creating new ones and return them for the same stable business keys.
- [x] Rerun the focused WMS tests and the full WMS test project.

### Task 2: Replace the direct Inventory prerequisite with public procurement and receiving

**Files:**
- Modify: `frontend/apps/business-console/src/leaderDemoMainChain.contract.test.ts`
- Modify: `frontend/apps/business-console/e2e/leader-demo-main-chain.spec.ts`

- [x] Add failing source-contract tests asserting no direct `/inventory/movements` call, exact public approval/ERP/WMS/Inventory operations, stable run-scoped keys, deliberate replay, and availability observation before Planning acceptance.
- [x] Run the single Vitest contract file and verify the new assertions fail against the old scenario.
- [x] Capture authenticated principal id/type, create the supplier and approval template, approve the purchase order, create/replay WMS inbound and putaway facts, complete/replay receiving, and poll ERP receipt plus exact Inventory availability.
- [x] Keep the MES release and shortage guard unchanged; attach the public supply facts to the successful schedule evidence.
- [x] Rerun the single contract file, then Business Console test, typecheck, and build gates.

### Task 3: Verify the governed real stack and deliver the PR

**Files:**
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs` only if evidence proves the ERP-to-Approval bridge is not wired to the managed endpoint.
- Modify: ERP Approval/WMS integration handlers and reliability tests only where managed PostgreSQL/CAP evidence proves successful consumers do not persist their projections.

- [x] Run targeted MES material-readiness tests to confirm shortage behavior remains covered.
- [x] Run repository checks proportional to the changed backend/frontend areas and `git diff --check`.
- [x] Run `.\\nerv.ps1 fullstack run -Scenario leader-demo-main-chain`; inspect the run evidence, confirm the next earliest business gap, and verify cleanup reports no remaining resources or cleanup errors.
- [x] Inspect the final diff for #1035 / MAN-571 scope only and prepare the ready-PR docs-impact and facade-coverage declarations.
