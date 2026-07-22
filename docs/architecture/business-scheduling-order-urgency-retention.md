# Order Urgency Snapshot Retention and Recovery

## Scope and policy

BusinessScheduling owns the lifecycle policy and audit state for `order_urgency_snapshots`; FileStorage owns the MinIO transport. The production baseline is 180 days online and 1,095 days total retention. Every policy row is isolated by `organizationId + environmentId`; no wildcard scope is supported. The newest snapshot for an order is always kept online even if it is older than the online cutoff.

The worker is disabled by default. A scope must be explicitly enabled and valid before it is considered. An active legal hold blocks source and archive deletion for that scope. Source-row deletion and exact archive-version deletion use separate, time-bounded authorization records. Missing or expired authorization still permits a verified archive write, but never deletion.

```json
{
  "OrderUrgencyRetention": {
    "Enabled": true,
    "IntervalMinutes": 60,
    "Scopes": {
      "org-001-prod": {
        "Enabled": true,
        "OrganizationId": "org-001",
        "EnvironmentId": "prod",
        "OnlineRetentionDays": 180,
        "TotalRetentionDays": 1095,
        "BatchSize": 100,
        "LegalHoldActive": false,
        "SourceDeletionAuthorization": {
          "Reference": "CAB-1234",
          "Actor": "user:records-manager",
          "Reason": "Approved online-retention enforcement",
          "ApprovedAtUtc": "2026-07-22T00:00:00Z",
          "ExpiresAtUtc": "2026-07-23T00:00:00Z"
        }
      }
    }
  }
}
```

Do not keep a standing deletion authorization. Inject a short-lived approval for a controlled run, verify its audit rows, and remove it. Add `ArchiveDeletionAuthorization` separately only when an archive version has passed total retention and the exact-version deletion has been approved.

## Archive safety boundary

FileStorage requires `Storage:MinIO:Endpoint`, `AccessKey`, `SecretKey`, and `ComplianceArchiveBucket`. The compliance bucket is pre-provisioned with versioning enabled; object-lock capability is also required when object-storage legal hold is used. The service does not create or downgrade this bucket.

For each deterministic batch, FileStorage writes a scope-namespaced JSON envelope, obtains a non-empty version ID, reads that exact version back, and verifies SHA-256 and byte length. Scheduling persists object key, exact version, hash, size, and verification time before it may delete source rows. Disabled policy, invalid scope configuration, unavailable FileStorage/MinIO, disabled bucket versioning, incomplete evidence, hash mismatch, active legal hold, lease contention, or missing authorization all fail safe: source snapshots remain online.

The scope lease and stable batch identity make overlapping worker instances idempotent. Failed or archived-but-not-deleted batches are durable and can be retried without losing their evidence. The FileStorage archive endpoints and Scheduling restore endpoint use internal-service authorization and are not Business Console surfaces.

## Recovery objective

The operational recovery objective is 24 hours from an approved restore request. Restore reads the recorded exact object version, verifies the stored evidence and envelope scope, rehydrates only missing immutable snapshots, and appends an audit row for every attempt, including an idempotent replay.

Use `POST /api/business/internal/v1/scheduling/order-urgency-archives/restore` with organization, environment, batch ID, and reason. The actor is resolved from the authenticated principal or canonical internal `X-Actor` forwarding header and cannot be supplied in the request body. Before declaring recovery complete, verify the corresponding `order_urgency_restore_audits` row, restored count, exact object version, and application read path. Never copy objects or insert rows manually as a substitute for this path.

## Metrics and alerts

- `nerv_iip_order_urgency_retention_runs_total{outcome}`: succeeded, failed, crashed, held, lease-skipped, or configuration-rejected runs.
- `nerv_iip_order_urgency_retention_snapshots_total{outcome}`: archived, source-deleted, and archive-deleted counts.
- `nerv_iip_order_urgency_retention_eligible_snapshots`: last observed eligible backlog.
- `nerv_iip_order_urgency_retention_oldest_eligible_age_seconds`: last observed oldest eligible age.

Error logs are operational alerts for rejected configuration, archive/evidence failure, and worker crashes. A warning is emitted when eligible rows exceed one configured batch. Alert on any increase in failed/configuration-rejected outcomes, repeated crashes, or a backlog/oldest-age value that does not decline across successful intervals. These metrics intentionally omit organization/environment labels to avoid tenant-cardinality leakage; scope remains in structured logs and database audit rows.

## Migration and rollout

Migration `20260722150201_AddOrderUrgencyRetentionArchive` adds the archive-batch audit, retention-lease, and restore-audit tables plus a scope/time retention-scan index. Apply the migration while retention remains disabled. Pre-provision and verify the compliance bucket, deploy FileStorage and Scheduling, exercise an archive-only scope without deletion authorization, and inspect exact-version evidence. Then enable a short-lived source-deletion authorization for one small batch. Increase batch size only after database latency, eligible backlog, and object-store latency are understood.

Rollback first disables all scopes and removes deletion authorizations. The code can then be rolled back while leaving the additive tables and evidence intact. Do not run the migration `Down` path after archives or audits exist unless records governance has explicitly authorized destroying that evidence.

Run the representative PostgreSQL capacity/concurrency profile with:

```powershell
pwsh scripts/verify-business-scheduling-urgency-retention.ps1
```

It migrates a disposable database, seeds 10,002 snapshots across 5,001 orders, overlaps two workers in the same scope, verifies a single leased 1,000-row archive/delete batch, and proves all 5,001 latest snapshots remain. JSON evidence is written under `artifacts/script-logs/business-scheduling-urgency-retention/<run-id>/` and is not committed.
