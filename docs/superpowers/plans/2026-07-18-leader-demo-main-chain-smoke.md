# Leader Demo Main-Chain Smoke Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reproduce MAN-524 / #965 against the disposable real full stack, record public-HTTP evidence for every sales-to-delivery hop, and turn each confirmed gap into a deduplicated responsibility issue without changing business implementations.

**Architecture:** Extend the existing `nerv.ps1 fullstack run` scenario router with a dedicated `leader-demo-main-chain` scenario. The scenario requires Redis messaging, uses the session-scoped admin login and BusinessGateway public facade only, writes redacted request/response and hop summaries beneath the session artifact directory, then relies on the existing managed-run `finally` path to remove all session-owned processes, containers, and volumes.

**Tech Stack:** PowerShell 7, Aspire full-stack sessions, PostgreSQL 18, Redis 8 / CAP Redis Streams, BusinessGateway HTTP facade, Pester-style repository script tests, Markdown evidence.

---

### Task 1: Freeze the scenario and evidence contract

**Files:**
- Create: `scripts/tests/leader-demo-main-chain-smoke.Tests.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`

- [ ] **Step 1: Add a failing parser/contract test**

Assert that `nerv.ps1` and `scripts/fullstack-session.ps1` accept `leader-demo-main-chain`, that the new scenario refuses a non-Redis messaging profile, and that its result contains one row for every MAN-518 node with source object, downstream object, stable key, automation mode, evidence path, conclusion, and demo wording.

- [ ] **Step 2: Run the focused tests and capture RED**

Run: `pwsh scripts/tests/leader-demo-main-chain-smoke.Tests.ps1`

Expected: FAIL because the scenario and evidence helper do not exist yet.

### Task 2: Add the managed full-stack scenario

**Files:**
- Modify: `nerv.ps1`
- Modify: `scripts/fullstack-session.ps1`
- Create: `scripts/lib/LeaderDemoMainChainSmoke.ps1`

- [ ] **Step 1: Route the dedicated scenario**

Add `leader-demo-main-chain` to both validated scenario sets, dot-source the focused library, and dispatch it inside the existing `Invoke-NervManagedFullStackRun` scenario callback. Do not add a second AppHost launcher or an interactive `fullstack start` path.

- [ ] **Step 2: Enforce the runtime profile**

Fail before business calls unless `Messaging__Provider=Redis`; record the manifest PostgreSQL and Redis resources plus the selected messaging profile in `environment.json` without secrets.

- [ ] **Step 3: Authenticate and call only public facades**

Login through PlatformGateway, call BusinessGateway with the bearer token, and redact authorization/password values. Use one run-scoped sales order reference for every manual continuation; never query service databases or reuse unrelated seed records.

- [ ] **Step 4: Record all hops even after a gap**

For each MAN-518 node, record the attempted public request or query, response status/body path, stable provenance key, automatic/manual classification, factual conclusion, responsible issue when known, and demo wording. A business gap is evidence and does not abort later independent probes; an environment/authentication failure aborts the scenario as incomplete.

- [ ] **Step 5: Verify GREEN**

Run: `pwsh scripts/tests/leader-demo-main-chain-smoke.Tests.ps1`

Expected: PASS.

### Task 3: Run the real stack and collect evidence

**Files:**
- Create at runtime only: `artifacts/fullstack/<session-id>/leader-demo-main-chain/**`

- [ ] **Step 1: Run the governed scenario**

Run: `$env:Messaging__Provider='Redis'; .\nerv.ps1 fullstack run -Scenario leader-demo-main-chain`

Expected: the scenario uses PostgreSQL and Redis, writes redacted evidence, and always stops its exact session. A confirmed chain gap may produce an evidence-complete result; startup/auth/profile failures produce an incomplete result and require a Draft PR.

- [ ] **Step 2: Verify cleanup**

Run: `.\nerv.ps1 fullstack list`

Expected: the run's session is `Stopped`; no interactive session remains.

### Task 4: Publish the reproducible evidence document

**Files:**
- Create: `docs/architecture/leader-demo-main-chain-smoke.md`

- [ ] **Step 1: Write the hop matrix**

Document the exact command, commit, profile, run/session evidence path, request/response paths, same-order correlation rules, automatic/manual verdict, stable keys, gaps, demo wording, side effects, and cleanup.

- [ ] **Step 2: Separate code facts from runtime facts**

Mark MAN-517/#958 as the existing owner of SalesOrder to DemandSource. For every other runtime-confirmed break, link a deduplicated GitHub/Linear issue and classify it `demo:blocker` or `demo:defer`. Do not claim a hop exists from endpoints or contracts alone.

### Task 5: Close governance and collaboration loops

**Files:**
- Modify only if required by the existing docs index: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Run focused gates**

Run:

```powershell
pwsh scripts/tests/leader-demo-main-chain-smoke.Tests.ps1
pwsh scripts/tests/fullstack-session-runtime.Tests.ps1
pwsh scripts/check-script-governance.ps1
git diff --check
```

Expected: all pass.

- [ ] **Step 2: Comment on MAN-518**

Post the verified stable-node list, unlinked-node list, issue links, commit/session evidence reference, and first-version scope wording to MAN-518.

- [ ] **Step 3: Commit, push, and open the PR**

Use a title beginning `MAN-524 #965`. Use `Fixes #965` only when real-stack evidence is complete; otherwise use `Refs #965` and create a Draft PR. Include Fix, Tests, Risk, OpenAPI or schema impact, and product-document impact.

## Self-Review Checklist

1. No file under `backend/services/**`, `backend/common/Contracts/**`, Gateway business implementation, OpenAPI, generated client, or frontend feature code changed.
2. Every hop has a factual result and evidence path; no database read is a primary assertion.
3. All continuation objects use the same run-scoped sales-order lineage and are explicitly labeled manual when no automatic bridge exists.
4. Redis messaging and PostgreSQL are proven from the managed full-stack run rather than inferred from defaults.
5. Every new gap issue was searched online before creation and is linked to MAN-518.
6. The exact full-stack session is stopped and no `fullstack start` session remains.
