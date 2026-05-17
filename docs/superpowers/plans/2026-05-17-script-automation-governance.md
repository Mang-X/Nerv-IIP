# Script Automation Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans when splitting this plan across workers. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn ADR 0010 into an executable script governance baseline: documentation, shared PowerShell helper, static gate, fixtures, and first high-risk verification script migration.

**Architecture:** Keep ADR 0010 focused on durable decision boundaries. Put operating rules in `docs/architecture/script-automation-governance.md`. Put reusable PowerShell execution primitives in `scripts/lib/ScriptAutomation.ps1`. Put parser/AST checks in `scripts/check-script-governance.ps1`, with explicit legacy exemptions while existing scripts are migrated.

**Tech Stack:** PowerShell 7, .NET 10, Docker Compose, pnpm, Git, local `artifacts/script-logs/**`.

---

## Completion Record

This plan starts from commit `eef40a8 fix: harden iam persistent auth review gaps` on branch `codex/iam-persistent-auth-foundation`.

Known handoff note: `skills-lock.json` is dirty before this plan begins, with no text diff reported in prior audits. Do not stage or modify it unless the user explicitly asks.

## Boundaries

1. Do not rewrite every legacy script in one pass.
2. Do not convert local `verify` scripts into customer `release-install` scripts.
3. Do not add CI provider-specific files in this pass.
4. Do not change business code unless a script migration exposes a real test break.
5. Do not stage unrelated `skills-lock.json` changes.

## File Structure Map

```text
docs/adr/
  0010-automation-script-trusted-execution-governance.md

docs/architecture/
  script-automation-governance.md
  deployment-baseline.md
  database-release-runbook.md
  implementation-readiness.md
  repo-layout.md
  api-contract-and-codegen.md

scripts/
  lib/ScriptAutomation.ps1
  check-script-governance.ps1
  tests/check-script-governance.Tests.ps1
  tests/fixtures/script-governance/*.ps1
  verify-iam-persistent-auth-foundation.ps1
```

## Task 1: Freeze Documentation

- [x] Add ADR 0010 for script trusted execution governance.
- [x] Add architecture-level script automation governance rules and migration matrix.
- [x] Cross-reference ADR 0010 from deployment, database release, repo layout, API generation, implementation readiness and README.

## Task 2: Add Failing Gate Tests

- [x] Add fixture scripts for allowed helper usage, missing helper, direct `dotnet`, direct `Start-Job`, and dynamic invocation.
- [x] Add a local PowerShell test harness that runs `scripts/check-script-governance.ps1 -Path <fixture>` and asserts pass/fail cases.
- [x] Run the harness before implementing the gate to confirm it fails for the expected missing command.

## Task 3: Implement Shared Helper And Static Gate

- [x] Add `scripts/lib/ScriptAutomation.ps1` with timeout native command execution, command wrappers, process tree cleanup, scoped environment variables and diagnostic redaction.
- [x] Add `scripts/check-script-governance.ps1` using PowerShell parser/AST checks, required governance header, helper dot-source detection and explicit legacy exemptions.
- [x] Run fixture tests and `pwsh scripts/check-script-governance.ps1`.

## Task 4: Migrate IAM Verification Script

- [x] Add `Script-Governance` metadata to `scripts/verify-iam-persistent-auth-foundation.ps1`.
- [x] Replace direct `dotnet`, `docker`, `pnpm` or nested `pwsh` calls with helper wrappers.
- [x] Ensure foreground native commands have timeout/PID/logging through helper; this IAM script does not start background service processes directly.
- [x] Rerun `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`.

## Task 5: Final Verification

- [x] Run script governance tests.
- [x] Run script governance gate.
- [x] Run migrated IAM verification script.
- [ ] Run `git diff --check`.
- [ ] Commit docs and script governance implementation in focused commits, leaving unrelated `skills-lock.json` untouched.
