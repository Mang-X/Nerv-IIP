# MAN-517 Acceptance Retry Observability Design

## Context

The ERP Sales Order Demand Acceptance job failed twice in GitHub Actions run
29684133999 while waiting 45 seconds for `SO-DEMO-001` version 2. The same code
later passed on `main` in run 29688202830. The available job log proves the HTTP
change command completed and the query never observed version 2 before cleanup,
but it does not retain enough state to distinguish ERP outbox publication, Redis
delivery, DemandPlanning CAP receipt, handler execution, or projection visibility.

DotNetCore.CAP 10.0.1 documents a 60-second default failed-message retry scan.
Therefore the 45-second gate is incompatible with one legitimate transient
failure. This explains why the acceptance amplifies a transient failure, but it
does not identify which hop failed in run 29684133999.

Follow-up issue: #981. This work references completed #958 and does not reopen or
auto-close #958 or #819.

## Chosen design

Keep production CAP retry defaults unchanged. Extend the acceptance convergence
window to cover at least one default retry scan, while adding a deterministic
PostgreSQL + Redis CAP test profile with a two-second retry interval. A test-only
subscriber fails the first delivery of a version-2 changed event, delegates the
retry to the real DemandPlanning handler, and asserts both two delivery attempts
and the durable version-2 projection.

Before destructive cleanup, the acceptance script writes a run-scoped diagnostic
bundle. `Wait-Demand` retains the last HTTP status/body, exception, and last
observed version/quantity/status. Failure collection captures redacted service
log tails, CAP published/received state, DemandPlanning processed events, DLQ,
sales-order projection and demand-source rows, plus Redis stream/group/pending
metadata relevant to the run. Diagnostic failures are best-effort and cannot
replace the original acceptance failure.

GitHub Actions uploads the acceptance evidence, diagnostics, and managed process
logs under `if: always()`. Artifact retention is bounded and the bundle must not
contain bearer tokens, passwords, or full connection strings.

## Alternatives considered

1. Only raise the wait to 90 seconds. This would align with one retry scan but
   would neither prove retry convergence nor identify a future failing hop.
2. Change production retry defaults globally. This would widen operational load
   and semantics without evidence that the production default is wrong.
3. Add a production fault-injection hook. This would contaminate service code
   for a test seam when CAP supports a test-assembly subscriber directly.

## Verification

- PowerShell contract tests fail before diagnostics/workflow upload exist and
  pass after implementation.
- The real PostgreSQL + Redis CAP test first fails under the 60-second default
  inside its bounded assertion, then passes with the two-second test profile.
- The full acceptance script passes repeatedly against Docker PostgreSQL/Redis.
- DemandPlanning targeted tests, script governance, touched-file formatting, and
  the required backend solution gate are run before the ready PR is created; any
  unrelated baseline failure is isolated and reported with an exact rerun.
