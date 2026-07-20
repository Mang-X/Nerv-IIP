# MAN-524 Public Main-Chain Evidence Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a repeatable, agent-owned acceptance scenario for GitHub #965 / Linear MAN-524 that proves the sales-to-fulfillment main chain through public HTTP, real PostgreSQL, and cross-process Redis messaging, while recording explicit evidence for every hop and making no business-code fixes.

**Architecture:** Extend the isolated `nerv.ps1 fullstack run` harness with a `leader-demo-main-chain` scenario. A dedicated Playwright test signs in through the public console, calls only BusinessGateway public facades, uses one run-scoped sales-order key across ERP, Planning, MES, Scheduling, Quality, Inventory, WMS, and Finance, and writes a redacted evidence ledger under the session artifact directory. Aspire owns all service processes, PostgreSQL databases, Redis transport, ports, secrets, and exact cleanup.

**Tech Stack:** PowerShell 7 governed automation, Aspire 13.4, Docker, .NET 10 services, PostgreSQL, CAP Redis Streams, Playwright/TypeScript.

---

### Task 1: Freeze the evidence contract and scenario routing

**Files:**
- Modify: `nerv.ps1`
- Modify: `scripts/fullstack-session.ps1`
- Modify: `scripts/lib/FullStackSessionRuntime.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`

- [x] Add `leader-demo-main-chain` to the public scenario allow-lists and help text.
- [x] Add deterministic contract tests requiring the dedicated Playwright spec, a session-owned evidence path, and validation of the evidence ledger before success.
- [x] Route the new scenario through the existing managed run lifecycle so success and failure both perform exact session cleanup.
- [x] Wait for all business resources required by the public chain and reject prematurely finished projects.

### Task 2: Implement the public HTTP main-chain probe

**Files:**
- Create: `frontend/apps/business-console/e2e/leader-demo-main-chain.spec.ts`

- [x] Sign in via the public console route and retain credentials only in process memory.
- [x] Create run-scoped master/product/resource prerequisites through public BusinessGateway facades.
- [x] Drive quotation/sales order, Planning demand/MRP/suggestion, MES work order, Scheduling release, production report, Quality/finished-goods receipt, Inventory, delivery/WMS completion, receivable, and voucher through public facades.
- [x] Use one `MAN524-<UTC>` suffix and preserve the same sales-order reference through every automatic and manual transition.
- [x] Poll asynchronous Redis-driven hops using bounded, diagnostic waits; never substitute database reads for business assertions.
- [x] Record one redacted evidence entry per required hop with stable keys, automation mode, public request/response facts, conclusion, demo wording, and owning follow-up issue.
- [x] Mark the known produced-lot source lookup limitation against #972 explicitly instead of repairing it in this change.
- [x] Fail the scenario if a required hop is absent, uses an invalid conclusion, leaks a secret, or cannot be associated with the run-scoped sales order.

### Task 3: Document the governed verification surface

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/script-automation-governance.md`

- [x] Add the new one-shot command and clarify that its JSON ledger is run evidence, not a committed artifact.
- [x] State the public-HTTP-only assertion boundary, real PostgreSQL/Redis prerequisites, and cleanup ownership.

### Task 4: Verify the implementation and run the real stack

**Files:** all files changed above.

- [x] Run the full-stack runtime contract tests and script-governance gate.
- [x] Run touched TypeScript formatting/type checks and the targeted Playwright discovery/config check.
- [x] Run `./nerv.ps1 fullstack run -Scenario leader-demo-main-chain` without bypassing build or infrastructure.
- [x] Inspect the generated ledger and session logs; confirm PostgreSQL/Redis resources participated and the exact session ended `Stopped` with no remaining owned resources.
- [x] Run `git diff --check`, inspect the entire issue-scoped diff, and use superpowers:verification-before-completion with fresh outputs.

### Task 5: Publish evidence and create the review PR

- [x] Post the redacted run summary and per-hop conclusions to GitHub #965 and Linear MAN-524, linking existing follow-up #972 for the deliberate gap.
- [x] Publish the full fifteen-node evidence table in [GitHub #965](https://github.com/Mang-X/Nerv-IIP/issues/965#issuecomment-5019044395); keep #989 as the owner of the public-prerequisite blocker exposed by the real-stack run.
- [x] Commit only the reusable scenario, tests, plan, and documentation; never commit runtime credentials or generated session artifacts.
- [x] Push `codex/man-524-965-real-stack-evidence` and create a ready PR with scope, exact verification command, evidence location, cleanup result, docs impact, and `Fixes #965` only if every required hop has an explicit accepted conclusion.
- [x] Stop after PR creation and wait for review; do not merge.
