# Test Evidence Governance

MAN-661 provides the repository-owned evidence path for backend and Connector Host VSTest runs. The owner is **Nerv-IIP Platform CI/Test Governance**. This document is the operator contract; the approved architecture remains in the MAN-661 design.

## Runtime and retained artifacts

CI runs `dotnet test` normally with `--logger trx`. Test steps have no `continue-on-error`, shell pipeline, or status-restoration wrapper, so their natural exit code remains authoritative. Collection and upload use `if: always()` so failed runs still publish diagnostics when normalized evidence exists.

Since MAN-669 the backend fast gate runs as four shard jobs instead of one. Each shard job runs `scripts/run-backend-test-shard.ps1`, which emits `trx;LogFilePrefix=<job id>` into its own job-local raw directory, and each job then invokes the same single-lane collector for the one lane it owns:

| Lane | CI job | Shard |
| --- | --- | --- |
| `backend-shard-1` | `Backend Tests - BusinessGateway` | `business-gateway` |
| `backend-shard-2` | `Backend Tests - Platform` | `platform` |
| `backend-shard-3` | `Backend Tests - Business Core A` | `business-core-a` |
| `backend-shard-4` | `Backend Tests - Business Core B` | `business-core-b` |
| `connector-host` | `Connector Host Tests` | — |

`Backend Tests` remains the stable required aggregate. It runs no tests, owns no evidence lane, and only asserts that shard governance and all four shard jobs succeeded. `scripts/verify-backend-test-shards.ps1` enforces this wiring structurally: the lane/job binding, the raw-only results directory, the exact collector arguments, and exactly one redacted evidence artifact per shard job. A shard job that uploaded its raw directory, claimed a sibling lane, piped the runner through a shell, or downgraded collection to `success()` fails that gate.

A shard that fails or times out prints its buffered stdout/stderr to the Actions job log **after redaction**; those buffers are never written to an uploaded file. `run-backend-test-shard.ps1` excludes real-dependency selectors with `FullyQualifiedName!~`, so those tests are absent from shard TRX rather than present as registered skips — skip-policy enforcement for them stays with `scripts/verify-backend-real-postgres-tests.ps1`.

Raw files live only at `artifacts/test-evidence-raw/<run>/attempt-<n>/<lane>/` during the job. The raw TRX, stdout, stderr, attachments, collector payloads, request/response bodies, and arbitrary result files are never uploaded. Retained artifacts are redacted and written to:

```text
artifacts/test-evidence/<run>/attempt-<n>/<lane>/
├── trx/                 # reconstructed, normalized TRX only
├── tests.jsonl          # one schema-v1 record per runtime test
├── summary.json
├── summary.md
└── diagnostics.log
```

Credential URL user info, bearer authorization, quoted or unquoted password/token/secret/client_secret values, PEM blocks, and named `customerName`, `phone`, `email`, and `address` fields are replaced before retention. Parameterized display-name values named `body`, `requestBody`, or `responseBody` are matched case-insensitively and replaced with a non-reversible 16-hex digest marker; nested/escaped values and multiple body parameters are bounded structurally, while method identity and non-body parameters remain available for skip-policy matching and instance distinction. Raw failed-test messages and unregistered skip reasons are omitted by construction; approved skip reasons are bounded to 512 characters. A collector failure publishes a retained bundle with `collectionStatus: failed`, an `evidence-collection-failed` summary, allowlisted and bounded run identity/diagnostics, and a nonzero exit. If the requested output directory already contains files, the failure bundle uses the first free deterministic sibling (`.failure`, `.failure-2`, ...) and reports that exact path through the collector step output; upload consumes that output and never overwrites the pre-existing directory. Retention is 14 days. GitHub permissions are only `actions: read` and `contents: read`.

## Schema v1

| Area | Required fields |
| --- | --- |
| Test record | `schemaVersion`, `workflowRunId`, `runAttempt`, explicit `headSha` and `testedSha`, lane/project/assembly, method identity, bounded parameterized `displayName`, stable `definitionId`/`testInstanceId`, exact `durationTicks` plus derived `durationMilliseconds`, outcome, approved `skipReason`, redaction count |
| Outcome | `passed`, `failed`, or `skipped` |
| Summary | run/job/runner/artifact name, retention days, and retention location; `selectedLanes` plus `selectedLaneResults`; passed/failed/skipped/executed/total per logical selected lane and lane+assembly; summed test duration; separate TRX elapsed duration; slowest tests/assemblies; skip aggregation; concrete baseline source and compatible report-only delta or structured `unavailableReason`; redaction count; attempt classification; violations |
| Lane | `<family>` or `<family>-shard-<positive-integer>` |

`headSha` is the branch head reported by the GitHub event. `testedSha` is the commit actually checked out and tested (`git rev-parse HEAD`). On `pull_request`, `testedSha` may be GitHub's synthetic merge commit and therefore differ from `headSha`; on the supported non-PR `push` event they must be identical. Both EvidenceRoot refresh and the legacy GitHub-console import derive `testedSha` from the independently downloaded job log instead of copying the run head or trusting an artifact to certify itself. Current workflows log `tested-sha=<sha>` after checkout; historical-console compatibility accepts only the exact checkout `git log -1 --format=%H` command and its following SHA. A missing or conflicting checkout authority fails closed. PR checkout provenance may retain distinct branch-head and synthetic-merge SHAs, but a PR run is not eligible to generate the committed baseline. Normalized TRX root attributes carry both values and must match parser run metadata.

`testInstanceId` prefers the persisted TRX `executionId`; only source TRX without a valid execution ID uses the deterministic fallback. `durationTicks` is the reversible 100 ns representation used to reconstruct normalized TRX, avoiding floating-point millisecond drift. `backend-shard-1` is therefore an ordinary schema-v1 lane. MAN-669 added shard lane invocations only: no shard envelope, no second collector, and no change to the record or summary schema.

Normalized TRX is a deterministic retained interchange format, not the original runner timeline. Its `Times` element uses the fixed synthetic start `2000-01-01T00:00:00Z` and derives finish only from retained TRX elapsed duration; consumers must not interpret those timestamps as wall-clock execution time. Raw TRX remains job-local and is never uploaded.

## Skip policy

`scripts/test-evidence-policy.json` has `{ schemaVersion, lanes[], sources[], rules[] }`. A source row identifies one repository-relative C# `Skip =` assignment by path, one-based ordinal, and anchored source-reason pattern. A rule identifies the source, classification, anchored runtime test/reason patterns, an exact `testIdentities` set with `expectedRuntimeTestCount`, allowed lanes/OS, optional required lane, and quarantine metadata. Source/rule references are closed in both directions, so a new method using a shared Fact attribute cannot silently consume an existing class-wide budget.

Every runtime skip must match exactly one context-applicable rule. The current inventory is 40 source assignments:

- `optional`: a capability was not selected; selecting its capability lane makes that skip illegal.
- `environment-gated`: a real dependency was not selected; selecting `requiredLane` requires execution.
- `quarantined`: temporary only, with responsibility issue, ISO expiry date, and measurable exit condition.

Each source assignment is registered by repository-relative file plus one-based `Skip =` ordinal and anchored reason. This deliberately prevents a shared Fact attribute from silently expanding a runtime budget, but it has an explicit maintenance cost: inserting an earlier `Skip =` in the same file shifts later ordinals, so the author must review and update every affected source row.

There are exactly three semantic hard gates:

- `unregistered-skip`: missing, multiple, reason-mismatched, or contextually illegal skip match.
- `illegal-quarantine`: missing/invalid metadata or expired quarantine.
- `zero-execution`: a selected `realDependency: true` lane has no passed or failed runtime result; skipped is not execution.

The collector is a single-lane collector: one invocation owns one physical `-Lane`. `-SelectedLanes` may name that physical shard or its logical base lane; sibling shard selectors must not be used to claim that the invocation certified each sibling. Zero-execution groups multiple selected sibling selectors by logical base only to avoid duplicate/false sibling failures, recognizes a base selector's current-shard execution, and still fails when the selected current shard truly has no passed/failed result. MAN-669 added lane names and invocations but did not change this collector contract. Current CI wires `backend-shard-1` … `backend-shard-4` and `connector-host`, all `realDependency: false`; PostgreSQL, FullChain, performance, and connector real-dependency job wiring remains follow-up work. The CI-wired contract suite proves the zero-execution function, but that is not evidence that a real-dependency job has run.

Timing, trends, skip totals, baseline deltas, and `recovered-after-rerun` are report-only. A recovery label requires an authenticated GitHub Actions lookup of the exact prior attempt and the lane's allowlisted job name, matching workflow run, current attempt, and branch-head SHA, with a failed prior job plus a successful current native test step with nonzero execution, zero failed tests, and zero policy violations. `Get-NervTestEvidenceLaneJobs` is the single allowlist behind both the recovery label and baseline authority; each lane is bound to exactly one job name, so a shard can never certify a sibling and the test-free `Backend Tests` aggregate can never certify a lane. Rerun classification is therefore per shard: one shard recovering after a rerun does not relabel the others. The production collector exposes no caller-supplied or test-only authority replacement parameter; tests call the pure response validator directly. When lookup is unavailable the summary says `prior-attempt-unavailable`; attempt number alone never proves recovery.

## Baseline ownership and refresh

The committed baseline was generated from the latest qualifying source available during implementation: GitHub Actions CI push to `main`, run `30819675007`, attempt 1, Backend Tests job `91706113150`, head/tested commit `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`, with successful run and job conclusions. The authoritative Actions job log resolves the tested checkout from its historical `git log -1 --format=%H` output, the hosted runner to `ubuntu24@20260720.247.2`, and the SDK to `10.0.302`; selectors such as `ubuntu-latest` or `10.0.x` are rejected as baseline provenance. It is a legacy console-import baseline with `granularity: project` and `durationMetric: project-wall-clock`, so it is not comparable with test-granularity `trx-elapsed` evidence and is not a timing gate.

Only `scripts/generate-test-evidence-baseline.ps1` may write `scripts/test-evidence-baseline.json`. The initial source command is:

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 -Repository Mang-X/Nerv-IIP -GitHubRunId 30819675007 -GitHubJobId 91706113150 -OutputPath scripts/test-evidence-baseline.json
```

After MAN-661 merges and normalized main-branch artifacts exist, refresh to test granularity with:

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json
```

Refresh is mandatory after MAN-663 changes shared BusinessGateway host profiles and after MAN-669 changes lane/shard topology. MAN-669 has now changed that topology: the committed baseline's 64 rows are all `lane: backend`, and no job emits that lane any more, so every shard comparison additionally reports `lane-assembly-not-in-baseline` until the refresh lands. The legacy `-Repository/-GitHubRunId/-GitHubJobId` console import can no longer produce a usable baseline either — the `Backend Tests` job it targets is now the test-free aggregate — so the EvidenceRoot command below is the only supported refresh path. That refresh requires the four shard evidence artifacts plus the Connector Host artifact from one qualifying run; `Assert-NervEvidenceRootAuthority` rejects a partial backend shard family so a baseline cannot silently cover one shard. Other intentional test-topology changes should refresh only from the latest completed attempt-1 successful `main` CI push whose required jobs succeeded. Until the first qualifying post-merge normalized main run refreshes the file to `granularity: test` and `durationMetric: trx-elapsed`, every comparison remains report-only unavailable with `unavailableReason: incompatible-granularity-or-duration-metric`; Markdown prints that reason and never renders empty `baseline=ms, delta=%` placeholders. This PR therefore does not satisfy or claim an actual relative-timing acceptance result. Evidence-root summaries must have complete, nonempty, mutually consistent run/attempt/head SHA/tested SHA/repository/event/branch/source URL/runner OS/resolved runner image/exact SDK provenance, unique valid lanes, the allowlisted lane-to-job mapping, successful native execution, nonzero execution, and no failures or violations. The production generator exposes no Actions-fixture or other authority replacement parameter. Both generator paths use the same checkout-provenance validator: it verifies push head/tested equality while preserving distinct PR head/tested values for validation, then independently enforces that only `main` push evidence is baseline-eligible. EvidenceRoot additionally verifies run URL/workflow/event/branch/head SHA/attempt/conclusion, the latest entry's matching ID/head SHA/attempt/conclusion/event/branch, every required job, and each job log's tested SHA, runner OS/image/version, and exact SDK. Tests exercise the pure validators with fixture objects. Baseline identity is lane plus assembly, and timing deltas are emitted only when both sides use TRX elapsed timing at test granularity. Commit the script-generated diff with its resolved runner image and actual .NET SDK provenance; never hand-edit the baseline.

The Script Governance CI job and `check-script-compatibility.ps1 -FastOnly` both execute `scripts/tests/test-evidence.Tests.ps1` directly, so semantic contract failures propagate their actual process exit code instead of relying on source scanning. `summary.json` and Job Summary expose the same selected lane selectors and logical result rows.

Successful retained artifacts intentionally replace a failed test's raw message with a bounded privacy-safe placeholder and keep `diagnostics.log` empty; the failure root remains in the access-controlled Actions job log. This is a deliberate privacy boundary, not a claim that retained artifacts contain full failure diagnostics.

Local fixture results, local full solution execution, PR CI and artifact availability, merge status, actual real-dependency lane execution, and post-merge test-granularity baseline refresh are separate delivery states. None implies another.
