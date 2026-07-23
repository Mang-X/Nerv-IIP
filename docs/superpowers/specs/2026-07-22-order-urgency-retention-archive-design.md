# Order Urgency Snapshot Retention And Archive Design

## Scope and policy

This design governs only BusinessScheduling `order_urgency_snapshots`. It does not change `order-urgency-v1`, the 15-minute recalculation cadence, business-priority audit rows, or any rescheduling behavior.

The approved policy baseline is:

- online retention: 180 days;
- total retention: 3 years;
- archive: immutable, version-addressable object storage behind the platform FileStorage boundary;
- legal hold: supported per organization and environment, with no active hold by default;
- restore objective: an operator can rehydrate a selected archive batch within 24 hours;
- deletion: source and archive deletion each require explicit, time-bounded authorization and produce durable audit evidence.

All durations, batch sizes, schedules, scopes, holds, and deletion authorizations are configuration driven. No scope is processed unless it is explicitly enabled. Invalid or incomplete configuration, an unavailable archive, a non-versioned bucket, a failed read-back, or incomplete object evidence stops the run without deleting source snapshots.

## Boundaries

BusinessScheduling owns candidate selection, retention policy, leases, archive-batch audit rows, deletion decisions, metrics, and restore semantics. It accesses only the Scheduling schema.

FileStorage owns MinIO/S3-compatible credentials and exposes a narrow internal-service API for versioned archive put/get/delete. The put operation requires bucket versioning, stores caller-supplied immutable content, and reads the exact returned object version back to verify SHA-256 and byte length. BusinessScheduling never sees credentials and never talks directly to MinIO.

The archive key is namespaced by organization, environment, archive kind, and stable batch id. The stored document is a versioned JSON envelope containing policy/version metadata plus complete snapshot rows, including model version, input fingerprint, contribution/result JSON, reason codes, calculated time, calculation bucket, and business-priority revision.

## Retention workflow

Each configured scope is processed independently:

1. Validate the complete policy and acquire a database-backed expiring lease for the organization/environment pair.
2. If a legal hold is active, record/emit the held outcome and do not archive or delete.
3. Resume any archive batch whose object evidence is complete but whose authorized source deletion has not yet committed.
4. Select at most the configured batch size below the stable 180-day watermark. A snapshot is eligible only when a strictly newer snapshot for the same order exists, so every order retains a directly queryable latest snapshot.
5. Serialize a deterministic archive envelope and calculate SHA-256 and byte length.
6. Put through FileStorage. Accept evidence only when object key, non-empty version id, checksum, size, and exact-version read-back all agree.
7. Persist the archive-batch evidence. Source deletion happens in a later atomic database transaction and only while a valid, explicit source-deletion authorization remains in force.
8. After the 3-year boundary, exact object-version deletion may run only with a separate archive-deletion authorization and no legal hold. The audit row remains permanently.

Stable batch identity and unique indexes make retries idempotent. The database lease prevents overlapping work for the same scope across service instances. A crash after object write but before audit persistence can create an unused object version but cannot delete source data. A crash after audit persistence resumes from recorded evidence. Source-row deletion and audit state transition share one transaction.

## Restore and audit retrieval

An internal authenticated restore endpoint accepts organization, environment, and archive batch id. It fetches the exact object version, verifies checksum and size again, validates the envelope scope and schema version, and inserts only missing snapshots using the original idempotency key. Existing rows are not overwritten. A restore audit record captures actor, reason, object version, counts, and timestamps.

The operator runbook describes discovery, dry validation, restore, verification, and rollback. The 24-hour objective is an operational target, not an automatic guarantee; the endpoint and stored evidence remove the need for ad-hoc SQL or direct object-store access.

## Observability and capacity

Prometheus counters cover selected, archived, source-deleted, archive-deleted, restored, held, failed, and configuration-rejected rows/batches. Gauges expose online eligible rows and oldest online snapshot age per scope without putting organization/environment values into unbounded metric labels. Logs and durable batch rows carry scope and authorization references. Repeated failures and stale eligible backlog emit error-level alerts suitable for the existing log/OTel pipeline.

A representative PostgreSQL verification seeds multiple scopes and orders, proves latest-row protection, measures bounded archive selection/source deletion, runs concurrent workers, retries partial failures, and records query/maintenance timing and row counts. It reports evidence rather than declaring a universal SLO.

## Schema and migration

Scheduling adds service-owned archive batch, scope lease, and restore audit tables plus indexes for scope/status/watermark scans. No cross-schema foreign keys or cross-service database access are introduced. The migration is forward-only; rollback disables the worker first, preserves object archives and audit rows, and does not attempt to recreate already archived source rows automatically.

## Alternatives rejected

Direct MinIO access from BusinessScheduling was rejected because it would leak platform storage credentials and violate the FileStorage boundary. Database-only archives were rejected because they do not satisfy versioned object-storage retention. A single global worker switch was rejected because it cannot prove organization/environment isolation or explicit per-scope opt-in.
