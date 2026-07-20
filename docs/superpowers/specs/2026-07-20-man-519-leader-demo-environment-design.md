# MAN-519 Leader Demo Environment Design

## Scope

Deliver only Linear MAN-519 / GitHub #960: a repeatable leader-demo environment with governed `start`, `reset`, `seed`, `health-check`, and cleanup commands. The environment must prepare the frozen cases `SO-DEMO-001`, `WO-DEMO-Q01`, and `DEV-CNC-DEMO` / `MWO-DEMO-001`, use PostgreSQL plus Redis cross-process messaging, preserve diagnostic evidence, and never seed final business outcomes.

MAN-520 acceptance execution, presentation governance, and repairs to unrelated business-chain gaps remain out of scope.

## Code Facts

- Aspire AppHost is the canonical full topology. Governed isolated full-stack sessions already provide per-session PostgreSQL, Redis, MinIO, endpoint discovery, leases, exact ownership, diagnostics, and cleanup.
- Full-stack sessions currently inherit the messaging provider from the caller. They do not prove Redis in their manifest and only wait for a small browser-smoke subset.
- ERP already has an idempotent startup seed for the released sales order `SO-DEMO-001`.
- MasterData, Quality, and Maintenance have startup seed services, but the frozen quality/equipment cases and the supporting production version and raw-material inventory are incomplete.
- Business service final states are reached through commands and integration events. Startup seeds can safely create master/reference data and open/released work, but must not create inspection conclusions, NCR dispositions, shipments, receivables, or completed repairs.
- Interactive `fullstack start` is diagnostic-only for agents. A leader-demo operator workflow needs a dedicated governed wrapper that owns its current session and can reset or stop it explicitly.

## Considered Approaches

1. **Governed demo profile on the existing full-stack session layer (chosen).** Add a thin `nerv.ps1 demo` command family that creates one isolated full-stack session with Redis forced, uses opt-in startup seeds for prerequisites, checks Aspire and public business facts, and writes a separate evidence manifest. Reset stops the exact prior session and starts a fresh one. This preserves one AppHost and exact cleanup ownership.
2. **A second demo Compose/AppHost topology.** This would duplicate ports, service lists, secrets, migrations, and cleanup rules and would drift from the canonical deployment model.
3. **A monolithic script that writes service databases directly.** This would bypass domain constructors and service-owned seed rules, create cross-schema knowledge in automation, and make idempotency and business-state boundaries difficult to audit.

## Command and Lifecycle Contract

The root CLI adds:

```powershell
.\nerv.ps1 demo start
.\nerv.ps1 demo reset
.\nerv.ps1 demo seed
.\nerv.ps1 demo health-check
.\nerv.ps1 demo stop
```

`start` requires the admin password from a controlled local environment variable and starts a fresh isolated session. It records only the session ID and non-secret operator metadata in the machine state root. `reset` stops the exact recorded session, verifies no owned resources remain, creates a fresh session and runs seed plus health-check. Repeating reset therefore recreates the same business keys on clean service databases without deleting a shared or persistent database.

`seed` does not mutate tables itself. It verifies that opt-in service startup seeds have converged and records their public identifiers and links. If a reserved identifier exists with incompatible facts, the owning service fails its seed instead of overwriting tenant data.

`health-check` is bounded. It fails non-zero and names each unavailable resource. It checks IAM, BusinessGateway, ERP, DemandPlanning, ProductEngineering, Scheduling, MES, Quality, WMS, Inventory, IndustrialTelemetry, Maintenance, PostgreSQL, Redis, and the console/business-console/screen entrypoints. It also asserts the session profile says `Messaging Provider=Redis` and verifies the frozen business keys through authenticated public Gateway facades.

`stop` and reset cleanup use only the recorded full-stack session ID. No prefix-based process/container removal, Docker prune, or broad Aspire stop is introduced.

## Seed Boundary

All demo facts use organization `org-001` and environment `env-dev`.

- MasterData provides site `SITE-001`, line `LINE-DEMO-01`, work center `WC-CNC-DEMO`, finished SKU `SKU-DEMO-001`, raw SKU `SKU-DEMO-RM-001`, customer `CUST-DEMO-001`, and device `DEV-CNC-DEMO`.
- ProductEngineering provides a published MBOM and routing for `SKU-DEMO-001`, including one operation that requires quality inspection, plus an active production version.
- Inventory provides available raw-material stock for `SKU-DEMO-RM-001`; it does not seed finished goods that could bypass production.
- ERP retains its existing released `SO-DEMO-001` prerequisite.
- MES provides released `WO-DEMO-Q01`, ready for operator execution and linked to the demo production version/routing facts. It has no production report or completion quantity.
- Quality provides an active variable-characteristic operation inspection plan with explicit limits. It has no inspection record, NCR, hold disposition, or approval result.
- IndustrialTelemetry provides a temperature tag and enabled alarm rule for `DEV-CNC-DEMO`. The rule code uses `MWO-DEMO-001` as the stable maintenance-case source prefix; no alarm event or telemetry sample is seeded.
- Maintenance retains reusable plans and receives the real alarm event during the demonstration. No maintenance work order completion is seeded.

Each service owns its own idempotent seed implementation and tests. Startup enables the demo seeds only under the explicit AppHost demo profile; ordinary production configuration remains unchanged.

## Evidence Manifest

Every successful or failed seed/health run writes a JSON evidence manifest under `artifacts/leader-demo/<UTC-run-id>/evidence.json`. It contains:

- UTC time, current commit SHA, session ID, worktree, command, and overall result;
- messaging provider and non-secret organization/environment identifiers;
- access URLs and account role names, never the password or tokens;
- per-resource Aspire state, endpoint if public, elapsed time, and failure remediation hint;
- frozen business identifiers, public result links, observed status, and whether the fact was found;
- full-stack diagnostic directory and the exact cleanup command.

Failure evidence is still written before returning non-zero. Sensitive text uses the existing script redaction helper.

## Testing and Verification

- TDD seed tests prove each reserved prerequisite is created once, repeated seeding creates no duplicates, and final business states remain absent.
- Script tests prove root dispatch, exact-session reset/stop, Redis assertion, bounded resource failure reporting, evidence schema, and secret redaction.
- AppHost build and script governance must pass.
- Targeted backend service tests cover all changed seed services.
- The final real-stack gate runs the governed demo reset/health path with PostgreSQL and Redis, verifies the frozen facts through public HTTP, repeats reset three times, and stops the final exact session with `remaining=0`.

## Documentation Impact

Product documentation is unaffected: this is operator/demo engineering and does not add a new end-user page or flow. Architecture readiness and script governance are updated because a new governed command surface and real-stack evidence contract are delivered.

## Design Self-Review

- The design keeps Aspire as the only topology source and reuses exact full-stack ownership.
- Seed facts stop before every prohibited final business outcome.
- Reset is isolated and recoverable; it cannot delete shared development or customer data.
- Redis, fixed business identifiers, public-fact verification, failure diagnostics, and evidence retention are explicit.
- No endpoint or Gateway contract change is required, so OpenAPI and generated clients remain unchanged.

