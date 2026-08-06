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

A shard that fails or times out prints its buffered stdout/stderr to the Actions job log **after redaction**; those buffers are never written to an uploaded file.

`run-backend-test-shard.ps1` excludes real-dependency selectors with `FullyQualifiedName!~`, so those tests are absent from shard TRX rather than present as registered skips. Two gates keep that exclusion honest instead of turning it into a private escape hatch:

- **Policy closure and owner-lane derivation.** Every fast-shard exclusion selector must resolve to at least one policy test identity whose rule is `environment-gated` with a real-dependency `requiredLane`. A test cannot be dropped from the default gate unless this document's skip policy already registers it. The shard's declared `excludedTestLanes` must then equal the heavy lanes those `requiredLane` values map to (via `heavyLanes[].policyLane`), so a shard cannot attribute a `full-chain` exclusion to the real-PostgreSQL owner script. All 49 current selectors resolve to `postgres` or `full-chain`; enforcement lives in `verify-backend-test-shards.ps1`. Passed-execution proof stays with the opt-in `scripts/verify-backend-real-postgres-tests.ps1`, which is a registration-level separation only — the heavy lanes are not wired into CI yet.
- **Selector anchoring.** VSTest `!~` is a substring match, so a class selector is emitted with a trailing dot (`FullyQualifiedName!~Ns.XTests.`) and cannot swallow a sibling class that merely shares its prefix. Method selectors stay unanchored so parameterized cases keep matching; governance compensates by scanning the registered MAN-661 source file and rejecting any method selector whose name is a prefix of another member in that file.
- **Per-project execution.** After a shard runs, every project it classifies must appear in the shard's own TRX with at least one executed result, and the shard must not execute an assembly it does not classify. This reads the same `UnitTest/@storage` attribute the collector uses. It deliberately does **not** scan dotnet console text: that text is localized, so a phrase match fails open on any non-English runner — the exact silent pass this boundary exists to prevent.

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

## CI timeout budgets

Evidence collection is only reachable if the job survives long enough to reach it. A job-level `timeout-minutes` cancels the **whole** job, `if: always()` steps included, so a job that hits its own budget publishes no evidence at all — the MAN-799 `Connector Host Tests` hang burned 28 minutes and produced nothing.

Two rules apply to every job in `.github/workflows/ci.yml`, and a third applies only where evidence exists:

1. **Every job declares `timeout-minutes`.** A job without one inherits GitHub's 360-minute default and can burn a full runner hour-block on a deadlock.
2. **Every explicit step declares `timeout-minutes`** — checkout, SDK/pnpm setup, cache restore, and the evidence collection/upload steps included. `if: always()` does not exempt a step from having a budget.
3. **A job that publishes evidence keeps the sum of its step budgets strictly below its job budget.** This is what makes its job budget unreachable in practice: some step must exceed its own budget first, and a step timeout fails only that step, so the job continues into `Collect …` / `Upload …` and the redacted bundle is still published. A single unbudgeted step reopens the path where the job budget fires first and takes the artifacts with it.

Rule 3 is deliberately scoped. The four backend fast shards (`backend-tests-business-gateway`, `backend-tests-platform`, `backend-tests-business-core-a`, `backend-tests-business-core-b`), `erp-sales-order-demand-acceptance`, and `connector-host-tests` have `if: always()` collection/upload steps and therefore something to lose, so their job budgets are inflated well past any real runtime to clear the step sum. `backend-tests` (the test-free aggregate that MAN-669 left behind), `backend-test-shard-governance`, `frontend`, `openapi-client-drift`, and `script-governance` have no step that can run after an earlier one failed: nothing is preserved by outliving their own budget, so inflating them past the step sum would only convert a fast red into a slow red.

In tier B the **job** budget is therefore the fail-fast bound, sized from observed runtime, and the step budgets are per-step upper bounds only. They are deliberately *not* claimed to be individually reachable: `frontend` sums 58m of step budget inside a 20m job, so its 15m build budget can never fire on its own — the job budget always arrives first. The single tier-B rule enforced by the gate is the case that is dead under every schedule: a step budget at or above the whole job budget. Step budgets are kept in both tiers, in tier B as documentation of what a healthy step costs and as the bound that does fire when a job has only one long step.

### How a job's tier is decided

The gate reads each step's `if:` and asks one question: can this step still run after an earlier step in the same job failed or timed out? `always()`, `!cancelled()` and `failure()` all answer yes, in any legal spelling — `${{ always() }}`, `always() && github.event_name == 'push'`, a trailing `# comment`. Per GitHub's own rule an `if:` expression containing none of the status-check functions is evaluated as `success() && (expression)` and answers no.

Classification fails **closed**: anything the reader cannot decide from the step itself — an `if:` continued on later lines as a block scalar, a YAML alias, an unrecognized function call — is treated as tier A, the stricter tier. The first version of this gate matched only the literal string `always()`, which quietly demoted a job to tier B for every other spelling and switched off rule 3 entirely; the current classification is covered by a fixture per spelling, including negative controls that must *not* promote.

### What the step budgets are, and what the margin covers

Step budgets are generous round upper bounds rather than a uniform multiple of anything: long steps (`dotnet test`, `pnpm build`, the verify scripts) are roughly 2x their observed maximum over the recent run history, while short fixed-cost steps such as checkout, `setup-dotnet`/`setup-node`/`pnpm/action-setup`, and cache restore get a 3–8 minute floor no healthy run comes near. Reading "~2x observed" onto a 10-second checkout would be wrong.

A budget is only a fail-fast bound while the observation behind it is still true. When a change makes a step materially faster, its budget is re-derived from the new measurement rather than inherited — otherwise the job keeps a bound sized for a runtime that no longer exists, which is indistinguishable from having no bound. MAN-663 is the worked example: it took the BusinessGateway shard's test step from 14.7m to 1.0m, so that step moved from the "~2x observed" rule to the fixed-cost floor (35m → 8m) and its job budget from 70m to 43m. Note what the tier-A invariant does *not* let you do: the job budget can never drop below the sum of the mandatory step budgets, so 43m is a structural floor, not a runtime claim. The budget that actually fires on a hung shard is the 8m step budget.

The same rule applied a second time when MAN-669 PR-A rebalanced the shard contents. The old per-shard spread (BusinessGateway 8m, Platform 10m, Business Core A 15m, Business Core B 12m) was derived from a topology in which one shard carried 357s of TRX elapsed and another 23s. Re-homing projects by measured cost removed that spread. Across three runs of the branch (`31114441118`, `31115903098`, `31116998822`) the shards measured test steps of 3.5m / 3.0m / 4.2m / 2.4m, then 4.5m / 3.0m / 4.2m / 3.3m, then 4.7m / 3.1m / — / 3.1m. Hosted-runner variance on the *same commit* is tens of percent and moves which shard tops the list, which is itself the argument for one shared budget rather than four: per-shard budgets would encode noise. All four now take a 10m test-step budget (~2x the 4.7m maximum across the three runs), a 39m step sum and a 45m job budget. A budget inherited across a topology change is the same failure mode as a budget inherited across a speed-up: it describes something that no longer exists.

Rule 2 covers the **explicit** steps in `steps:` — the only ones that can carry `timeout-minutes` at all. GitHub also runs steps that are not in `steps:` and cannot be budgeted: `Set up job` before the first step, and the implicit post steps of composite actions (`actions/cache`'s post-save, `setup-node`'s post-cache). The job budget starts before `Set up job` and keeps running through the post steps, so a job's remaining margin — job budget minus step sum — has to absorb them. Current margins are 6m on each of the four backend fast shards, 9m (`erp-sales-order-demand-acceptance`) and 9m (`connector-host-tests`) against implicit overhead observed in the tens of seconds.

The evidence conclusion survives this gap: implicit post steps are scheduled **after** the last explicit step, so they run after `Upload … test evidence` has already published the artifact. They can consume job budget; they cannot cost evidence. What they can do is push a job into its job budget even though every step stayed inside its own — which is why the margin, not just the strict inequality, is part of the design.

### Enforcement

This is not a comment-only convention. `scripts/lib/CiWorkflowBudgets.ps1` reads the workflow structurally and `scripts/tests/test-evidence.Tests.ps1` fails on `missing-job-timeout`, `missing-step-timeout`, `evidence-job-budget-not-above-step-sum`, or `job-budget-not-above-largest-step`; the Script Governance CI job runs that suite directly, so a violation propagates a real nonzero exit. The reader cross-checks its parsed step count against the raw file and throws on a mismatch, so a workflow it cannot parse fails closed instead of reporting zero violations; a job header it cannot read throws for the same reason, because skipping it would merge that job's steps and budget into the previous job. The cross-check resolves each six-space sequence item against its own enclosing job-level key rather than trusting indentation alone — `needs:` and `strategy.matrix` items sit at exactly the same column as step entries, and counting them as steps turned every workflow that uses them into a hard red naming a parse error instead of the real finding. The same suite carries negative fixtures for each violation code and for each `if:` spelling, so "zero violations on `ci.yml`" is a result and not a vacuous pass.

Consequences for anyone editing the workflow: adding or reordering a step in a tier-A job means adding its budget **and** raising the job budget to keep the sum strictly below it; adding any step to a tier-B job whose `if:` can run after a failure — in any spelling — promotes that job to tier A, and its budget has to be raised accordingly.

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

Timing, trends, skip totals, baseline deltas, and `recovered-after-rerun` are report-only. A recovery label requires an authenticated GitHub Actions lookup of the exact prior attempt and the lane's allowlisted job name, matching workflow run, current attempt, and branch-head SHA, with a failed prior job plus a successful current native test step with nonzero execution, zero failed tests, and zero policy violations. `Get-NervTestEvidenceLaneJobs` is the single allowlist behind both the recovery label and baseline authority. It has exactly five entries — the four backend shard lanes and `connector-host` — and each is bound to exactly one job name, so a shard can never certify a sibling. The unsharded `backend` lane is deliberately **absent**: since MAN-669 no job produces it, and leaving it mapped to `Backend Tests` would have let the test-free aggregate certify a lane it never ran. `backend` remains a valid logical base lane for `-SelectedLanes` and for the policy's `allowedLanes`; it is simply no longer certifiable. Rerun classification is therefore per shard: one shard recovering after a rerun does not relabel the others. The production collector exposes no caller-supplied or test-only authority replacement parameter; tests call the pure response validator directly. When lookup is unavailable the summary says `prior-attempt-unavailable`; attempt number alone never proves recovery.

## Baseline ownership and refresh

The committed baseline was generated from the latest qualifying source available during implementation: GitHub Actions CI push to `main`, run `30819675007`, attempt 1, Backend Tests job `91706113150`, head/tested commit `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`, with successful run and job conclusions. The authoritative Actions job log resolves the tested checkout from its historical `git log -1 --format=%H` output, the hosted runner to `ubuntu24@20260720.247.2`, and the SDK to `10.0.302`; selectors such as `ubuntu-latest` or `10.0.x` are rejected as baseline provenance. It is a legacy console-import baseline with `granularity: project` and `durationMetric: project-wall-clock`, so it is not comparable with test-granularity `trx-elapsed` evidence and is not a timing gate.

Only `scripts/generate-test-evidence-baseline.ps1` may write `scripts/test-evidence-baseline.json`. The initial source command is:

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 -Repository Mang-X/Nerv-IIP -GitHubRunId 30819675007 -GitHubJobId 91706113150 -OutputPath scripts/test-evidence-baseline.json
```

That console command is historical only; it can no longer produce a usable baseline (see below). The committed baseline has been test-granularity since the 2026-08-05 refresh, produced with:

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json
```

Refresh is mandatory after MAN-663 changes shared BusinessGateway host profiles and after MAN-669 changes lane/shard topology. Both refreshes are now done: run `30999368607` (main push, merge commit `92d7f1ddc`, attempt 1, success) was the first qualifying post-merge run carrying the full `backend-shard-1`..`backend-shard-4` plus `connector-host` artifact set, and the 2026-08-05 refresh replaced the 64 `lane: backend` project-wall-clock rows with 71 lane+assembly rows at `granularity: test` / `durationMetric: trx-elapsed`. Comparisons are therefore `available` again — verified by re-running the collector over that run's own shard-1 evidence (`unavailableReason: null`, self-delta 0.0%) — and remain report-only. The legacy `-Repository/-GitHubRunId/-GitHubJobId` console import can no longer produce a usable baseline — the `Backend Tests` job it targets is now the test-free aggregate — so the EvidenceRoot command above is the only supported refresh path. That refresh requires the four shard evidence artifacts plus the Connector Host artifact from one qualifying run; `Assert-NervEvidenceRootAuthority` rejects a partial backend shard family so a baseline cannot silently cover one shard. Other intentional test-topology changes should refresh only from the latest completed attempt-1 successful `main` CI push whose required jobs succeeded. When a committed baseline is not test-granularity `trx-elapsed`, every comparison is report-only unavailable with `unavailableReason: incompatible-granularity-or-duration-metric`; Markdown prints that reason and never renders empty `baseline=ms, delta=%` placeholders. Contract tests assert that unavailable rendering against an explicitly constructed project-granularity baseline, assert the available path's exact signed delta, and additionally require the committed baseline to stay `test`/`trx-elapsed`, to cover every authenticated lane, and to carry only positive durations. Evidence-root summaries must have complete, nonempty, mutually consistent run/attempt/head SHA/tested SHA/repository/event/branch/source URL/runner OS/resolved runner image/exact SDK provenance, unique valid lanes, the allowlisted lane-to-job mapping, successful native execution, nonzero execution, and no failures or violations. The production generator exposes no Actions-fixture or other authority replacement parameter. Both generator paths use the same checkout-provenance validator: it verifies push head/tested equality while preserving distinct PR head/tested values for validation, then independently enforces that only `main` push evidence is baseline-eligible. EvidenceRoot additionally verifies run URL/workflow/event/branch/head SHA/attempt/conclusion, the latest entry's matching ID/head SHA/attempt/conclusion/event/branch, every required job, and each job log's tested SHA, runner OS/image/version, and exact SDK. Tests exercise the pure validators with fixture objects. Baseline identity is lane plus assembly, and timing deltas are emitted only when both sides use TRX elapsed timing at test granularity. Commit the script-generated diff with its resolved runner image and actual .NET SDK provenance; never hand-edit the baseline.

The Script Governance CI job and `check-script-compatibility.ps1 -FastOnly` both execute `scripts/tests/test-evidence.Tests.ps1` directly, so semantic contract failures propagate their actual process exit code instead of relying on source scanning. `summary.json` and Job Summary expose the same selected lane selectors and logical result rows.

Successful retained artifacts intentionally replace a failed test's raw message with a bounded privacy-safe placeholder and keep `diagnostics.log` empty; the failure root remains in the access-controlled Actions job log. This is a deliberate privacy boundary, not a claim that retained artifacts contain full failure diagnostics.

Local fixture results, local full solution execution, PR CI and artifact availability, merge status, actual real-dependency lane execution, and post-merge test-granularity baseline refresh are separate delivery states. None implies another.
