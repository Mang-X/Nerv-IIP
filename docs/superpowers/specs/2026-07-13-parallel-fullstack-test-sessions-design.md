# Parallel Full-Stack Test Sessions Design

## Context

Nerv-IIP uses one platform-level Aspire AppHost as the canonical local topology. A full-stack run starts the platform services, business services, gateways, frontends and container resources required for realistic cross-service or browser verification.

Independent Codex sessions already work in separate git worktrees. `scripts/dev.ps1` detects linked worktrees and adds `aspire start --isolated`, so Aspire can randomize published ports and isolate user secrets. That solves the first layer of concurrency, but it does not yet provide a complete test-session lifecycle:

1. Full-stack endpoint addresses are not exported through a stable machine-readable session contract.
2. AppHost container resources use fixed persistent volume names such as `nerv-iip-postgres-18`, `nerv-iip-redis`, `nerv-iip-minio` and `nerv-iip-victoria-logs`; concurrent isolated AppHosts must not share those writable volumes.
3. `scripts/aspire-control.ps1` has a fallback that finds Aspire development containers by generic resource-name prefixes such as `postgres-*` and `redis-*`. In parallel runs, one session can therefore remove another session's containers.
4. A successful `aspire start` leaves the full topology running until an explicit stop. Failed, interrupted or abandoned agent sessions can accumulate Aspire, Node.js, .NET and Docker resources.
5. Full-stack runs are relatively expensive. The machine normally needs only two or three concurrent stacks, but admission must consider current free memory instead of assuming unlimited capacity.

The design governs only real full-stack verification. Unit tests, contract tests, frontend type checks, Vitest suites and existing focused infrastructure tests remain unchanged.

## Decision

Add a governed full-stack session layer to the root `nerv.ps1` CLI. Each session uses the existing AppHost with Aspire isolated mode, receives an automatically generated session ID, uses session-specific ephemeral storage, exports dynamic endpoints through a manifest, and owns a bounded cleanup lifecycle.

The design does not add a second AppHost or a parallel Compose service graph. Aspire remains the single topology source.

The design does not add a global Nginx reverse proxy. Test automation consumes the session's discovered Aspire endpoints directly. PlatformGateway and BusinessGateway remain the API edges inside each session.

## Goals

1. Allow two or three worktree sessions to run the real full stack concurrently without port, database, cache, object-storage or process-cleanup conflicts.
2. Make dynamic endpoint discovery transparent to HTTP and Playwright verification.
3. Release Aspire, Node.js, .NET and temporary Docker resources immediately after success, failure or timeout.
4. Recover stale sessions left by interrupted agents without affecting live sessions.
5. Preserve logs, traces, screenshots and test reports after runtime resources are removed.
6. Keep ordinary `nerv.ps1 dev` behavior and persistent local development data unchanged.

## Non-Goals

1. Migrating existing PostgreSQL profile tests to Testcontainers.
2. Changing unit, domain, contract, Vitest or type-check workflows.
3. Running every verification command through a full stack.
4. Adding a global Nginx, Traefik or other shared reverse-proxy registry.
5. Maintaining a second full-platform topology in Docker Compose.
6. Replacing Aspire CLI lifecycle commands with direct AppHost `dotnet run` invocations.
7. Cleaning unrelated Aspire applications, containers or processes owned by other repositories.

## Alternatives Considered

### Full AppHost Per Session Without Additional Governance

Rely only on `aspire start --isolated`. This provides randomized ports and isolated secrets, but it does not isolate the AppHost's explicitly named persistent volumes, provide test endpoint handoff, or guarantee cleanup after interrupted sessions.

### Shared Infrastructure And Global Nginx

Run one shared PostgreSQL, Redis, MinIO and message transport, then route each worktree through a shared Nginx instance. This reduces container startup cost, but introduces shared mutable state and a global route registry. Database reset, cache cleanup, message consumption, proxy reload and session shutdown can affect unrelated tests. The shared proxy also becomes a new correctness dependency.

### Governed Aspire Isolated Sessions

Use the existing AppHost, add session-aware ephemeral storage and labels, discover dynamic endpoints from Aspire, and manage the complete lifecycle through governed scripts. This is the selected approach because it preserves the canonical topology while removing shared state and cleanup ambiguity.

## Command Surface

The root CLI adds one command family:

```powershell
# Preferred automation path: start, wait, run, collect and clean up in finally.
.\nerv.ps1 fullstack run -Scenario smoke

# Interactive debugging path.
.\nerv.ps1 fullstack start
.\nerv.ps1 fullstack url business-console
.\nerv.ps1 fullstack status
.\nerv.ps1 fullstack logs gateway
.\nerv.ps1 fullstack stop

# Inspect or recover stale sessions from any checkout.
.\nerv.ps1 fullstack list
.\nerv.ps1 fullstack gc
```

`fullstack run` is the required path for agent-owned automated full-stack tests because the same process owns startup, scenario execution, diagnostics and `finally` cleanup.

`fullstack start` is reserved for interactive or investigative work. It creates a renewable lease and prints the session ID, expiration time and endpoint-discovery commands.

`run -Scenario` resolves a governed scenario name rather than accepting an arbitrary shell command. The initial `smoke` scenario waits for `gateway`, `business-gateway`, `console` and `business-console`, verifies the discovered HTTP endpoints, and fails if any Aspire project resource is already `Finished`. Later full-stack Playwright scenarios can register through the same controlled dispatch table without changing session ownership rules.

All native execution remains in governed scripts under `scripts/`. The root `nerv.ps1` file only dispatches arguments.

## Session Identity And Manifest

Every start generates a filesystem-safe session ID from the worktree identity plus a random suffix, for example:

```text
nerv-16d5-a82f31
```

The session ID must contain only lowercase ASCII letters, digits and hyphens and must stay short enough for Docker resource-name limits.

Session manifests live outside the git worktree. The state root is `%LOCALAPPDATA%\Nerv-IIP` on Windows and `${XDG_STATE_HOME:-$HOME/.local/state}/nerv-iip` on Linux/WSL:

```text
<state-root>/fullstack-sessions/<sessionId>.json
```

The manifest is created in `Creating` state before Aspire starts. It records:

1. Schema version and session ID.
2. State and state-transition timestamps.
3. Exact worktree root and AppHost project path.
4. Session mode, coordinator/guardian process IDs, process start times and lease expiration.
5. Aspire/DCP ownership identifiers available from startup output and container labels.
6. Session-owned process IDs, container IDs, network IDs and volume names.
7. Discovered resource endpoints.
8. Artifact directory and cleanup results.
9. Failure category and redacted diagnostic summary when applicable.

The manifest never stores connection strings, passwords, bearer tokens, private keys or secret parameter values.

Manifest updates use an exclusive cross-process file lock and atomic replace. Admission, state transitions and stale-session recovery must not race when multiple worktrees start simultaneously.

## Artifact Layout

Diagnostic artifacts stay in the owning worktree:

```text
artifacts/fullstack/<sessionId>/
├── summary.json
├── aspire-logs/
├── traces/
├── screenshots/
└── test-results/
```

Runtime cleanup does not delete these artifacts. Existing repository ignore and artifact-retention rules apply.

## AppHost Ephemeral Session Mode

The same AppHost supports two modes.

### Persistent Development Mode

Ordinary `nerv.ps1 dev` keeps existing behavior and existing persistent volumes. No session ID is required, and local development data survives normal stop/start cycles.

### Ephemeral Full-Stack Mode

The full-stack script starts Aspire with:

```text
NERV_IIP_SESSION_ID=<sessionId>
NERV_IIP_EPHEMERAL=true
```

and invokes:

```powershell
aspire start --isolated --format Json --apphost <exact-apphost-path> --non-interactive --nologo
```

In ephemeral mode:

1. PostgreSQL, Redis, MinIO and VictoriaLogs use volume names suffixed with the validated session ID.
2. Any optional RabbitMQ or other stateful container introduced into the selected AppHost profile follows the same rule.
3. Session-owned containers carry an ownership label equivalent to `com.nerv-iip.session=<sessionId>` through documented Aspire container customization.
4. Volumes and networks use session-scoped names or recorded IDs. They also use ownership labels when the documented Aspire/Docker API supports those labels; cleanup never relies on a generic resource prefix.
5. Persistent development volume names are never mounted.
6. The session may delete all session-owned volumes during cleanup because the mode is explicitly ephemeral.

The implementation must check the Aspire 13.4 API reference before adding container labels or session-aware volume configuration. It must not guess unsupported builder APIs.

## Dynamic Endpoint Discovery

Aspire isolated mode randomizes published ports. Fixed local ports are therefore not part of the full-stack test contract.

After startup, the session script parses the detached start result and uses Aspire's machine-readable describe output to resolve named resources. It waits through Aspire CLI before exposing a URL:

```powershell
aspire wait <resource> --apphost <exact-apphost-path> --non-interactive --nologo
```

The manifest stores URLs by stable resource name, including at least the resources required by the selected scenario. `fullstack url <resource>` reads the manifest and prints that URL. Before launching a governed scenario, the session runner maps discovered values into that child process using existing consumer names such as:

```text
NERV_IIP_GATEWAY_URL
NERV_IIP_BUSINESS_GATEWAY_URL
PLAYWRIGHT_BASE_URL
```

Playwright and HTTP scenarios receive these values from the manifest. They do not scan ports, assume `5100`/`5125`, or depend on a global reverse proxy.

## Session State Machine

The lifecycle uses these states:

```text
Creating -> Running -> Collecting -> Stopping -> Stopped
                   \-> Failed -------------------^
                              \-> CleanupFailed
```

Allowed transitions are explicit and test-protected. `stop` and `gc` are idempotent for `Stopped` sessions. A cleanup failure preserves the manifest and diagnostic details so a later `gc` can retry.

## Admission And Resource Budget

Full-stack admission is resource-aware:

1. The default safety ceiling is three active sessions.
2. A new session requires at least 4 GiB of free physical memory after stale manifests are reconciled. The governed helper reads this through Windows CIM or Linux `/proc/meminfo`; inability to measure memory fails admission with a clear diagnostic rather than silently overcommitting.
3. Both values are configurable through non-secret local settings:

```text
NERV_IIP_FULLSTACK_MAX_SESSIONS=3
NERV_IIP_FULLSTACK_MIN_FREE_GB=4
NERV_IIP_FULLSTACK_LEASE_MINUTES=90
NERV_IIP_FULLSTACK_CAPACITY_WAIT_MINUTES=10
NERV_IIP_FULLSTACK_GUARDIAN_INTERVAL_SECONDS=60
NERV_IIP_FULLSTACK_START_TIMEOUT_SECONDS=900
NERV_IIP_FULLSTACK_COLLECT_TIMEOUT_SECONDS=120
NERV_IIP_FULLSTACK_STOP_TIMEOUT_SECONDS=120
```

4. `fullstack run` may wait for capacity with a bounded timeout and report the active session IDs occupying capacity.
5. Interactive `fullstack start` fails fast when admission is denied and prints the same bounded diagnostic summary.
6. The admission decision and initial manifest creation happen under the cross-process session lock.

The ceiling is a safety guard, not a promise that three stacks will always fit. The free-memory gate can allow two stacks and reject a third when other applications consume the remaining capacity.

## Lease And Stale-Session Recovery

`fullstack run` is owned by its parent script and always enters cleanup through `finally`.

Every running session also has a lightweight lease guardian launched through the governed process helper. The guardian records its PID and process start time in the manifest, checks the coordinator and lease every 60 seconds by default, and invokes session-specific cleanup when the coordinator disappears or the lease expires. It exits after cleanup and is itself included in final process verification. This provides crash recovery without a global daemon or scheduled task.

Interactive `fullstack start` receives a 90-minute lease by default, and its guardian becomes the long-lived coordinator after the start command returns. `status`, `url` and `logs` renew a live session's lease. A session is stale when any of these conditions is true:

1. Its lease has expired.
2. Its recorded owner process no longer exists with the recorded process start time.
3. Aspire no longer reports the exact AppHost instance, but owned processes or Docker resources remain.
4. The manifest stayed in `Creating`, `Collecting` or `Stopping` beyond the phase timeout.

`fullstack gc` reconciles manifests against live Aspire, process and Docker state. A lightweight stale-session pass runs before every new full-stack start and after every cleanup. It never treats an unowned container name prefix as sufficient cleanup authority.

## Diagnostics And Cleanup

Cleanup runs after success, test failure, startup failure and timeout.

The ordered cleanup path is:

1. While resources are still available, collect bounded Aspire logs, telemetry export, scenario reports and browser artifacts.
2. Mark the manifest `Stopping` even if diagnostic collection partially failed.
3. Invoke `aspire stop --apphost <exact-apphost-path> --non-interactive --nologo` with a bounded timeout.
4. Wait for normal termination.
5. If normal stop fails, terminate only process trees recorded in the manifest or proven to belong to the exact worktree and recorded Aspire ownership chain.
6. Remove containers only when their recorded IDs and session ownership labels agree. Remove networks and volumes only when their recorded IDs or validated session-scoped names agree with the manifest; require ownership labels as an additional check where supported.
7. Verify that the session has no remaining process, container, network, volume or listening endpoint ownership.
8. Mark the manifest `Stopped`; use `CleanupFailed` if owned resources remain.

Diagnostic collection failures never prevent runtime cleanup. Each collection and cleanup phase has its own timeout.

The existing generic orphan cleanup in `scripts/aspire-control.ps1` must be replaced. Matching `postgres-*`, `redis-*`, `minio-*`, `rabbitmq-*` or similar names is not sufficient proof of ownership in a parallel environment. Ordinary persistent `nerv.ps1 stop` may use its exact AppHost path and the proven DCP/AppHost process ownership chain for fallback cleanup; when ownership cannot be proved, it reports the candidate resource and leaves it untouched.

`aspire stop --all` remains available only as an explicit human emergency operation. Automated agents, test scenarios and normal `nerv.ps1 stop` flows must not use it.

## Error Handling

1. Docker unavailable: fail before creating runtime resources and mark the provisional manifest `Failed` then `Stopped` after reconciliation.
2. Aspire start failure: collect bounded startup logs, discover resources carrying the session label, and clean them in `finally`.
3. Mixed human and JSON output from Aspire: isolate and parse the valid JSON payload; retain redacted raw output in artifacts when parsing fails.
4. Resource wait failure: report the failed resource and its latest logs, then clean the whole session.
5. Scenario failure: preserve the scenario exit code and artifacts, clean the session, then return the original scenario failure unless cleanup also failed.
6. Cleanup failure: return a distinct non-zero result, retain the manifest as `CleanupFailed`, and print the exact recovery command using the session ID.
7. Missing manifest: refuse broad cleanup. The emergency path requires an explicit AppHost or session ownership proof.
8. Repeated stop: succeed idempotently when no owned runtime resources remain.

## Components And Expected Files

Implementation is expected to affect these owned surfaces:

1. `nerv.ps1` for thin `fullstack` command dispatch.
2. `scripts/fullstack-session.ps1` for governed start/run/url/status/logs/stop/list/gc behavior.
3. A focused helper under `scripts/lib/` for manifest locking, state transitions, ownership checks and cleanup primitives when keeping all logic in one script would make it difficult to test.
4. `scripts/aspire-control.ps1` to remove unsafe global container cleanup and preserve repository-scoped ordinary development stop behavior.
5. `infra/aspire/Nerv.IIP.AppHost/Program.cs` for validated ephemeral volume names and session ownership metadata.
6. `scripts/tests/` for fast command, manifest, lease and ownership contract tests.
7. A governed real-infrastructure verification script for parallel Aspire session acceptance.
8. Current architecture and contributor documentation for the new full-stack workflow.

No backend business endpoint, Gateway contract, OpenAPI snapshot, generated API client or database migration changes are expected.

## Verification Strategy

### Fast Contract Tests

Fast tests use fixtures or mocks rather than starting Docker. They prove:

1. Session IDs and Docker resource names are valid and bounded.
2. Manifest writes are atomic and schema-versioned.
3. State transitions reject invalid movement and allow idempotent stop.
4. Admission is serialized across processes and honors capacity plus memory settings.
5. Lease renewal and stale detection use both PID and process start time, avoiding PID-reuse mistakes.
6. The guardian cleans an expired or abandoned session and exits without touching a live session.
7. Cleanup filters require exact session ownership and never accept a generic resource-name prefix.
8. Secrets and full connection strings are absent from manifests and logs.
9. Root CLI arguments route only to governed scripts.

### Real Parallel Acceptance

Add a governed verification entrypoint shaped as:

```powershell
scripts/verify-parallel-fullstack-isolation.ps1 -Sessions 2
scripts/verify-parallel-fullstack-isolation.ps1 -Sessions 3
```

The two-session run is the required acceptance gate. The three-session run is an explicit local capacity/stress gate.

Real acceptance proves:

1. Two worktree sessions can start concurrently.
2. Public endpoints are distinct and reachable through manifest discovery.
3. PostgreSQL, Redis, MinIO and VictoriaLogs storage names are distinct.
4. Session-local data written in one stack is not visible in the other.
5. Stopping one session leaves the other healthy.
6. A deliberately failed scenario still removes its processes, containers, networks and volumes.
7. A synthetic stale manifest is reclaimed without affecting a live session.
8. Repeated stop and GC remain idempotent.
9. Three sessions either pass when capacity permits or are rejected/queued cleanly without partial leaked resources.

### Required Repository Gates

Implementation must run the gates required by the affected areas:

```powershell
scripts/check-script-governance.ps1
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

It must also run the new fast script tests and the real two-session isolation acceptance. The three-session acceptance is required before changing the default safety ceiling above three, but remains optional for ordinary changes when local capacity is insufficient.

## Rollout Sequence

1. Replace the unsafe generic orphan cleanup with exact repository/session ownership and add contract tests before enabling parallel session commands.
2. Add manifest infrastructure, state transitions, locking, admission and idempotent cleanup.
3. Add AppHost ephemeral mode with session-specific volumes and ownership metadata while preserving ordinary persistent development mode.
4. Add root full-stack commands and machine-readable endpoint handoff.
5. Add the `smoke` managed scenario and real two-session isolation acceptance.
6. Add lease expiry, stale GC and failure-injection acceptance.
7. Validate the optional three-session capacity path and document local tuning settings.

Each rollout step must remain recoverable through the existing `nerv.ps1 stop` path or the new session-specific stop command. No step may require a global Docker prune or machine restart.

## Documentation Impact

Implementation must update:

1. Root development guidance with the distinction between persistent `dev` and ephemeral `fullstack` sessions.
2. `docs/architecture/deployment-baseline.md` for isolated full-stack development topology and temporary storage ownership.
3. `docs/architecture/script-automation-governance.md` for session manifests, leases and ownership-safe cleanup.
4. `docs/architecture/implementation-readiness.md` with the delivered command surface and current verification status after implementation.
5. Agent guidance only if agents need an explicit mandatory `fullstack run`/cleanup rule that is not already clear from the governed CLI.

Historical plans remain unchanged unless they are presented as current operational instructions.

## Success Criteria

1. Two independent worktrees can run the real Nerv-IIP full stack concurrently with no fixed-port or writable-storage collision.
2. Tests obtain all full-stack URLs from the session manifest.
3. Stopping or failing one session does not interrupt another live session.
4. Automated full-stack scenarios release all owned Aspire, Node.js, .NET and Docker runtime resources in `finally`.
5. The lease guardian and stale-session GC remove only resources with exact session ownership proof.
6. Diagnostic artifacts survive runtime cleanup and contain no secrets.
7. Ordinary persistent `nerv.ps1 dev` behavior and data volumes remain unchanged.
8. Script governance, AppHost build, fast session tests and the real two-session isolation gate pass.

## Design Self-Review

1. No unresolved marker or deferred decision is required to begin implementation planning.
2. The design preserves one canonical AppHost and does not introduce a competing topology.
3. Dynamic endpoint discovery, ephemeral storage, process ownership and cleanup use the same session ID boundary.
4. Full-stack concurrency is bounded by both a configurable ceiling and live memory capacity.
5. Failure handling preserves diagnostics but never keeps a failed stack alive for debugging.
6. The scope is limited to full-stack session conflicts and does not expand into unrelated test-framework migration.
