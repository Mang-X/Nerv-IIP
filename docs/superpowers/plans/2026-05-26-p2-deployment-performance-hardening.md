# P2 Deployment Rehearsal and Performance Thresholding Implementation Plan

**Goal:** Complete P2 second-step hardening by turning deployment artifacts and performance baselines into explicit opt-in release gates.

**Architecture:** Keep default local verification lightweight. Deployment rehearsal is a separate governed script that starts a disposable Compose project only when a profile is selected. Performance thresholding remains configurable per run and writes JSONL/summary output for CI or release evidence.

**Implementation status (2026-05-26):** This slice adds the initial release rehearsal entrypoint, machine-readable performance metrics, configurable elapsed-time thresholds, and updates readiness/deployment/script documentation.

## Tasks

- [x] Add machine-readable performance metrics output from `Nerv.IIP.Business.Performance.Tests`.
- [x] Add configurable global and per-scenario elapsed-time thresholds to `scripts/verify-business-performance-baseline.ps1`.
- [x] Add `scripts/verify-production-release-rehearsal.ps1` with explicit `dependencies` and `platform-smoke` profiles.
- [x] Ensure Notification exposes `/health` and can run Development-only auto-migration smoke under PostgreSQL profile.
- [x] Update deployment/readiness/script governance documentation.

## Validation

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Performance.Tests/Nerv.IIP.Business.Performance.Tests.csproj --no-restore --filter FullyQualifiedName~PerformanceMetricTests
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationStartupTests
pwsh scripts/check-script-governance.ps1
pwsh scripts/verify-production-deployment-artifacts.ps1 -SkipDockerComposeConfig
git diff --check
```
