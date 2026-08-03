# MAN-661 Test Evidence Baseline Design

## Decision Record

MAN-661 adopts a repository-owned PowerShell evidence pipeline. The design was approved on 2026-08-03 during the 22:35–23:26 Asia/Shanghai decision window.

The pipeline owns TRX collection, normalization, machine-readable summaries, skip governance, zero-execution detection, rerun correlation, redaction, and baseline comparison. It does not introduce a third-party test reporter or a second CI result authority.

Implementation alignment (2026-08-04): the initial generated baseline retained the approved candidate, run `30819675007`, Backend Tests job `91706113150`, and commit `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`. The delivered operator contract is `docs/architecture/test-evidence-governance.md`; no hard-gate or schema-v1 boundary changed during implementation.

MAN-661 builds the complete collection pipeline. MAN-669 consumes it when backend tests are sharded; MAN-669 may add lane names such as `backend-shard-1`, but it must not create another collector or change the evidence schema.

## Context

The current CI workflow runs the backend and Connector Host solutions with `dotnet test`, but relies on console output for project duration and passed/failed/skipped counts. Results are not retained as a consistent machine-readable dataset, skip reasons are not governed, and a dependency-gated test assembly can be green even when every selected real-dependency test was skipped.

The current clean main baseline candidate is GitHub Actions run `30819675007`, attempt `1`, commit `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`, after #1442 merged. Its Backend Tests job spent about 22 minutes 17 seconds in the test step. The recorded assembly critical path included:

| Assembly | Duration | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| `Nerv.IIP.BusinessGateway.Web.Tests` | 13 m 42 s | 1023 | 0 | 0 |
| `Nerv.IIP.Business.IndustrialTelemetry.Web.Tests` | 1 m 24 s | 240 | 0 | 8 |
| `Nerv.IIP.Business.Wms.Web.Tests` | 1 m 15 s | 295 | 0 | 10 |
| `Nerv.IIP.Business.Inventory.Web.Tests` | 51 s | 231 | 0 | 2 |
| `Nerv.IIP.Business.FullChain.Tests` | 28 s | 10 | 0 | 5 |

These figures are observations, not thresholds. They demonstrate that the critical path and skip shape already drift from the earlier issue description and justify a reproducible evidence baseline.

## Goals

1. Produce retained TRX and a stable machine-readable record for every backend and Connector Host CI run.
2. Show selected lanes, assembly counts and durations, slowest assemblies and tests, skip reasons, baseline deltas, and rerun status in the GitHub Actions job summary.
3. Distinguish allowed non-selection from an environment-gated lane that was selected but executed zero tests.
4. Govern every skip as `optional`, `environment-gated`, or `quarantined`.
5. Correlate attempts for the same workflow run and commit and report a failed first attempt followed by a passing rerun as `recovered-after-rerun`.
6. Upload only redacted evidence while preserving the natural test failure result.
7. Commit an owned, reproducible main-branch baseline that later test-isolation and shard work can refresh and compare against.

## Non-Goals

1. Refactoring or optimizing slow tests.
2. Adding timing or trend thresholds to required CI gates.
3. Treating a rerun pass as if the initial failure did not happen.
4. Adding the future PostgreSQL, Redis, FullChain, performance, or nightly lane topology.
5. Replacing `dotnet test`, VSTest/TRX, xUnit, or the existing GitHub Actions workflow authority.
6. Implementing MAN-669 sharding, MAN-663 BusinessGateway host reuse, or MAN-662 shared timing/static-state isolation.

## Alternatives Considered

### Third-Party Test Reporter Action

A third-party reporter can render TRX quickly, but it does not own Nerv-IIP's skip classifications, quarantine metadata, selected-real-lane zero-execution rule, baseline lifecycle, or repository redaction contract. It would also add a new artifact-processing trust boundary.

### Standalone .NET Evidence CLI

A typed .NET CLI could parse TRX and emit the same schema. It would add a restore/build/tool lifecycle to the CI evidence path and duplicate the repository's existing PowerShell automation governance. The additional complexity is not justified for the initial fact baseline.

### Governed Repository PowerShell Pipeline

The selected design uses small governed PowerShell entry points with a shared library and fixture-driven tests. It can consume VSTest TRX directly, reuse `ScriptAutomation.ps1` redaction, run on the existing Ubuntu hosted runner, and remain reusable by later lanes without changing test framework or workflow authority.

## Architecture

The implementation has five responsibilities with explicit boundaries:

1. **Test invocation** remains in `.github/workflows/ci.yml`. It selects the lane and invokes the existing solution-level `dotnet test` command with the TRX logger and a lane/run/attempt-specific results directory.
2. **Evidence collection** parses all TRX files produced by one lane, normalizes records, validates the skip policy, renders reports, and writes only to `artifacts/test-evidence/**`.
3. **Policy** declares every allowed skip and the real-dependency lane relationship used to determine whether an environment-gated skip is legal.
4. **Baseline generation** is the only writer of the committed baseline file. Routine CI reads the baseline but cannot modify it.
5. **Workflow retention** appends the Markdown report to `$GITHUB_STEP_SUMMARY` and uploads the redacted evidence directory even after test or collector failure.

The planned repository files are:

| File | Responsibility |
| --- | --- |
| `scripts/lib/TestEvidence.ps1` | Pure TRX parsing, normalization, policy validation, report rendering, baseline comparison, rerun classification, and redaction helpers. |
| `scripts/collect-test-evidence.ps1` | Governed `check` entry point for one lane; writes normalized evidence and returns nonzero only for parsing/infrastructure failure or the three evidence policy violations. |
| `scripts/generate-test-evidence-baseline.ps1` | Governed `generate` entry point and sole writer of the committed baseline. |
| `scripts/test-evidence-policy.json` | Versioned skip inventory and lane dependency rules. |
| `scripts/test-evidence-baseline.json` | Script-generated, report-only comparison baseline with source-run provenance. |
| `scripts/tests/test-evidence.Tests.ps1` | Fixture-driven executable contract for schema, policy, redaction, zero execution, rerun, and baseline behavior. |
| `scripts/tests/fixtures/test-evidence/**` | Minimal synthetic TRX, policy, baseline, log, and attempt inputs; contains no credentials that resemble usable secrets. |
| `.github/workflows/ci.yml` | TRX invocation, always-run collection/summary/upload, and previous-attempt lookup. |
| `docs/architecture/test-evidence-governance.md` | Operator-facing schema, skip policy, baseline ownership, refresh procedure, and failure interpretation. |
| `docs/architecture/implementation-readiness.md` | Current delivered boundary and verification entry points. |

`TestEvidence.ps1` is a focused library rather than a new general-purpose scripts utility. It must not absorb unrelated CI orchestration or test execution.

## Lane Namespace And MAN-669 Compatibility

Every evidence record contains one required, opaque, filesystem-safe `lane` string. Version 1 reserves this naming convention:

```text
<family>
<family>-shard-<positive-integer>
```

Initial lanes are `backend` and `connector-host`. Future examples include `backend-shard-1`, `backend-shard-2`, `postgres`, and `full-chain`.

The schema does not encode a fixed lane enum and does not add a shard-only envelope. A shard is another lane using the reserved namespace. MAN-669 therefore only adds lane invocations and policy rows; it does not change `schemaVersion`, record fields, collector code paths, or artifact layout.

Artifact paths keep attempts and lanes isolated:

```text
artifacts/test-evidence/<workflow-run-id>/attempt-<run-attempt>/<lane>/
├── trx/
├── tests.jsonl
├── summary.json
├── summary.md
└── diagnostics.log
```

The collector normalizes raw TRX names to include lane, assembly, abbreviated tested SHA, and attempt. It never relies on one user-supplied `LogFileName` shared by all projects in a solution.

## Evidence Schema

`tests.jsonl` contains one JSON object per test result. `summary.json` contains aggregate arrays and run metadata. Both carry `schemaVersion: 1`.

The per-test record contains at least:

```json
{
  "schemaVersion": 1,
  "workflowRunId": "30819675007",
  "runAttempt": 1,
  "headSha": "9dafb512c992b240222c8d9b5ada43e4bfc8ac3d",
  "testedSha": "9dafb512c992b240222c8d9b5ada43e4bfc8ac3d",
  "lane": "backend",
  "project": "Nerv.IIP.BusinessGateway.Web.Tests",
  "assembly": "Nerv.IIP.BusinessGateway.Web.Tests.dll",
  "testName": "Nerv.IIP.ExampleTests.example",
  "durationTicks": 124000,
  "durationMilliseconds": 12.4,
  "outcome": "passed",
  "skipReason": null
}
```

Allowed normalized outcomes are `passed`, `failed`, and `skipped`. Unknown or malformed VSTest outcomes are parsing failures and cannot be silently counted as another outcome.

The parser preserves a valid TRX `executionId` as `testInstanceId`; only absent or invalid IDs use a deterministic fallback. `durationTicks` is the reversible 100 ns duration authority used to write normalized TRX, while `durationMilliseconds` remains the reporting projection.

The summary includes:

- selected lane and run identity;
- project/assembly passed, failed, skipped, executed, and total counts;
- project/assembly elapsed duration from TRX counters/times and summed test duration as separate values;
- slowest assemblies and slowest tests Top N;
- skip reason aggregation and matched policy entry;
- committed baseline source and report-only delta;
- previous attempt outcome when available;
- `attemptClassification` of `initial`, `rerun`, or `recovered-after-rerun`;
- the three evidence policy violation collections;
- redaction count and collector diagnostics that contain no raw secret values.

No schema field is removed or reinterpreted within version 1. Additive optional fields are allowed. A breaking change requires a new `schemaVersion`, migration support for the committed baseline, and dedicated fixtures for both versions.

## Skip Policy

Every runtime skip must match exactly one policy entry by source/test identity and reason pattern. The policy also records a source marker so the governance check can detect a newly added source-level `Skip` assignment before a matching CI lane happens to execute it.

The skip budget is the exact registered inventory, not a runner-sensitive numeric threshold. Each policy entry names the tests or source markers it covers; a new test, a new source marker, or a skip occurring outside the entry's selected-lane conditions is outside budget and is treated as unregistered. Aggregate registered skip totals remain report-only.

The three classifications are:

### `optional`

The test belongs to a capability that is not selected in the current lane, such as an explicit simulator integration. The policy names its allowed lanes and stable reason pattern. Optional means selection is optional; it does not allow an unexplained skip after a lane contract says the capability was selected.

### `environment-gated`

The test needs a real dependency lane. The policy names `requiredLane`, for example `postgres` or `full-chain`. Its skip is allowed only when that required lane is not among the selected lanes. When the required lane is selected, matching tests must execute rather than skip.

### `quarantined`

A quarantine is temporary and must include all of:

- a responsibility issue identifier or URL;
- an explicit expiry date;
- a measurable exit condition;
- the affected lane and exact test/source pattern.

Missing any field makes the policy invalid. Expired quarantine also becomes invalid; extending it requires an intentional policy diff and owner review.

## Hard-Gate Boundary

The evidence layer adds exactly three semantic red-light conditions:

1. **Unregistered skip:** a TRX skip or source skip marker matches no contextually applicable policy entry, or ambiguously matches more than one entry. An `optional` or `environment-gated` entry becomes inapplicable when its capability/required lane is selected, so a skip in that context is covered by this same condition rather than introducing another gate type.
2. **Illegal quarantine:** a quarantine lacks its responsibility issue, expiry date, exit condition, or is expired.
3. **Selected real-dependency lane executed zero tests:** a lane declared `realDependency: true`, including one of its selected filters or shards, has `passed + failed = 0`; skipped tests do not count as executed. Ordinary backend shards are not implicitly real-dependency lanes.

Timing delta, critical-path movement, trend, slowest-test ranking, registered skip totals, and `recovered-after-rerun` are report-only. They must never change the process exit code in MAN-661.

Existing test failures remain natural `dotnet test` failures. A malformed TRX, collector crash, or artifact-generation failure is an ordinary CI infrastructure failure, not a fourth policy threshold. The distinction keeps the governance rule set closed without allowing a broken evidence pipeline to report green.

## Workflow Failure Semantics

The test step runs normally and is allowed to fail naturally. The workflow must not set `continue-on-error` on test, collection, summary, or upload steps, and must not pipe `dotnet test` through a command that loses its exit code.

The sequence is:

1. Run solution-level `dotnet test` with TRX output in a unique raw directory.
2. Run evidence collection with `if: always()`.
3. Append the produced summary with `if: always()` when it exists.
4. Upload only the redacted evidence directory with `if: always()`.

GitHub Actions preserves the failed test step as the job result while still executing later `if: always()` steps. There is no synthetic final step that reconstructs or overwrites the test exit code, and no use of `continue-on-error` that could wash red into green.

The collector writes as much safe summary information as possible before returning a nonzero policy or parsing result. The upload step runs even when collection fails.

## Rerun Correlation

`workflowRunId` is the stable identity shared by GitHub Actions attempts. `runAttempt`, `headSha`, `testedSha`, and `lane` distinguish individual evidence records. `headSha` is the event branch head; `testedSha` is the checkout proven by `git rev-parse HEAD`. They may differ on `pull_request` because GitHub tests a synthetic merge commit, and must be identical on `push`. Artifact refresh and legacy console import both recover `testedSha` from an independently downloaded job log: current logs use `tested-sha=<sha>`, while historical console support is limited to the exact checkout `git log -1 --format=%H` command and its following SHA. Missing or conflicting checkout authority is an error; copying `headSha` into `testedSha` is not valid provenance.

For attempt `1`, classification is `initial`. For attempt greater than `1`, the workflow performs a read-only lookup of the same named job in the immediately preceding attempt and passes its conclusion to the collector. The collector reports:

- `rerun` when the prior attempt did not fail or the current lane did not pass;
- `recovered-after-rerun` when the prior same-run, same-commit, same-lane job failed and the current lane passed.

This classification is report-only. Both attempts remain independently retained and joinable by `workflowRunId + headSha + testedSha + lane`; the initial failure is never removed or rewritten.

If the previous-attempt lookup is unavailable, the collector records `prior-attempt-unavailable` and continues. It must not infer recovery from `runAttempt > 1` alone.

The workflow grants only `contents: read` and the additional `actions: read` permission required for this lookup. It does not grant write access to Actions, checks, pull requests, or repository contents.

## Redaction And Artifact Boundary

Raw TRX and captured console logs are temporary runner inputs and are never uploaded directly. The collector writes structurally normalized TRX copies and summaries into the retained directory.

All retained free text passes through the existing `Protect-ScriptAutomationText` or streaming `Protect-ScriptAutomationLogFile` contract. XML text and attributes are redacted before normalized TRX is serialized. Parameterized `body`, `requestBody`, and `responseBody` display-name values are removed case-insensitively with nested/escaped value parsing and replaced by non-reversible digest markers; method names and non-body parameters remain visible. At minimum, redaction covers:

- passwords and connection-string password fields;
- bearer tokens and generic token/secret values;
- `Authorization` headers;
- private keys and client secrets;
- connection strings and URLs containing credentials;
- request/response bodies, database rows, or business payload dumps not needed to identify the failing test.

The machine-readable summary retains only test identity, assembly/project, duration, outcome, governed skip reason, run identity, and aggregates. It does not retain arbitrary stdout, HTTP payloads, database dumps, customer records, or environment-variable values.

Fixtures use obvious non-usable sentinel strings and assert that none survive in normalized TRX, JSONL, JSON, Markdown, or diagnostics. Artifact upload targets only `artifacts/test-evidence/**`; it never uploads the raw results directory or an unbounded repository log glob.

## Baseline Ownership And Refresh

`scripts/test-evidence-baseline.json` is owned by the Nerv-IIP Platform CI/Test Governance maintainers, represented by the Linear project **Nerv-IIP 测试可信度与 CI 效率治理** and the repository's `area:infra` / `domain:platform` review boundary.

The baseline is report-only. It stores:

- schema version;
- generation tool version;
- source workflow run ID, attempt, head SHA, tested SHA, URL, runner OS/image, .NET SDK version, and selected lanes;
- per-lane and per-assembly counts and durations;
- known critical path at generation time;
- generation timestamp and owner;
- the normalized command recorded by the generator.

The baseline file must never be hand-edited. `scripts/generate-test-evidence-baseline.ps1` is its sole writer. The canonical refresh command is:

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 `
  -EvidenceRoot artifacts/test-evidence `
  -OutputPath scripts/test-evidence-baseline.json
```

At generation time, the initial project-level baseline imports the latest clean first-attempt successful main run after #1442. Run `30819675007` is the current candidate and must be replaced only when a newer qualifying main run exists. Its tested checkout is taken from the historical checkout command in the authoritative Backend Tests job log, not inferred from the run head. The shared checkout validator accepts a distinct branch head and synthetic merge SHA when validating pull-request provenance, but pull-request runs remain ineligible baseline sources. The generator records `granularity: project`; the first successful main run with MAN-661 TRX refreshes it to `test` granularity using the same command.

A refresh is required after the first successful main run following any of:

1. MAN-663 BusinessGateway shared-host/profile work merges.
2. MAN-669 changes lane or shard topology.
3. MAN-662 or MAN-664 intentionally changes shared test timing, isolation, or project composition.
4. The hosted runner image, .NET SDK major/minor, test framework, test selection, or lane contract changes.
5. The evidence schema or baseline generator changes.
6. A reviewed CI/test issue explicitly declares that its delivery establishes a new comparison point.

Ordinary timing fluctuation is not a refresh trigger. A baseline PR must cite the source run and triggering issue, include the generator command in the diff metadata, and receive Platform CI/Test Governance owner review. A failed, cancelled, zero-execution, or rerun-recovered attempt cannot become the baseline source; use the next clean first-attempt successful main run.

## GitHub Actions Summary

Each instrumented lane writes a compact Markdown summary containing:

1. lane selection and run/attempt identity;
2. assembly execution table with passed/failed/skipped/executed counts and duration;
3. slowest assemblies and tests Top N;
4. skip reasons grouped by classification and policy entry;
5. the three hard-gate results;
6. report-only baseline deltas;
7. report-only rerun classification;
8. artifact name and retention location.

The summary must explicitly distinguish `skipped` from `executed`, and `recovered-after-rerun` from a clean first-attempt pass.

## Script Governance

Every new PowerShell entry point and every PowerShell fixture/test dot-sources `scripts/lib/ScriptAutomation.ps1` from a resolved repository path.

The scripts obey these rules:

1. `collect-test-evidence.ps1` declares `Category: check` and its artifact writes.
2. `generate-test-evidence-baseline.ps1` declares `Category: generate` and names the committed baseline write.
3. Native execution, including any fixture process or future `dotnet` invocation, uses the appropriate `Invoke-*` helper; no script calls `dotnet`, `docker`, `pnpm`, `pwsh`, or `powershell` directly.
4. Sensitive diagnostics reuse `Protect-ScriptAutomationText` and `Protect-ScriptAutomationLogFile`.
5. All scripts pass `pwsh scripts/check-script-governance.ps1` and the existing governance fixture suite.
6. The collector does not mutate the policy or baseline. The generator does not run tests or modify normalized evidence.

Workflow YAML may invoke `dotnet test` directly because the script prohibition applies to repository scripts, but it must preserve the natural exit code and write only to the declared results directory.

## Testing Strategy

Implementation follows test-driven development. Synthetic fixtures prove behavior without requiring a real test solution or external service.

The fixture suite covers at least:

1. multiple TRX files aggregate into separate assemblies without filename collision;
2. test/project duration and passed/failed/skipped/executed counts are normalized correctly;
3. slowest assembly and test ordering is deterministic;
4. an unregistered runtime skip fails;
5. an unregistered source skip marker fails policy governance;
6. valid optional skip passes when its capability is not selected;
7. valid environment-gated skip passes when its required lane is not selected;
8. the same skip fails when the required real-dependency lane is selected;
9. a selected real-dependency lane with only skipped tests fails as zero execution;
10. a selected real-dependency filter matching no TRX test fails as zero execution;
11. quarantine without responsibility issue fails;
12. quarantine without expiry or exit condition fails;
13. expired quarantine fails;
14. timing delta and critical-path regression remain report-only;
15. attempt 1 failure plus attempt 2 pass is `recovered-after-rerun` and remains report-only;
16. attempt 2 without trustworthy prior evidence is not called recovered;
17. password, bearer token, authorization header, secret, credential URL, and business payload sentinels are absent from every retained format;
18. baseline output is deterministic and can only be produced through the generator;
19. `backend-shard-1` uses schema version 1 without a shard-specific field or migration;
20. failed test-step workflow structure retains `if: always()` collection and upload and contains no `continue-on-error`.

Verification for the implementation includes:

```powershell
pwsh scripts/tests/test-evidence.Tests.ps1
pwsh scripts/tests/check-script-governance.Tests.ps1
pwsh scripts/check-script-governance.ps1
```

The implementation also runs focused local backend and Connector Host test projects with temporary TRX directories to prove compatibility with real VSTest output. A full solution run is required only when dependencies and execution time permit; build, test execution, parser fixtures, and CI workflow inspection are reported as separate evidence.

## Delivery And Follow-Up Boundary

MAN-661 is complete when backend and Connector Host CI lanes produce retained redacted TRX plus machine-readable summaries, job summaries expose the requested facts, the three policy gates are executable, rerun attempts correlate, and the committed baseline is reproducibly generated.

MAN-669 reuses the collector and policy by adding shard lane names. It does not own TRX infrastructure. MAN-663 and other performance/isolation tasks consume the report-only baseline and must refresh it after their first clean main run when they intentionally establish a new comparison point.

The implementation does not create or change business-service HTTP endpoints, database schemas, OpenAPI snapshots, generated clients, or frontend product behavior.
