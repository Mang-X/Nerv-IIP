# MAN-661 Test Evidence Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a repository-owned, redacted TRX evidence pipeline for backend and Connector Host CI that reports timing/skip/rerun facts and fails only on the three approved evidence-governance violations.

**Architecture:** Keep solution-level `dotnet test` as the test authority and add governed PowerShell collectors around its TRX output. A versioned policy inventories every source/runtime skip, a pure library normalizes TRX into JSONL/JSON/Markdown, a separate generator is the only writer of the committed baseline, and GitHub Actions always collects/uploads evidence without masking the natural test result.

**Tech Stack:** PowerShell 7, `scripts/lib/ScriptAutomation.ps1`, VSTest TRX, xUnit, JSON/JSONL, GitHub Actions, .NET 10.

## Global Constraints

- Evidence schema starts at `schemaVersion: 1`; `lane` is a filesystem-safe open string supporting `<family>` and `<family>-shard-<positive-integer>` without a later shard schema change.
- MAN-661 builds the complete TRX pipeline. MAN-669 may only add lanes such as `backend-shard-1`; it must not add another reporter or change schema v1.
- The evidence layer has exactly three semantic hard gates: unregistered/contextually illegal skip, illegal quarantine, and zero execution for a selected lane declared `realDependency: true`.
- Timing delta, critical path, trends, registered skip totals, and `recovered-after-rerun` are report-only and never affect exit status.
- Test steps fail naturally. Do not use `continue-on-error`; collection and artifact upload use `if: always()` and never reconstruct or swallow the `dotnet test` exit code.
- Every new PowerShell entry point and PowerShell test/fixture dot-sources `scripts/lib/ScriptAutomation.ps1`; native child execution uses `Invoke-*` helpers.
- `scripts/generate-test-evidence-baseline.ps1` is the sole writer of `scripts/test-evidence-baseline.json`; routine CI is read-only against the baseline.
- The initial baseline uses the newest clean, first-attempt successful main run after #1442. Current qualifying candidate: run `30819675007`, job `91706113150`, commit `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`.
- Re-check main immediately before generating the baseline. Replace the current candidate only with a newer `push` run on `main` whose attempt is `1` and whose Backend Tests job succeeded.
- The live repository scan currently contains 40 source `Skip =` assignments in test trees. Classify each source explicitly; do not bulk-label the inventory `optional`.
- There are no initial quarantines. Any future quarantine requires responsibility issue, expiry date, exit condition, exact test/source pattern, and owner review.
- Upload only normalized/redacted evidence. Raw TRX, stdout/stderr, request/response bodies, database dumps, and broad repository log globs are never artifact inputs.
- No business HTTP endpoint, database schema, OpenAPI snapshot, generated client, frontend product behavior, or test business behavior changes are in scope.
- Keep documentation impact distinct from product docs: update CI/test governance architecture docs and implementation readiness; `frontend/apps/docs` has no product behavior impact.

---

### Task 0: Synchronize PR #1467 with current main before implementation

**Files:**
- No intentional content change; this task integrates the current `origin/main` history that already contains the #1464 CI format gate.

**Interfaces:**
- Consumes: existing detached HEAD whose remote PR branch is `codex/man-661-test-evidence-design`.
- Produces: an implementation base containing both the approved MAN-661 design and current main workflow shape.

- [ ] **Step 1: Fetch and inspect divergence**

```bash
git fetch origin
git status --short --branch
git log --oneline --left-right HEAD...origin/main
```

Expected: clean worktree before the merge; the left side contains the MAN-661 docs commits and the right side contains main changes after `a7e66e0c6`, including #1464.

- [ ] **Step 2: Merge current main without rewriting the open PR branch**

```bash
git merge --no-edit origin/main
```

Resolve only scoped documentation or `.github/workflows/ci.yml` conflicts. Preserve the current frontend format/lint step from main verbatim.

- [ ] **Step 3: Verify the synchronized base**

```bash
git status --short --branch
git log -n 8 --oneline --decorate
git diff --check origin/main...HEAD
```

Expected: clean worktree, MAN-661 design/plan reachable from HEAD, and #1464 workflow changes present.

---

### Task 1: Lock schema v1, lane contracts, and the exact skip inventory

**Files:**
- Create: `scripts/lib/TestEvidence.ps1`
- Create: `scripts/test-evidence-policy.json`
- Create: `scripts/tests/test-evidence.Tests.ps1`
- Create: `scripts/tests/fixtures/test-evidence/policy-valid.json`
- Create: `scripts/tests/fixtures/test-evidence/policy-illegal-quarantine.json`

**Interfaces:**
- Produces: `Import-NervTestEvidencePolicy -Path [string]` returning the validated schema object.
- Produces: `Test-NervTestEvidenceLaneName -Lane [string]` returning a Boolean.
- Produces: `Get-NervSourceSkipAssignments -RepoRoot [string]` returning `{ sourcePath, sourceOrdinal, sourceText }[]`.
- Produces: `Test-NervTestEvidencePolicy -Policy [object] -RepoRoot [string] -AsOfUtc [DateTimeOffset]` returning semantic violations without terminating the process.
- Policy shape: `{ schemaVersion, lanes[], sources[], rules[] }`.
- Lane row: `{ namePattern, realDependency }`.
- Source row: `{ id, sourcePath, sourceOrdinal, sourceReasonPattern }`.
- Rule row: `{ id, sourceId, classification, testPattern, reasonPattern, allowedLanes, requiredLane, allowedOperatingSystems, responsibilityIssue, expiresOn, exitCondition }`.

- [ ] **Step 1: Write the failing policy and lane tests**

Create `scripts/tests/test-evidence.Tests.ps1` with the governance header, resolve the repository root, and dot-source `ScriptAutomation.ps1`. Use a small in-file assertion harness:

```powershell
# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs deterministic MAN-661 evidence fixtures
#   Writes:
#     - Temporary files under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixture directories in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$fixtures = Join-Path $PSScriptRoot 'fixtures/test-evidence'
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/TestEvidence.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) {
        throw "$Message Expected=[$Expected] Actual=[$Actual]"
    }
}

function Assert-Violation([object[]] $Violations, [string] $Code) {
    Assert-True (@($Violations | Where-Object code -eq $Code).Count -gt 0) "Expected violation '$Code'."
}

Assert-True (Test-NervTestEvidenceLaneName 'backend') 'backend must be valid.'
Assert-True (Test-NervTestEvidenceLaneName 'backend-shard-1') 'backend-shard-1 must use schema v1.'
Assert-True (-not (Test-NervTestEvidenceLaneName 'backend/shard/1')) 'slash lane must be rejected.'

$policy = Import-NervTestEvidencePolicy -Path (Join-Path $PSScriptRoot 'fixtures/test-evidence/policy-valid.json')
Assert-Equal 1 $policy.schemaVersion 'Policy schema version must be one.'

$illegal = Import-NervTestEvidencePolicy -Path (Join-Path $PSScriptRoot 'fixtures/test-evidence/policy-illegal-quarantine.json')
$violations = Test-NervTestEvidencePolicy -Policy $illegal -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]'2026-08-03T16:00:00Z')
Assert-Violation $violations 'illegal-quarantine'
```

The invalid fixture contains a `quarantined` rule with `responsibilityIssue: null`; the valid fixture contains one optional connector rule and one environment-gated PostgreSQL rule.

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
pwsh scripts/tests/test-evidence.Tests.ps1
```

Expected: FAIL because `scripts/lib/TestEvidence.ps1` and the policy functions do not exist.

- [ ] **Step 3: Implement the minimal policy loader and lane validator**

Create `scripts/lib/TestEvidence.ps1` with a Script-Governance header and `Set-StrictMode -Version Latest`. Implement:

```powershell
function Test-NervTestEvidenceLaneName {
    param([Parameter(Mandatory)] [string] $Lane)
    if ($Lane.Contains('-shard-', [StringComparison]::Ordinal)) {
        return $Lane -cmatch '^[a-z0-9]+(?:-[a-z0-9]+)*-shard-[1-9][0-9]*$'
    }
    return $Lane -cmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$'
}

function Import-NervTestEvidencePolicy {
    param([Parameter(Mandatory)] [string] $Path)
    $policy = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
    if ([int] $policy.schemaVersion -ne 1) {
        throw "Unsupported test-evidence policy schemaVersion '$($policy.schemaVersion)'."
    }
    return $policy
}
```

Add policy validation for the only three classifications, unique IDs, valid lane regexes, exact quarantine metadata, and ISO `expiresOn`. Return violations as objects `{ code, id, message }`; do not throw for semantic policy violations.

- [ ] **Step 4: Add the full live source inventory without bulk optional classification**

Create `scripts/test-evidence-policy.json` with these lane contracts:

```json
{
  "schemaVersion": 1,
  "lanes": [
    { "namePattern": "^backend(?:-shard-[1-9][0-9]*)?$", "realDependency": false },
    { "namePattern": "^connector-host(?:-shard-[1-9][0-9]*)?$", "realDependency": false },
    { "namePattern": "^postgres(?:-shard-[1-9][0-9]*)?$", "realDependency": true },
    { "namePattern": "^full-chain(?:-shard-[1-9][0-9]*)?$", "realDependency": true },
    { "namePattern": "^performance(?:-shard-[1-9][0-9]*)?$", "realDependency": true },
    { "namePattern": "^connector-(?:docker|opcua)(?:-shard-[1-9][0-9]*)?$", "realDependency": true }
  ],
  "sources": [],
  "rules": []
}
```

Populate one `sources` row per source assignment below. `sourceOrdinal` is the one-based `Skip =` occurrence within that file. Populate one runtime rule unless the `Rules` column says four. Exact classifications are fixed by this plan:

| Source ID | Source path · ordinal | Classification | Required/capability lane | Rules |
| --- | --- | --- | --- | ---: |
| `connector-opcua-opt-in` | `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests/OpcUaSimulatorIntegrationTests.cs` · 1 | `optional` | capability `connector-opcua` | 1 |
| `connector-opcua-daemon` | same file · 2 | `environment-gated` | `connector-opcua` | 1 |
| `connector-docker-opt-in` | `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/DockerCliIntegrationTests.cs` · 1 | `optional` | capability `connector-docker` | 1 |
| `connector-docker-daemon` | same file · 2 | `environment-gated` | `connector-docker` | 1 |
| `connector-unix-sigterm` | `connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/SimulatedConnectorHostProcessTests.cs` · 1 | `optional` | OS `Windows` only | 1 |
| `testing-postgres-lifecycle` | `backend/tests/Nerv.IIP.Testing.PostgreSql.Tests/PostgreSqlTestDatabaseTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `fullchain-maintenance-runtime` | `backend/tests/Nerv.IIP.Business.FullChain.Tests/MaintenanceRuntimeHoursPostgresRedisAcceptanceTests.cs` · 1 | `environment-gated` | `full-chain` | 1 |
| `fullchain-mes-inventory` | `backend/tests/Nerv.IIP.Business.FullChain.Tests/MesInventoryProducedLotPostgresRedisAcceptanceTests.cs` · 1 | `environment-gated` | `full-chain` | 1 |
| `fullchain-erp-wms` | `backend/tests/Nerv.IIP.Business.FullChain.Tests/ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests.cs` · 1 | `environment-gated` | `full-chain` | 1 |
| `fullchain-sales-demand` | `backend/tests/Nerv.IIP.Business.FullChain.Tests/SalesOrderDemandPlanningPostgresRedisAcceptanceTests.cs` · 1 | `environment-gated` | `full-chain` | 1 |
| `fullchain-erp-return` | `backend/tests/Nerv.IIP.Business.FullChain.Tests/ErpReturnClosurePostgresAcceptanceTests.cs` · 1 | `environment-gated` | `full-chain` | 1 |
| `acceptance-runtime-maintenance` | `backend/tests/Nerv.IIP.Business.Acceptance.Tests/RuntimeHoursMaintenancePostgresAcceptanceTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `performance-baseline` | `backend/tests/Nerv.IIP.Business.Performance.Tests/PerformanceBaselineFactAttribute.cs` · 1 | `environment-gated` | `performance` | 4, exact tests `erp`, `inventory`, `mes`, `scheduling` |
| `apphub-postgres` | `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubPostgresProfileTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `filestorage-postgres` | `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageRestartPersistenceTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `demandplanning-postgres` | `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/ErpSalesOrderDemandConsumerTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `demandplanning-postgres-redis` | same file · 2 | `environment-gated` | `full-chain` | 1 |
| `productengineering-world-bible` | `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/WorldBibleSeedPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `wms-world-history` | `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WorldHistoryWmsSeedPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `wms-quality-gate` | `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsQualityInspectionGateConsumerTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `inventory-postgres-profile` | `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryPostgresProfileTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `wms-task-action` | `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WarehouseTaskActionConcurrencyPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `wms-short-pick` | `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsShortPickBackorderTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `wms-wcs-dispatch` | `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WcsDispatchConcurrencyPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `wms-assignment-migration` | `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsWorkAssignmentMigrationPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `barcodelabel-world-history` | `backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/WorldHistoryLabelSeedPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `scheduling-postgres` | `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/RecordSchedulePlanInvalidationsPostgresProfileTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `barcodelabel-postgres-profile` | `backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelPostgresProfileTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `industrialtelemetry-postgres` | `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryIdempotentConcurrencyTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `maintenance-world-history` | `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/WorldHistoryMaintenanceSeedPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `maintenance-device-pause` | `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventHandlerTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `quality-postgres` | `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityCapaRedrivePostgresProfileTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `masterdata-postgres` | `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataPostgresProfileTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `mes-world-history` | `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/WorldHistorySeedPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `mes-production-candidate` | `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/TelemetryProductionReportCandidatePostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `mes-cap-postgres` | `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesCapSubscriptionTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `erp-world-history` | `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/WorldHistorySeedPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `erp-business-partner` | `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/BusinessPartnerChangedPostgresAcceptanceTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `erp-cost-accounting` | `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpCostAccountingPostgresAcceptanceTests.cs` · 1 | `environment-gated` | `postgres` | 1 |
| `erp-scale-seed` | `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/LeaderDemoScaleSeedPostgresTests.cs` · 1 | `environment-gated` | `postgres` | 1 |

For each rule, use a fully anchored `testPattern` and `reasonPattern`. Do not use `.*` for the whole assembly and do not replace the table with a single optional catch-all. The four performance rules share `sourceId: performance-baseline` but have exact test names from `ErpPerformanceBaselineTests`, `InventoryPerformanceBaselineTests`, `MesPerformanceBaselineTests`, and `SchedulingScaleBenchmarkTests`.

- [ ] **Step 5: Implement source assignment discovery and prove the inventory is exact**

Scan only these roots for C# test files:

```text
backend/tests
backend/services/**/tests
connector-hosts/tests
```

Match source assignments with `\bSkip\s*=` and order them per normalized repository-relative path. Compare every discovered `{path, ordinal}` with `policy.sources`. New, missing, duplicate, or reason-mismatched entries return `unregistered-skip`.

Add assertions:

```powershell
$liveAssignments = Get-NervSourceSkipAssignments -RepoRoot $repoRoot
Assert-Equal 40 $liveAssignments.Count 'The approved initial source skip inventory changed; classify the diff explicitly.'
$liveViolations = Test-NervTestEvidencePolicy -Policy (Import-NervTestEvidencePolicy (Join-Path $repoRoot 'scripts/test-evidence-policy.json')) -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)
Assert-Equal 0 @($liveViolations).Count 'The committed live skip policy must be valid.'
```

The count assertion is an initial migration guard. Future intentional skip changes update both policy and the expected count in the same reviewed diff.

- [ ] **Step 6: Run tests and verify GREEN**

Run:

```powershell
pwsh scripts/tests/test-evidence.Tests.ps1
```

Expected: PASS; output identifies 40 registered source assignments and no illegal quarantine.

- [ ] **Step 7: Commit**

```bash
git add scripts/lib/TestEvidence.ps1 scripts/test-evidence-policy.json scripts/tests/test-evidence.Tests.ps1 scripts/tests/fixtures/test-evidence
git commit -m "test(ci): lock MAN-661 evidence policy schema"
```

---

### Task 2: Parse real TRX into stable schema-v1 test records

**Files:**
- Modify: `scripts/lib/TestEvidence.ps1`
- Modify: `scripts/tests/test-evidence.Tests.ps1`
- Create: `scripts/tests/fixtures/test-evidence/backend-results.trx`
- Create: `scripts/tests/fixtures/test-evidence/connector-results.trx`
- Create: `scripts/tests/fixtures/test-evidence/malformed-results.trx`

**Interfaces:**
- Produces: `Read-NervTrxResults -Path <string[]> -RunMetadata <hashtable>` returning normalized test records.
- Produces: `Get-NervTrxSkipReason -UnitTestResult <XmlElement>` returning a redacted string or `$null`.
- Normalized record fields: `schemaVersion`, `workflowRunId`, `runAttempt`, `commitSha`, `lane`, `project`, `assembly`, `testName`, `durationMilliseconds`, `outcome`, `skipReason`.

- [ ] **Step 1: Add a minimal namespaced TRX fixture and failing assertions**

Create `backend-results.trx` using the VSTest 2010 namespace with three results: one `Passed`, one `Failed`, and one `NotExecuted`. Include `TestDefinitions/UnitTest` entries whose `storage` values are `Nerv.IIP.Sample.Tests.dll`, and put the skip reason in `Output/ErrorInfo/Message`.

Add these exact assertions:

```powershell
$run = @{
    workflowRunId = '1001'
    runAttempt = 2
    commitSha = '0123456789abcdef0123456789abcdef01234567'
    lane = 'backend-shard-1'
}
$records = Read-NervTrxResults -Path @((Join-Path $fixtures 'backend-results.trx')) -RunMetadata $run
Assert-Equal 3 $records.Count 'TRX must yield one record per UnitTestResult.'
Assert-Equal 1 @($records | Where-Object outcome -eq 'passed').Count 'Passed outcome mismatch.'
Assert-Equal 1 @($records | Where-Object outcome -eq 'failed').Count 'Failed outcome mismatch.'
Assert-Equal 1 @($records | Where-Object outcome -eq 'skipped').Count 'NotExecuted must normalize to skipped.'
Assert-Equal 'backend-shard-1' $records[0].lane 'Shard lane must not alter schema.'
Assert-Equal 'Nerv.IIP.Sample.Tests.dll' $records[0].assembly 'Assembly must come from UnitTest storage.'
Assert-Equal 1250.0 ($records | Where-Object outcome -eq 'passed').durationMilliseconds 'Duration must use invariant TimeSpan parsing.'
Assert-Equal 'Set NERV_IIP_TEST_POSTGRES to run the fixture.' ($records | Where-Object outcome -eq 'skipped').skipReason 'Skip reason mismatch.'
```

- [ ] **Step 2: Run and verify RED**

Run `pwsh scripts/tests/test-evidence.Tests.ps1`.

Expected: FAIL because `Read-NervTrxResults` does not exist.

- [ ] **Step 3: Implement minimal namespace-independent TRX parsing**

Use `local-name()` XPath so the parser accepts standard VSTest namespace declarations. Build a lookup from `TestDefinitions/UnitTest@id` to `@storage` and `TestMethod@className/name`. Normalize:

```powershell
$outcomeMap = @{
    Passed = 'passed'
    Failed = 'failed'
    NotExecuted = 'skipped'
}
```

Reject unknown outcomes with a parsing exception. Parse `duration` using `[TimeSpan]::Parse(..., [Globalization.CultureInfo]::InvariantCulture)` and store total milliseconds as `[double]`. Derive `project` from the assembly filename without `.dll`.

Skip reason lookup order is:

1. `Output/ErrorInfo/Message`;
2. the first nonblank `Output/StdOut` line containing `SKIP`;
3. `$null`, which later becomes an unregistered runtime skip.

- [ ] **Step 4: Add multi-file and malformed-TRX tests**

Add a Connector Host fixture and assert both files aggregate without collision. Add a malformed XML fixture and assert a typed message containing only the redacted file path, never raw XML content.

```powershell
$combined = Read-NervTrxResults -Path @(
    (Join-Path $fixtures 'backend-results.trx'),
    (Join-Path $fixtures 'connector-results.trx')
) -RunMetadata $run
Assert-Equal 4 $combined.Count 'Multiple TRX files must aggregate.'
Assert-Equal 2 @($combined.assembly | Sort-Object -Unique).Count 'Assemblies must remain distinct.'
```

- [ ] **Step 5: Run and verify GREEN**

Run `pwsh scripts/tests/test-evidence.Tests.ps1`.

Expected: PASS for parsing, multi-file aggregation, schema v1 shard lane, duration, outcome, and malformed input diagnostics.

- [ ] **Step 6: Commit**

```bash
git add scripts/lib/TestEvidence.ps1 scripts/tests/test-evidence.Tests.ps1 scripts/tests/fixtures/test-evidence
git commit -m "feat(ci): normalize VSTest TRX evidence"
```

---

### Task 3: Enforce the three and only three evidence policy gates

**Files:**
- Modify: `scripts/lib/TestEvidence.ps1`
- Modify: `scripts/tests/test-evidence.Tests.ps1`
- Create: `scripts/tests/fixtures/test-evidence/postgres-all-skipped.trx`
- Create: `scripts/tests/fixtures/test-evidence/postgres-zero-results.trx`
- Create: `scripts/tests/fixtures/test-evidence/unregistered-skip.trx`
- Create: `scripts/tests/fixtures/test-evidence/policy-expired-quarantine.json`

**Interfaces:**
- Produces: `Get-NervTestEvidenceViolations -Records <object[]> -Policy <object> -SelectedLanes <string[]> -RunnerOs <string>` returning only codes `unregistered-skip`, `illegal-quarantine`, or `zero-execution`.
- Produces: `Test-NervRuleApplies -Rule <object> -SelectedLanes <string[]> -RunnerOs <string>`.
- `executed = passed + failed`; skipped never counts as executed.

- [ ] **Step 1: Write failing tests for unregistered/contextually illegal skips**

Add:

```powershell
$unregisteredRecords = Read-NervTrxResults -Path @((Join-Path $fixtures 'unregistered-skip.trx')) -RunMetadata $run
$violations = Get-NervTestEvidenceViolations -Records $unregisteredRecords -Policy $policy -SelectedLanes @('backend') -RunnerOs 'Linux'
Assert-Violation $violations 'unregistered-skip'

$postgresSelected = Get-NervTestEvidenceViolations -Records $records -Policy $policy -SelectedLanes @('postgres') -RunnerOs 'Linux'
Assert-Violation $postgresSelected 'unregistered-skip'
```

The second assertion proves an environment-gated skip becomes contextually unregistered when its required lane is selected; this is not a fourth gate code.

- [ ] **Step 2: Write failing zero-execution tests**

Use one TRX containing only skipped PostgreSQL tests and another with counters/result set zero:

```powershell
$postgresRun = $run.Clone()
$postgresRun.lane = 'postgres'
$allSkipped = Read-NervTrxResults -Path @((Join-Path $fixtures 'postgres-all-skipped.trx')) -RunMetadata $postgresRun
$violations = Get-NervTestEvidenceViolations -Records $allSkipped -Policy $policy -SelectedLanes @('postgres') -RunnerOs 'Linux'
Assert-Violation $violations 'zero-execution'

$empty = Read-NervTrxResults -Path @((Join-Path $fixtures 'postgres-zero-results.trx')) -RunMetadata $postgresRun
$violations = Get-NervTestEvidenceViolations -Records $empty -Policy $policy -SelectedLanes @('postgres') -RunnerOs 'Linux'
Assert-Violation $violations 'zero-execution'

$backendEmptyViolations = Get-NervTestEvidenceViolations -Records @() -Policy $policy -SelectedLanes @('backend-shard-1') -RunnerOs 'Linux'
Assert-True (-not (@($backendEmptyViolations.code) -contains 'zero-execution')) 'Ordinary backend shard zero execution is outside the MAN-661 real-dependency gate.'
```

- [ ] **Step 3: Write failing quarantine tests**

Assert missing issue, missing expiry, missing exit condition, invalid date, and expired date all produce `illegal-quarantine`. Assert no other policy code is emitted:

```powershell
$allowedCodes = @('unregistered-skip', 'illegal-quarantine', 'zero-execution')
Assert-Equal 0 @($violations | Where-Object { $allowedCodes -notcontains $_.code }).Count 'Evidence layer emitted an unapproved hard-gate code.'
```

- [ ] **Step 4: Run and verify RED**

Run `pwsh scripts/tests/test-evidence.Tests.ps1`.

Expected: FAIL because policy application and runtime gate functions do not exist.

- [ ] **Step 5: Implement exact-match policy application and gate calculation**

For each skipped record:

1. find rules whose anchored `testPattern` and `reasonPattern` match;
2. filter by `allowedLanes`, `requiredLane`, and `allowedOperatingSystems` context;
3. require exactly one applicable rule;
4. emit `unregistered-skip` for zero or multiple matches.

Validate quarantines before inspecting records. Determine real-dependency status by matching the current lane against exactly one `policy.lanes[].namePattern`. Emit `zero-execution` only when that row has `realDependency: true` and the selected lane has no passed/failed record.

- [ ] **Step 6: Prove report-only facts never become violations**

Construct a summary input with a 300% duration delta and `recovered-after-rerun`. Assert `Get-NervTestEvidenceViolations` returns no timing, trend, skip-count, or rerun code.

- [ ] **Step 7: Run and verify GREEN**

Run `pwsh scripts/tests/test-evidence.Tests.ps1`.

Expected: PASS; all semantic violation codes are within the approved three-code set.

- [ ] **Step 8: Commit**

```bash
git add scripts/lib/TestEvidence.ps1 scripts/tests/test-evidence.Tests.ps1 scripts/tests/fixtures/test-evidence
git commit -m "feat(ci): enforce skip and zero-execution gates"
```

---

### Task 4: Produce redacted JSONL, JSON, Markdown, and normalized TRX

**Files:**
- Modify: `scripts/lib/ScriptAutomation.ps1`
- Modify: `scripts/lib/TestEvidence.ps1`
- Modify: `scripts/tests/check-script-governance.Tests.ps1`
- Modify: `scripts/tests/test-evidence.Tests.ps1`
- Create: `scripts/tests/fixtures/test-evidence/sensitive-results.trx`
- Create: `scripts/tests/fixtures/test-evidence/baseline-report-only.json`

**Interfaces:**
- Produces: `Protect-NervTestEvidenceText -Text <string>` extending the shared redactor with CI evidence privacy fields.
- Produces: `New-NervTestEvidenceSummary -Records -RunMetadata -Violations -Baseline -PriorAttemptOutcome -TopCount`.
- Produces: `Write-NervTestEvidenceArtifacts -Records -Summary -OutputDirectory`.
- Raw source TRX paths are discovered and read only by `collect-test-evidence.ps1`; the artifact writer receives parsed records and never accepts or copies raw source paths.
- Artifact filenames: `tests.jsonl`, `summary.json`, `summary.md`, `diagnostics.log`, and `trx/<lane>-<assembly>-<sha8>-attempt-<n>.trx`.

- [ ] **Step 1: Extend redaction tests before redaction code**

In `scripts/tests/check-script-governance.Tests.ps1`, extend the existing shared redaction assertions for:

```text
https://user:password@example.invalid/path
Authorization: Bearer fixture-bearer-value
client_secret=fixture-client-secret
"customerName":"Fixture Customer"
"phone":"13800000000"
"email":"fixture@example.invalid"
```

Use non-usable sentinels only. Assert none of their values survive.

In `test-evidence.Tests.ps1`, parse `sensitive-results.trx`, write artifacts to an owned temp directory, recursively read every retained file, and assert the same sentinel values are absent.

- [ ] **Step 2: Run and verify RED**

Run:

```powershell
pwsh scripts/tests/check-script-governance.Tests.ps1
pwsh scripts/tests/test-evidence.Tests.ps1
```

Expected: FAIL because credential URLs and business privacy fields are not yet protected and artifact writers do not exist.

- [ ] **Step 3: Extend the shared redactor minimally**

Add bounded patterns to `Protect-ScriptAutomationText` for URL user info and named privacy keys (`customerName`, `phone`, `email`, `address`). Preserve key names and replace values with `<redacted>`. Do not add a generic JSON-value eraser that destroys normal diagnostics.

- [ ] **Step 4: Implement deterministic summary and report rendering**

`New-NervTestEvidenceSummary` computes:

- lane/run identity;
- per-assembly passed/failed/skipped/executed/total;
- summed test duration and TRX elapsed duration as separate fields;
- deterministic slowest assembly/test Top N ordered by duration descending then ordinal name;
- skip reason/classification aggregation;
- baseline delta fields with `enforcement: report-only`;
- `attemptClassification`.

Classification rules:

```powershell
if ($RunMetadata.runAttempt -eq 1) { 'initial' }
elseif ($PriorAttemptOutcome -eq 'failure' -and $Summary.failed -eq 0 -and $Violations.Count -eq 0) { 'recovered-after-rerun' }
else { 'rerun' }
```

If prior lookup is absent, set `priorAttemptStatus: prior-attempt-unavailable`; never infer recovery only from attempt number.

- [ ] **Step 5: Implement retained artifact writing**

Write UTF-8 without BOM. JSONL has one compact object per line. JSON and Markdown are deterministic for fixed input. Normalized TRX is reconstructed from safe result identity/outcome/duration plus redacted `ErrorInfo`; drop `StdOut`, `StdErr`, attachments, collector data, request/response bodies, and arbitrary result files.

Write artifacts to a temporary sibling directory and atomically rename into `OutputDirectory` only after all required files are complete. On failure, write a bounded redacted `diagnostics.log` before rethrowing.

- [ ] **Step 6: Assert baseline deltas and rerun are report-only**

Use `baseline-report-only.json` with deliberately smaller durations. Assert Markdown contains the delta and `recovered-after-rerun`, while violations remain empty and process status remains success.

- [ ] **Step 7: Run and verify GREEN**

Run the two PowerShell test scripts again. Expected: PASS and no sentinel value in any retained format.

- [ ] **Step 8: Commit**

```bash
git add scripts/lib/ScriptAutomation.ps1 scripts/lib/TestEvidence.ps1 scripts/tests/check-script-governance.Tests.ps1 scripts/tests/test-evidence.Tests.ps1 scripts/tests/fixtures/test-evidence
git commit -m "feat(ci): emit redacted test evidence reports"
```

---

### Task 5: Add the sole baseline generator and generate the initial clean-main baseline

**Files:**
- Create: `scripts/generate-test-evidence-baseline.ps1`
- Create: `scripts/test-evidence-baseline.json`
- Modify: `scripts/lib/TestEvidence.ps1`
- Modify: `scripts/tests/test-evidence.Tests.ps1`
- Create: `scripts/tests/fixtures/test-evidence/github-backend-console.log.txt`
- Create: `scripts/tests/fixtures/test-evidence/github-run-metadata.json`

**Interfaces:**
- Produces: `ConvertFrom-NervDotNetConsoleSummary -Text <string> -RunMetadata <object>` for the initial project-granularity import.
- Produces: `New-NervTestEvidenceBaseline -Summaries <object[]> -SourceMetadata <object> -GeneratedAtUtc <DateTimeOffset>`.
- Generator parameter sets:
  - `Evidence`: `-EvidenceRoot`, `-OutputPath`, optional `-GeneratedAtUtc`.
  - `GitHubConsole`: `-Repository`, `-GitHubRunId`, `-GitHubJobId`, `-OutputPath`, optional `-GeneratedAtUtc`.

- [ ] **Step 1: Write failing deterministic generator tests**

Add fixture console lines copied and redacted from run `30819675007` for BusinessGateway, IndustrialTelemetry, WMS, Inventory, and FullChain. Add metadata asserting `event: push`, `headBranch: main`, `runAttempt: 1`, `conclusion: success`, run/job/commit IDs.

Assertions:

```powershell
$imported = ConvertFrom-NervDotNetConsoleSummary -Text (Get-Content $consoleFixture -Raw) -RunMetadata $metadata
Assert-Equal 'project' $imported.granularity 'Console import is project-granularity.'
Assert-Equal 822000 ($imported.assemblies | Where-Object assembly -eq 'Nerv.IIP.BusinessGateway.Web.Tests.dll').durationMilliseconds '13m42s must normalize to milliseconds.'

$baselineA = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
$baselineB = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
Assert-Equal ($baselineA | ConvertTo-Json -Depth 100) ($baselineB | ConvertTo-Json -Depth 100) 'Baseline generation must be deterministic.'
```

- [ ] **Step 2: Run and verify RED**

Run `pwsh scripts/tests/test-evidence.Tests.ps1`.

Expected: FAIL because console import and baseline generation do not exist.

- [ ] **Step 3: Implement baseline model and generator entry point**

The generator declares `Category: generate`, dot-sources both shared libraries, and validates that the GitHub source is:

- workflow event `push`;
- branch `main`;
- run attempt `1`;
- run and Backend Tests job conclusions `success`;
- nonempty 40-character commit SHA.

For `GitHubConsole`, call `gh run view` only through `Invoke-NativeCommandOutput`; never direct-execute `gh`. Capture metadata with `--json` and the selected job log with `--log`, redact before parsing, and reject missing/ambiguous project summary lines.

Baseline metadata includes schema/tool version, `granularity`, run ID, attempt, job ID, SHA, source URL, runner OS/image when available, .NET SDK, selected lanes, generated timestamp, owner `Nerv-IIP Platform CI/Test Governance`, and normalized generator command.

- [ ] **Step 4: Verify the current latest qualifying main run**

Run:

```bash
gh run list --repo Mang-X/Nerv-IIP --workflow CI --branch main --limit 10 --json databaseId,attempt,headSha,conclusion,status,event,createdAt,url
```

Select the newest completed row satisfying the global constraint. Query the current candidate with `gh run view 30819675007 --json jobs`; if the list returned a newer qualifying run, query that exact numeric ID instead. At plan creation the expected source is run `30819675007`, Backend Tests job `91706113150`, commit `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`. If a newer qualifying run exists, record its exact IDs in the generated baseline and implementation report.

- [ ] **Step 5: Generate the committed baseline through the script only**

If the current candidate remains latest, run:

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 `
  -Repository Mang-X/Nerv-IIP `
  -GitHubRunId 30819675007 `
  -GitHubJobId 91706113150 `
  -OutputPath scripts/test-evidence-baseline.json
```

If Step 4 found a newer qualifying run, substitute only the verified run/job IDs; do not hand-edit the output.

- [ ] **Step 6: Prove the baseline is reproducible**

Copy the generated file to an owned temp path, rerun the same generator command, and compare SHA-256 hashes while passing the original `generatedAtUtc` from the file. Expected: identical hashes. Then run `pwsh scripts/tests/test-evidence.Tests.ps1`.

- [ ] **Step 7: Commit**

```bash
git add scripts/generate-test-evidence-baseline.ps1 scripts/test-evidence-baseline.json scripts/lib/TestEvidence.ps1 scripts/tests/test-evidence.Tests.ps1 scripts/tests/fixtures/test-evidence
git commit -m "feat(ci): generate clean-main test baseline"
```

---

### Task 6: Add the governed collector entry point and real-TRX compatibility proof

**Files:**
- Create: `scripts/collect-test-evidence.ps1`
- Modify: `scripts/tests/test-evidence.Tests.ps1`
- Modify: `scripts/tests/check-script-governance.Tests.ps1`

**Interfaces:**
- Collector parameters: `-Lane`, `-SelectedLanes`, `-ResultsDirectory`, `-OutputDirectory`, `-PolicyPath`, `-BaselinePath`, `-WorkflowRunId`, `-RunAttempt`, `-CommitSha`, `-RunnerOs`, `-Repository`, `-JobName`, `-PriorAttemptOutcome`, `-StepSummaryPath`.
- `-PriorAttemptOutcome` bypasses network and is used by fixtures.
- When attempt is greater than one and no prior outcome was supplied, optional read-only lookup uses `Repository + WorkflowRunId + RunAttempt - 1 + JobName`.

- [ ] **Step 1: Write failing CLI tests**

Use `Invoke-PwshScript` for a successful fixture and catch its structured failure for invalid fixtures. Assert:

- successful collection writes all five artifact types;
- unregistered skip exits nonzero after writing summary/diagnostics;
- all-skipped `postgres` exits nonzero with `zero-execution`;
- 300% baseline delta exits zero;
- prior `failure` plus current pass emits `recovered-after-rerun` and exits zero;
- no raw results path is copied into the output.

- [ ] **Step 2: Run and verify RED**

Run `pwsh scripts/tests/test-evidence.Tests.ps1`.

Expected: FAIL because the collector entry point does not exist.

- [ ] **Step 3: Implement the collector**

The collector declares `Category: check` and writes only its declared output. Execution order:

1. validate run/lane parameters;
2. import and validate policy/baseline;
3. enumerate `*.trx` recursively under the exact results directory;
4. parse records;
5. calculate the three policy gates;
6. resolve optional prior attempt through `Invoke-NativeCommandOutput -Command gh` and map lookup failure to `prior-attempt-unavailable`;
7. build/write redacted artifacts;
8. append `summary.md` to `StepSummaryPath` when provided;
9. print report-only facts;
10. exit nonzero for parsing/infrastructure failure or any of the approved three violations.

Do not invoke `dotnet` from the collector.

- [ ] **Step 4: Remove the temporary governance guards**

In `check-script-governance.Tests.ps1`, call `Invoke-GovernanceScriptCase` unconditionally for both collector and generator.

- [ ] **Step 5: Run focused real tests into temporary TRX directories**

Use a GUID-named operating-system temp directory outside the repository for raw results. Run:

```powershell
$man661RawRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-man-661-real-trx-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($man661RawRoot) | Out-Null
dotnet test backend/tests/Nerv.IIP.Business.Performance.Tests/Nerv.IIP.Business.Performance.Tests.csproj --configuration Release --logger trx --results-directory (Join-Path $man661RawRoot 'backend')
dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests.csproj --configuration Release --logger trx --results-directory (Join-Path $man661RawRoot 'connector')
```

Then invoke the collector for lane `backend` and `connector-host` with run ID `local-man-661`, attempt `1`, current commit SHA, policy/baseline paths, and output under a second owned temp directory. Expected: actual VSTest files parse, environment-gated/optional skips match exact policy, and both collectors exit zero.

- [ ] **Step 6: Run script governance and fixture suites**

```powershell
pwsh scripts/tests/test-evidence.Tests.ps1
pwsh scripts/tests/check-script-governance.Tests.ps1
pwsh scripts/check-script-governance.ps1
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add scripts/collect-test-evidence.ps1 scripts/tests/test-evidence.Tests.ps1 scripts/tests/check-script-governance.Tests.ps1
git commit -m "feat(ci): add governed test evidence collector"
```

---

### Task 7: Wire Backend and Connector Host CI without washing failures green

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `scripts/tests/test-evidence.Tests.ps1`

**Interfaces:**
- Backend lane: `backend`.
- Connector lane: `connector-host`.
- Raw root: `artifacts/test-evidence-raw/<run-id>/attempt-<n>/<lane>`; never uploaded.
- Retained root: `artifacts/test-evidence/<run-id>/attempt-<n>/<lane>`.
- Artifact names: `test-evidence-<lane>-<run-id>-<attempt>`.

- [ ] **Step 1: Write the failing workflow contract assertions**

Read `.github/workflows/ci.yml` as text and assert:

```powershell
Assert-True ($workflow.Contains('actions: read')) 'Rerun lookup needs read-only Actions permission.'
Assert-True (-not $workflow.Contains('continue-on-error')) 'MAN-661 forbids continue-on-error.'
Assert-True ($workflow.Contains('--logger trx')) 'Backend and Connector Host must emit TRX.'
Assert-True ($workflow.Contains('./scripts/collect-test-evidence.ps1')) 'CI must use the governed collector.'
Assert-True ($workflow.Contains('if: always()')) 'Collection/upload must run after failures.'
Assert-True ($workflow.Contains('test-evidence-backend-${{ github.run_id }}-${{ github.run_attempt }}')) 'Backend artifact identity mismatch.'
Assert-True ($workflow.Contains('test-evidence-connector-host-${{ github.run_id }}-${{ github.run_attempt }}')) 'Connector artifact identity mismatch.'
Assert-True (-not $workflow.Contains('path: artifacts/test-evidence-raw')) 'Raw TRX must not be uploaded.'
```

Also parse step ordering by locating test, collector, and upload names; assert test < collector < upload for both jobs and every collector/upload block contains `if: always()`.

- [ ] **Step 2: Run and verify RED**

Run `pwsh scripts/tests/test-evidence.Tests.ps1`.

Expected: FAIL because CI does not yet emit or retain evidence.

- [ ] **Step 3: Add least-privilege permissions**

Change workflow permissions to:

```yaml
permissions:
  actions: read
  contents: read
```

Do not add checks, pull-request, or contents write permissions.

- [ ] **Step 4: Add backend TRX, collection, and upload steps**

Change only the existing test command arguments; do not wrap it in a shell pipeline:

```yaml
- name: Test backend solution
  run: >-
    dotnet test backend/Nerv.IIP.sln
    --configuration Release
    --logger trx
    --results-directory artifacts/test-evidence-raw/${{ github.run_id }}/attempt-${{ github.run_attempt }}/backend

- name: Collect backend test evidence
  if: always()
  shell: pwsh
  run: >-
    ./scripts/collect-test-evidence.ps1
    -Lane backend
    -SelectedLanes backend
    -ResultsDirectory artifacts/test-evidence-raw/${{ github.run_id }}/attempt-${{ github.run_attempt }}/backend
    -OutputDirectory artifacts/test-evidence/${{ github.run_id }}/attempt-${{ github.run_attempt }}/backend
    -WorkflowRunId ${{ github.run_id }}
    -RunAttempt ${{ github.run_attempt }}
    -CommitSha ${{ github.sha }}
    -RunnerOs ${{ runner.os }}
    -Repository ${{ github.repository }}
    -JobName "Backend Tests"
    -StepSummaryPath $env:GITHUB_STEP_SUMMARY

- name: Upload backend test evidence
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: test-evidence-backend-${{ github.run_id }}-${{ github.run_attempt }}
    path: artifacts/test-evidence/${{ github.run_id }}/attempt-${{ github.run_attempt }}/backend
    if-no-files-found: error
    retention-days: 14
```

The collector appends its rendered Markdown to `$GITHUB_STEP_SUMMARY`; `summary.md` inside the retained evidence directory is the redacted downloadable copy.

- [ ] **Step 5: Add equivalent Connector Host steps**

Use lane `connector-host`, job name `Connector Host Tests`, Connector Host solution path, and connector-specific raw/retained/artifact paths. Do not instrument the ERP acceptance lane in MAN-661; its dedicated artifacts remain unchanged and future FullChain consolidation reuses the collector.

- [ ] **Step 6: Prove natural failure semantics structurally**

The workflow contract test must show:

- test steps have no `if`, `continue-on-error`, or pipe;
- collectors and uploads have `if: always()`;
- no synthetic “restore test status” step exists;
- upload uses only normalized retained roots.

- [ ] **Step 7: Run GREEN verification**

```powershell
pwsh scripts/tests/test-evidence.Tests.ps1
pwsh scripts/tests/check-script-governance.Tests.ps1
pwsh scripts/check-script-governance.ps1
```

Also run `git diff --check`. Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add .github/workflows/ci.yml scripts/tests/test-evidence.Tests.ps1
git commit -m "ci: retain backend and connector test evidence"
```

---

### Task 8: Document ownership, refresh rules, and delivered boundary

**Files:**
- Create: `docs/architecture/test-evidence-governance.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/superpowers/specs/2026-08-03-man-661-test-evidence-baseline-design.md`
- Modify: `scripts/tests/test-evidence.Tests.ps1`

**Interfaces:**
- Operator refresh command remains exactly `pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json` for TRX-era baselines.
- Initial legacy-console command is recorded with the actual source run/job IDs.

- [ ] **Step 1: Add failing documentation contract assertions**

Assert the architecture doc contains:

- all three skip classifications;
- exactly the three gate names;
- `backend-shard-1` and MAN-669 reuse boundary;
- `recovered-after-rerun` report-only statement;
- `continue-on-error` prohibition;
- owner `Nerv-IIP Platform CI/Test Governance`;
- mandatory refresh after MAN-663 and MAN-669;
- baseline generator command;
- raw artifact exclusion and redaction boundary;
- current baseline run/job/SHA provenance.

- [ ] **Step 2: Run and verify RED**

Run `pwsh scripts/tests/test-evidence.Tests.ps1`.

Expected: FAIL because `docs/architecture/test-evidence-governance.md` does not exist.

- [ ] **Step 3: Write the operator-facing governance document**

Keep it concise but operational. Include schema field table, artifact tree, policy row schema, interpretation of the three gates, report-only metrics, rerun correlation, least-privilege permissions, baseline owner, qualifying-source algorithm, refresh triggers, and both generator commands.

- [ ] **Step 4: Update implementation readiness**

Add a dated MAN-661 section stating what is delivered and what remains owned by MAN-662/663/669/668/688. Record the current baseline provenance and make clear that local fixture passes, full solution test execution, PR CI, merge, and post-merge test-granularity refresh are distinct statuses.

- [ ] **Step 5: Align the design with the final generated baseline**

If Task 5 selected a newer qualifying main run than `30819675007`, update the design's baseline candidate table and provenance through normal Markdown editing. Do not change the approved architecture or hard-gate boundary.

- [ ] **Step 6: Run and verify GREEN**

```powershell
pwsh scripts/tests/test-evidence.Tests.ps1
pwsh scripts/tests/check-script-governance.Tests.ps1
pwsh scripts/check-script-governance.ps1
git diff --check
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add docs/architecture/test-evidence-governance.md docs/architecture/implementation-readiness.md docs/superpowers/specs/2026-08-03-man-661-test-evidence-baseline-design.md scripts/tests/test-evidence.Tests.ps1
git commit -m "docs(ci): govern MAN-661 evidence lifecycle"
```

---

### Task 9: Run full verification and prepare PR #1467 evidence

**Files:**
- Modify only if a verification-discovered defect requires a scoped fix in files already listed above.

**Interfaces:**
- Produces fresh local evidence for fixture, governance, real TRX, backend solution, Connector Host solution, baseline reproducibility, and workflow structure.
- Does not merge, mark Linear complete, or claim PR CI status from local results.

- [ ] **Step 1: Run the fast governed suite**

```powershell
pwsh scripts/tests/test-evidence.Tests.ps1
pwsh scripts/tests/check-script-governance.Tests.ps1
pwsh scripts/check-script-governance.ps1
```

Record exit codes and exact pass counts/output.

- [ ] **Step 2: Run full backend tests with TRX**

Use an owned temporary raw directory and run:

```powershell
$man661FullRawRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-man-661-full-trx-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($man661FullRawRoot) | Out-Null
dotnet test backend/Nerv.IIP.sln --configuration Release --logger trx --results-directory (Join-Path $man661FullRawRoot 'backend')
```

Then collect with lane `backend` and verify:

- at least one TRX per executed test assembly;
- `tests.jsonl` contains test-level records;
- `summary.json` counts agree with TRX counters;
- all observed skips match the exact policy;
- artifact scan finds no sensitive sentinels or raw outputs.

This is actual test execution evidence, not merely compilation.

- [ ] **Step 3: Run full Connector Host tests with TRX**

```powershell
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln --configuration Release --logger trx --results-directory (Join-Path $man661FullRawRoot 'connector-host')
```

Collect with lane `connector-host` and perform the same count/redaction checks.

- [ ] **Step 4: Reproduce the baseline**

Run the exact generator command recorded in `scripts/test-evidence-baseline.json` with its recorded `generatedAtUtc` into a temp output and compare SHA-256 against the committed file. Expected: identical.

- [ ] **Step 5: Inspect the complete diff**

```bash
git diff --check
git status --short
git diff --stat origin/main...HEAD
git diff origin/main...HEAD -- .github/workflows/ci.yml scripts docs/architecture/test-evidence-governance.md docs/architecture/implementation-readiness.md
```

Verify no generated API/client, business endpoint, database, frontend, or unrelated test refactor entered the diff.

- [ ] **Step 6: Commit any verification-only correction**

Only when Step 1–5 found a scoped defect, stage exactly the already-listed MAN-661 file paths that changed, inspect `git diff --cached`, and commit with `git commit -m "fix(ci): correct MAN-661 evidence verification"`. If no defect exists, do not create an empty commit.

- [ ] **Step 7: Push and monitor PR #1467**

Push the detached worktree commit chain to the existing PR branch:

```bash
git push origin HEAD:codex/man-661-test-evidence-design
```

Wait for PR #1467 checks. Distinguish local full tests, PR CI, merge status, and the required post-merge test-granularity baseline refresh. Do not mark MAN-661 complete before the PR CI evidence and required artifacts are inspectable.

- [ ] **Step 8: Update Linear only with verified facts**

Add a MAN-661 comment containing:

- PR URL;
- exact local commands and outcomes;
- baseline source run/job/SHA;
- current 40-source skip inventory and classifications;
- confirmation that timing/rerun are report-only;
- PR CI status and artifact names;
- any remaining post-merge test-granularity refresh action.

Move the issue state only when the user's workflow and verified delivery status justify it; do not equate an open green PR with merge or completion.
