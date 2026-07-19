# MAN-517 Acceptance Retry Observability Design

## Context

The ERP Sales Order Demand Acceptance job failed twice in GitHub Actions run
29684133999 while waiting 45 seconds for `SO-DEMO-001` version 2. The same code
later passed on `main` in run 29688202830. The available job log proves the HTTP
change command completed and the query never observed version 2 before cleanup,
but it does not retain enough state to distinguish ERP outbox publication, Redis
delivery, DemandPlanning CAP receipt, handler execution, or projection visibility.

DotNetCore.CAP 10.0.1 performs up to three immediate executions inside one
subscriber dispatch. Its failed-message processor runs every 60 seconds by
default, but only selects messages older than the default 240-second
`FallbackWindowLookbackSeconds`. The original 45-second gate therefore already
covered the immediate path but could not cover a message that exhausted it;
raising the gate to 90 seconds alone would still not cover the default fallback
path. The historical logs cannot identify which hop or exception exhausted the
immediate attempts in run 29684133999.

Follow-up issue: #981. This work references completed #958 and does not reopen or
auto-close #958 or #819.

## Chosen design

Keep production CAP retry defaults unchanged. ERP and DemandPlanning accept
validated optional CAP recovery settings; only the acceptance profile selects
the CAP-recommended 30-second minimum fallback lookback and a two-second failed
message scan interval. The 90-second convergence window covers that run-scoped
eligibility and scan budget with CI scheduling slack.

A deterministic PostgreSQL + Redis test subscriber fails all three immediate
executions of a version-2 changed event. It proves the durable projection remains
at version 1 with exactly three attempts for a five-second stability window,
then proves the background fallback scanner performs a fourth execution after
the lookback threshold and the real DemandPlanning handler persists version 2.

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

1. Only raise the wait to 90 seconds. This would cover neither the default
   240-second fallback eligibility nor identify a future failing hop.
2. Change production retry defaults globally. This would widen operational load
   and semantics without evidence that the production default is wrong.
3. Add a production fault-injection hook. This would contaminate service code
   for a test seam when CAP supports a test-assembly subscriber directly.

## Verification

- PowerShell contract tests fail before diagnostics/workflow upload exist and
  pass after implementation.
- The real PostgreSQL + Redis CAP test exhausts three immediate executions,
  remains at version 1 before eligibility, then passes on the fourth execution
  through the 30-second-lookback/two-second-scan test profile.
- The full acceptance script passes repeatedly against Docker PostgreSQL/Redis.
- DemandPlanning targeted tests, script governance, touched-file formatting, and
  the required backend solution gate are run before the ready PR is created; any
  unrelated baseline failure is isolated and reported with an exact rerun.
