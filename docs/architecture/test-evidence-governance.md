# Test Evidence Governance

MAN-661 provides the repository-owned evidence path for backend and Connector Host VSTest runs. The owner is **Nerv-IIP Platform CI/Test Governance**. This document is the operator contract; the approved architecture remains in the MAN-661 design.

## Runtime and retained artifacts

CI runs `dotnet test` normally with `--logger trx`. Test steps have no `continue-on-error`, shell pipeline, or status-restoration wrapper, so their natural exit code remains authoritative. Collection and upload use `if: always()` so failed runs still publish diagnostics when normalized evidence exists.

Raw files live only at `artifacts/test-evidence-raw/<run>/attempt-<n>/<lane>/` during the job. The raw TRX, stdout, stderr, attachments, collector payloads, request/response bodies, and arbitrary result files are never uploaded. Retained artifacts are redacted and written to:

```text
artifacts/test-evidence/<run>/attempt-<n>/<lane>/
├── trx/                 # reconstructed, normalized TRX only
├── tests.jsonl          # one schema-v1 record per runtime test
├── summary.json
├── summary.md
└── diagnostics.log
```

Credential URL user info, bearer authorization, quoted or unquoted password/token/secret/client_secret values, PEM blocks, and named `customerName`, `phone`, `email`, and `address` fields are replaced before retention. Raw failed-test messages and unregistered skip reasons are omitted by construction; approved skip reasons are bounded to 512 characters. A collector failure still publishes the same retained bundle with `collectionStatus: failed`, an `evidence-collection-failed` summary, bounded diagnostics, and a nonzero exit. Retention is 14 days. GitHub permissions are only `actions: read` and `contents: read`.

## Schema v1

| Area | Required fields |
| --- | --- |
| Test record | `schemaVersion`, run identity, lane/project/assembly, method identity, bounded parameterized `displayName`, stable `definitionId`/`testInstanceId`, duration, outcome, approved `skipReason`, redaction count |
| Outcome | `passed`, `failed`, or `skipped` |
| Summary | run/job/runner/artifact provenance; passed/failed/skipped/executed/total per lane+assembly; summed test duration; separate TRX elapsed duration; slowest tests/assemblies; skip aggregation; baseline source and compatible report-only delta; redaction count; attempt classification; violations |
| Lane | `<family>` or `<family>-shard-<positive-integer>` |

`backend-shard-1` is therefore an ordinary schema-v1 lane. MAN-669 may add shard lane invocations and policy rows, but it must not introduce a shard envelope or a second collector.

## Skip policy

`scripts/test-evidence-policy.json` has `{ schemaVersion, lanes[], sources[], rules[] }`. A source row identifies one repository-relative C# `Skip =` assignment by path, one-based ordinal, and anchored source-reason pattern. A rule identifies the source, classification, anchored runtime test/reason patterns, an exact `testIdentities` set with `expectedRuntimeTestCount`, allowed lanes/OS, optional required lane, and quarantine metadata. Source/rule references are closed in both directions, so a new method using a shared Fact attribute cannot silently consume an existing class-wide budget.

Every runtime skip must match exactly one context-applicable rule. The current inventory is 40 source assignments:

- `optional`: a capability was not selected; selecting its capability lane makes that skip illegal.
- `environment-gated`: a real dependency was not selected; selecting `requiredLane` requires execution.
- `quarantined`: temporary only, with responsibility issue, ISO expiry date, and measurable exit condition.

There are exactly three semantic hard gates:

- `unregistered-skip`: missing, multiple, reason-mismatched, or contextually illegal skip match.
- `illegal-quarantine`: missing/invalid metadata or expired quarantine.
- `zero-execution`: a selected `realDependency: true` lane has no passed or failed runtime result; skipped is not execution.

Timing, trends, skip totals, baseline deltas, and `recovered-after-rerun` are report-only. A recovery label requires a prior failed job from the same workflow run, commit SHA, and lane plus a successful current native test step with nonzero execution, zero failed tests, and zero policy violations. When lookup is unavailable the summary says `prior-attempt-unavailable`; attempt number alone never proves recovery.

## Baseline ownership and refresh

The committed baseline was generated from the latest qualifying source available during implementation: GitHub Actions CI push to `main`, run `30819675007`, attempt 1, Backend Tests job `91706113150`, commit `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`, with successful run and job conclusions. It is a legacy console-import baseline at project granularity, not a timing gate.

Only `scripts/generate-test-evidence-baseline.ps1` may write `scripts/test-evidence-baseline.json`. The initial source command is:

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 -Repository Mang-X/Nerv-IIP -GitHubRunId 30819675007 -GitHubJobId 91706113150 -OutputPath scripts/test-evidence-baseline.json
```

After MAN-661 merges and normalized main-branch artifacts exist, refresh to test granularity with:

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json
```

Refresh is mandatory after MAN-663 changes shared BusinessGateway host profiles and after MAN-669 changes lane/shard topology. Other intentional test-topology changes should refresh only from the latest completed attempt-1 successful `main` CI push whose required jobs succeeded. Evidence-root summaries must agree on run, attempt, SHA, repository, event, and branch and must report successful native execution with no failures or violations; the generator verifies those facts against GitHub Actions. Baseline identity is lane plus assembly, and timing deltas are emitted only when both sides use TRX elapsed timing at test granularity. Commit the script-generated diff with its resolved runner image and actual .NET SDK provenance; never hand-edit the baseline.

Local fixture results, local full solution execution, PR CI and artifact availability, merge status, and post-merge test-granularity baseline refresh are separate delivery states. None implies another.
