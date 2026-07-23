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
        "MaxArchiveBytes": 5242879,
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

For each deterministic batch, Scheduling first persists a `pending` batch intent and its ordered source membership in one database transaction. The indexed `organization + environment + snapshot` membership prevents a source generation from entering another batch without scanning audit JSON; `batch + sequence` reconstructs the original payload order. Pending and retryable failed intents always run before new selection and reconstruct the payload from that membership and the recorded creation time, so later `BatchSize` or `MaxArchiveBytes` changes cannot rewrite an in-flight batch. FileStorage writes a scope-namespaced JSON envelope with the S3 `If-None-Match: *` precondition, obtains a non-empty version ID, reads that exact version back, and verifies SHA-256 and byte length. MinIO 7 applies that precondition atomically on its single `PutObject` path but switches to multipart at 5 MiB, so the shared contract caps content at 5 MiB minus one byte. Scheduling dynamically shortens a configured row batch to remain within `MaxArchiveBytes`; FileStorage independently rejects any larger request before object storage is called. An individual snapshot larger than the configured cap records `archive-payload-too-large`, remains online, is durably excluded from repeated attempts, and does not prevent later eligible rows from advancing. The conditional write atomically permits only the first writer for a batch key, including when a lease expires during remote I/O. A retry reuses the current object version only when its stored SHA-256 and byte length match the deterministic batch payload, preventing a read-back or database-persistence retry from creating another unmanaged version. Scheduling persists object key, exact version, hash, size, and verification time before it may delete source rows, and revalidates that the live evidence returns the same object key and version ID. Immediately before the fenced source-delete transaction, every member must still be outside the current online-retention window and must still have a newer snapshot for the same order; any policy extension or missing newer generation preserves the complete source batch. Disabled policy, invalid scope configuration, unavailable FileStorage/MinIO, disabled bucket versioning, incomplete evidence, hash mismatch, active legal hold, lease contention, or missing authorization all fail safe: source snapshots remain online.

The scope lease and source-row-generation-derived stable batch identity make overlapping worker instances idempotent. Candidate ordering ends with the immutable snapshot ID so a `BatchSize` boundary is deterministic. A completed `archived` batch reuses its recorded exact-version evidence instead of uploading another object version; restored rows receive new IDs and therefore form a new batch without regressing the original terminal batch. Batch lifecycle transitions use an optimistic concurrency revision. Recovery work is limited to ten batches per run, and the worker checks or renews its ten-minute lease around remote I/O and before destructive transitions. Source deletion, the batch transition, and a lease-revision fence commit in one database transaction; a concurrent takeover changes that revision and rolls the entire delete transaction back. A lost lease aborts the run with source rows preserved. Failed or archived-but-not-deleted batches are durable and can be retried without losing their evidence. The FileStorage archive endpoints and Scheduling restore endpoint use internal-service authorization and are not Business Console surfaces.

## Recovery objective

The operational recovery objective is 24 hours from an approved restore request. Restore reads the recorded exact object version, verifies the stored evidence and envelope scope, rehydrates only missing immutable snapshots, and appends an audit row for every attempt, including an idempotent replay.

Use `POST /api/business/internal/v1/scheduling/order-urgency-archives/restore` with organization, environment, batch ID, and reason. The actor is resolved from the authenticated principal or canonical internal `X-Actor` forwarding header and cannot be supplied in the request body. Before declaring recovery complete, verify the corresponding `order_urgency_restore_audits` row, restored count, exact object version, and application read path. Never copy objects or insert rows manually as a substitute for this path.

## Metrics and alerts

- `nerv_iip_order_urgency_retention_runs_total{outcome,organization,environment}`: succeeded, failed, crashed, held, lease-skipped, or configuration-rejected runs.
- `nerv_iip_order_urgency_retention_snapshots_total{outcome,organization,environment}`: archived, source-deleted, and archive-deleted counts.
- `nerv_iip_order_urgency_retention_eligible_snapshots{organization,environment}`: last observed eligible backlog.
- `nerv_iip_order_urgency_retention_oldest_eligible_age_seconds{organization,environment}`: last observed oldest eligible age.
- `nerv_iip_order_urgency_retention_operation_failures_total{error_code,organization,environment}`: stable classifications for persistence, fencing, and other scope-run crashes.

Error logs are operational alerts for rejected configuration, archive/evidence failure, and worker crashes. A warning is emitted when eligible rows exceed one configured batch. Alert on any increase in failed/configuration-rejected outcomes, repeated crashes, or a backlog/oldest-age value that does not decline across successful intervals. Metrics carry `organization` and `environment` labels so concurrent configured scopes retain independent gauges and counters. These values come only from the bounded operator configuration; never populate them from arbitrary request data, and apply normal metrics-access controls because scope names may identify tenants.

## Migration and rollout

Migration `20260722150201_AddOrderUrgencyRetentionArchive` adds the archive-batch audit, retention-lease, and restore-audit tables plus a scope/time retention-scan index. Migration `20260722154723_HardenOrderUrgencyArchiveLifecycle` adds the archive-batch lifecycle concurrency revision. Migration `20260722164839_AddOrderUrgencyArchiveMembership` adds ordered, scope-isolated, indexed source membership. Apply all three migrations while retention remains disabled. Pre-provision and verify the compliance bucket, deploy FileStorage and Scheduling, exercise an archive-only scope without deletion authorization, and inspect exact-version evidence. Then enable a short-lived source-deletion authorization for one small batch. Increase batch size only after database latency, eligible backlog, and object-store latency are understood.

Rollback first disables all scopes and removes deletion authorizations. The code can then be rolled back while leaving the additive tables and evidence intact. Do not run the migration `Down` path after archives or audits exist unless records governance has explicitly authorized destroying that evidence.

Run the representative PostgreSQL capacity/concurrency profile with:

```powershell
pwsh scripts/verify-business-scheduling-urgency-retention.ps1
```

It migrates a disposable database, seeds 10,002 snapshots across 5,001 orders, overlaps two workers in the same scope, verifies a single leased 1,000-row archive/delete batch, and proves all 5,001 latest snapshots remain. JSON evidence is written under `artifacts/script-logs/business-scheduling-urgency-retention/<run-id>/` and is not committed.
