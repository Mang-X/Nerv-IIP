# ADR 0020: Industrial Telemetry Historian Storage

- Status: Accepted
- Date: 2026-07-07

## Context

IndustrialTelemetry previously stored `TelemetrySummary` buckets for current history and alarm evaluation, but MAN-373 / GitHub #689 requires a historian foundation with raw sampling detail, hourly and daily downsampling, retention cleanup, and shared consumption of `TelemetryTag.SamplingPolicy`.

The deployment baseline is still private, single-machine Windows/Aspire installs before larger clustered deployments. The historian must therefore avoid introducing a mandatory TimescaleDB extension or a second time-series database into the first production path unless the current PostgreSQL route cannot meet the target.

## Decision

Use the existing IndustrialTelemetry PostgreSQL schema with native tables and provider-neutral indexes:

1. `telemetry_raw_samples` stores the finest bucket detail accepted by the service: min, max, weighted-average input, first, last, sample count, bucket start/end, and source idempotency metadata.
2. `telemetry_rollups` stores `Hourly` and `Daily` rollups with min, max, weighted average, first and last. The rollup key is organization, environment, device, tag, grain and window start.
3. `telemetry_summaries` remains as a compatibility write path for alarm rule evaluation and existing clients, but `/equipment/telemetry/history` reads the historian tables.
4. `TelemetryTag.SamplingPolicy` is parsed by both Connector Host and IndustrialTelemetry storage. The collector can derive bucket seconds from `sample-10s`, `sample-1m`, or `bucket=30s;raw=7d;hourly=90d;daily=730d`; storage rejects writes whose bucket width does not match the configured tag policy.
5. Retention cleanup is layer-specific: raw, hourly and daily windows are deleted independently after their configured duration.

TimescaleDB remains an optional future optimization, not the baseline dependency for this slice. If later benchmark evidence shows native PostgreSQL is insufficient for a target customer profile, the migration path should keep the same domain/application contract and move only the infrastructure storage strategy.

## Alternatives Considered

1. **Mandatory TimescaleDB hypertables**: rejected for the current baseline because it adds extension installation, backup/restore, and offline deployment requirements to single-machine private installs.
2. **Keep only `telemetry_summaries`**: rejected because summaries do not preserve first/last values or retention-layer separation and cannot serve raw-detail history.
3. **External historian service now**: rejected because cross-service history ownership and connector delivery contracts are not yet stable enough to justify a new physical dependency.

## Consequences

Historical reads can choose raw, hourly or daily rows from the same service-owned schema without cross-schema foreign keys. The first implementation uses regular tables and indexes; PostgreSQL partitioning can be added later within the same schema if retention deletes become too expensive.

Downsampling must be idempotent. Re-running a rollup window must not create a second hourly or daily row, and a deterministic historian source sequence is recorded for operator diagnostics.

Retention jobs must run after downsampling has completed for the affected windows. Operators should use a raw retention window long enough to cover late connector delivery.

## Performance Note

The reproducible local check for this slice is:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --filter IndustrialTelemetryHistorianTests -v:minimal
```

That test covers raw writes, policy enforcement, hourly/daily weighted aggregation and retention cleanup. For higher-volume customer sizing, run the same command against a PostgreSQL-backed benchmark fixture before enabling shorter raw retention; the first production schema already has the idempotency and range indexes that benchmark should exercise.
