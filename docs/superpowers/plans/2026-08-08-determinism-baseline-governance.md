# Determinism Baseline Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close GitHub issues #1487 and #1488 by making expiring debt genuinely time-bounded and making every permanent baseline row require checker-owned capacity.

**Architecture:** Keep the checker offline and deterministic. Schema 3 makes an expiring row declare when and under which issue it was registered, then enforces a 45-day maximum and a different long-lived owner; the permanent allowlist becomes a checker-owned `path=pattern=maxRows` contract so baseline-only growth fails.

**Tech Stack:** PowerShell 7, JSON baseline fixtures, governed script contract tests, Markdown architecture documentation.

## Global Constraints

- Implement #1487 and #1488 in this one branch and one PR, but keep one implementation commit and one independent review gate per issue.
- Implementers and reviewers are separate agents; reviewers must not modify code.
- Preserve the checker's offline, read-only operation; do not add GitHub or Linear network calls.
- `expiring-debt` must retain a tracked `ownerIssue`, an explicit exit condition, and expiry hard failure.
- `permanent` remains limited to audited primitive implementations and their own tests; baseline data cannot enlarge permanent capacity.
- Script changes must pass `scripts/tests/check-backend-test-determinism.Tests.ps1` and `scripts/check-script-governance.ps1`.
- No business-service HTTP endpoint, OpenAPI snapshot, generated client, database schema, or frontend product documentation changes.
- Product docs impact statement: `文档：无影响`.

---

### Task 1: #1487 Bound Expiring Debt and Reject Self-Guarantee

**Files:**
- Modify: `scripts/check-backend-test-determinism.ps1`
- Modify: `scripts/tests/check-backend-test-determinism.Tests.ps1`
- Modify: `scripts/tests/fixtures/backend-test-determinism/valid-baseline.json`
- Modify: `backend/test-determinism-baseline.json`
- Modify: `docs/architecture/backend-test-determinism.md`
- Modify: `docs/architecture/script-automation-governance.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify if its baseline contract is asserted there: `scripts/tests/check-script-governance.Tests.ps1`

**Interfaces:**
- Consumes: existing schema 2 `expiring-debt` fields `ownerIssue`, `exitCondition`, and `expiresOn`.
- Produces: schema 3 expiring rows with two additional required string fields, `registeredByIssue` and `registeredOn`; `registeredByIssue` accepts `MAN-\d+` or `#\d+`, must differ from `ownerIssue`, and `registeredOn` uses `yyyy-MM-dd`.
- Produces: an offline 45-day inclusive maximum lifetime: `expiresOn` must be no earlier than `registeredOn`, no later than `registeredOn + 45 days`, and `registeredOn` must not be in the future. Existing expiry-before-today rejection remains.

- [ ] **Step 1: Add adversarial RED cases**

Add fixture-contract cases that invoke the real checker and assert nonzero exit plus targeted diagnostics for: `ownerIssue == registeredByIssue`; missing `registeredByIssue`; malformed `registeredByIssue`; missing `registeredOn`; malformed `registeredOn`; future `registeredOn`; `expiresOn` earlier than `registeredOn`; and `expiresOn` 46 days after `registeredOn`. Add the passing boundary control where expiry is exactly 45 days after registration.

- [ ] **Step 2: Run the checker fixture suite and preserve RED evidence**

Run `pwsh -NoProfile -File scripts/tests/check-backend-test-determinism.Tests.ps1`. Record that at least the new self-guarantee and 46-day cases fail before implementation for the expected reason: the checker still admits them or does not require the new metadata.

- [ ] **Step 3: Implement schema 3 validation**

Change the schema number to 3, add `registeredByIssue` and `registeredOn` only to the expiring classification, parse both dates with invariant `DateOnly`, and emit distinct actionable errors for each invalid condition. Keep permanent metadata mutually exclusive by treating the new fields as expiring-only fields. Do not query GitHub or Linear.

- [ ] **Step 4: Migrate governed baselines and fixtures**

Set the repository baseline to schema 3. It currently has zero expiring rows, so do not invent debt metadata for permanent rows. Update every generated and static test baseline to schema 3 and give valid expiring fixture rows non-self values, such as `registeredByIssue = '#1487'`, `ownerIssue = 'MAN-662'`, `registeredOn = '2026-08-08'`, and an expiry no later than `2026-09-22`.

- [ ] **Step 5: Update architecture policy**

Document schema 3, the two new fields, self-guarantee rejection, the 45-day cap, inclusive boundary, and offline rationale in all three listed architecture/governance status documents. Replace statements that describe schema 2 as current; retain historical schema 2 discussion as history where useful.

- [ ] **Step 6: Verify and commit**

Run `pwsh -NoProfile -File scripts/tests/check-backend-test-determinism.Tests.ps1`, `pwsh -NoProfile -File scripts/check-backend-test-determinism.ps1`, and `pwsh -NoProfile -File scripts/check-script-governance.ps1`. Commit only #1487 and its required documentation/tests with a message that references `#1487`.

---

### Task 2: #1488 Cap Permanent Rows per Checker-Owned Pair

**Files:**
- Modify: `scripts/check-backend-test-determinism.ps1`
- Modify: `scripts/tests/check-backend-test-determinism.Tests.ps1`
- Modify: `docs/architecture/backend-test-determinism.md`
- Modify: `docs/architecture/script-automation-governance.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify if its parameter contract is asserted there: `scripts/tests/check-script-governance.Tests.ps1`

**Interfaces:**
- Consumes: Task 1 schema 3 validation and its tests.
- Produces: `$PermanentAllowlist` entries in exact `<repo-relative-path>=<pattern>=<maxRows>` form.
- Produces: default capacities `GlobalTestStateScopeTests.cs=StaticSetter=12`, `GlobalTestStateScope.cs=StaticSetter=9`, and `BoundedObservationWindow.cs=Task.Delay=1`.
- Produces: exact ordinal path/pattern matching and a positive integer `maxRows`; the cap counts valid permanent baseline rows for the pair, not source occurrences and not `occurrenceCount`.

- [ ] **Step 1: Add the capacity-growth RED case**

Create a generated source with two distinct `Thread.Sleep` lines and two matching permanent baseline rows. With an allowlist entry ending in `=1`, assert exit 1 and a diagnostic naming the pair, actual row count 2, and maximum 1. Add the control using the same data and a cap of 2, which must pass.

- [ ] **Step 2: Add allowlist grammar RED cases**

Assert rejection for the legacy `<path>=<pattern>` form, a zero cap, a negative cap, a non-integer cap, an empty path, an empty pattern, an empty cap, an unsupported pattern, and duplicate entries for the same exact path/pattern pair.

- [ ] **Step 3: Run the checker fixture suite and preserve RED evidence**

Run `pwsh -NoProfile -File scripts/tests/check-backend-test-determinism.Tests.ps1`. Record that the two-row case passes under the old pair-only allowlist before implementation, reproducing #1488.

- [ ] **Step 4: Implement checker-owned capacity**

Parse each allowlist entry from the right into path, pattern, and positive integer maximum. Store one exact ordinal capacity per path/pattern pair, reject duplicate pair declarations, and after row validation reject any pair whose valid permanent baseline row count exceeds its maximum. Update comments and diagnostics to the new grammar. Do not require equality: removing a permanent row must remain legal without first lowering the cap.

- [ ] **Step 5: Update policy documents**

Replace all current claims that `path=pattern` alone prevents baseline self-exemption. Document `path=pattern=maxRows`, the three current capacities, baseline-row counting semantics, and the rule that increasing capacity requires changing the governed checker. Ensure historical wording is clearly historical rather than current policy.

- [ ] **Step 6: Verify and commit**

Run `pwsh -NoProfile -File scripts/tests/check-backend-test-determinism.Tests.ps1`, `pwsh -NoProfile -File scripts/check-backend-test-determinism.ps1`, and `pwsh -NoProfile -File scripts/check-script-governance.ps1`. Commit only #1488 changes with a message that references `#1488`.
