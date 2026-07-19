# MAN-517 Acceptance Retry Observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the MAN-517 acceptance gate retry-aware and preserve enough redacted failure evidence to locate the failing integration hop.

**Architecture:** Keep production CAP defaults intact. Prove retry behavior with a real PostgreSQL/Redis test-only subscriber, and make the acceptance harness collect hop-specific diagnostics before cleanup and upload them unconditionally from CI.

**Tech Stack:** .NET 10, xUnit, DotNetCore.CAP 10.0.1, PostgreSQL 18, Redis 8, PowerShell 7, GitHub Actions.

---

### Task 1: Establish red tests for retry and diagnostics

**Files:**
- Modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/ErpSalesOrderDemandConsumerTests.cs`
- Modify: `scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1`

- [ ] Add a real Redis CAP test subscriber that throws on the first version-2 changed-event delivery, then calls `SalesOrderChangedIntegrationEventHandlerForProjectDemandSource`.
- [ ] Run the test without a shortened `FailedRetryInterval` and record the expected timeout before CAP's default 60-second scan.
- [ ] Add PowerShell assertions requiring last-observation diagnostics, pre-cleanup DB/Redis/log export, and an `if: always()` artifact upload.
- [ ] Run the PowerShell test and record the expected missing-contract failures.

### Task 2: Make CAP retry convergence deterministic in the test profile

**Files:**
- Modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/ErpSalesOrderDemandConsumerTests.cs`

- [ ] Configure only the test host with `FailedRetryInterval = 2`.
- [ ] Assert exactly one injected failure, at least two delivery attempts, durable inbox evidence, and version-2 quantity/status projection.
- [ ] Run the targeted test and the surrounding DemandPlanning test project to green.

### Task 3: Add retry-aware, redacted acceptance diagnostics

**Files:**
- Modify: `scripts/verify-erp-sales-order-demand-planning.ps1`
- Modify: `scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1`

- [ ] Change `Wait-Demand` to retain the last HTTP status/body, exception, and matching-row state, and use a 90-second convergence window.
- [ ] Add best-effort pre-cleanup exporters for service log tails, CAP published/received rows, DemandPlanning processed/DLQ/projection/source rows, and run-relevant Redis stream/group/pending state.
- [ ] Redact the internal token, password-bearing connection strings, and authorization text before writing diagnostics.
- [ ] Re-run the PowerShell test and script-governance gate to green.

### Task 4: Retain diagnostics in GitHub Actions

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1`

- [ ] Add `actions/upload-artifact@v4` after the verification step with `if: always()`, bounded retention, and paths for MAN-517 evidence/diagnostics and managed logs.
- [ ] Re-run the PowerShell contract test to green.

### Task 5: Verify, review, and publish

**Files:**
- Modify if evidence requires: `docs/architecture/sales-order-to-demand-planning.md`
- Modify: `docs/superpowers/specs/2026-07-19-man-517-acceptance-retry-observability-design.md`
- Modify: `docs/superpowers/plans/2026-07-19-man-517-acceptance-retry-observability.md`

- [ ] Run the targeted PowerShell, DemandPlanning, and full-chain tests.
- [ ] Run the actual acceptance script repeatedly, script governance, touched-file formatting, and backend solution tests.
- [ ] Use verification-before-completion, request a code review, address valid findings, and re-run affected checks.
- [ ] Commit, push, and create a ready PR that fixes #981, references #958 and run 29684133999, and documents root cause, fix, tests, risk, OpenAPI/schema impact, and product-doc impact.
