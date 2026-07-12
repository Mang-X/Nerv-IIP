# Maintenance Actual Technician Design

## Context

Maintenance work orders currently store `AssignedTechnicianUserId`, which is selected when a work order is created. Completion records actual labor minutes and costs but cannot record the worker who performed that labor. Replacing the assignment during completion would destroy the distinction between planned assignment and actual execution.

Issue #897 closes this gap for the current single-primary-technician model. Multi-technician labor transactions remain outside this issue.

## Decision

Keep `AssignedTechnicianUserId` as the planned assignment and add nullable `ActualTechnicianUserId` as the primary technician who actually completed the work.

`CompleteMaintenanceWorkOrderRequest` accepts optional `actualTechnicianUserId`. When supplied, the completed work order stores the normalized value. When it is omitted or blank, completion falls back to the existing `AssignedTechnicianUserId`. This preserves useful attribution for existing clients and work orders while retaining assignment history.

The reliability summary attributes actual labor and costs using `ActualTechnicianUserId`, falling back to `AssignedTechnicianUserId` for historical rows that predate the new column. Work-order reads expose both fields so callers can distinguish assignment from execution.

## Backend Changes

- Add `ActualTechnicianUserId` to `MaintenanceWorkOrder` and set it only as part of successful completion.
- Extend the completion domain method and command with the optional actual technician reference.
- Validate the reference using the same maximum length and normalization convention as the assigned technician reference.
- Add a nullable `actual_technician_user_id` column through a governed EF Core migration and update schema documentation and convention tests.
- Extend list/detail DTOs and the reliability query without changing endpoint routes or authorization.
- Keep the existing complete endpoint classified as `exposed` in facade coverage governance; this is a contract change, not a new endpoint.

## Gateway and Frontend Changes

- Pass `actualTechnicianUserId` through the BusinessGateway completion facade and expose both technician fields in relevant responses.
- Export the governed BusinessGateway OpenAPI snapshot and regenerate the frontend API client; generated files are never edited manually.
- Add an actual-technician selector to the Business Console completion sheet using the existing worker lookup and NvUI stable component boundary.
- Default the selector to the work order's assigned technician while allowing the operator to select a different worker.
- Send the selected worker as `actualTechnicianUserId`; do not mutate or relabel the planned assignment in the UI.

## Data and Compatibility

The new database column is nullable, so deployment does not require backfilling existing rows. Reliability aggregation uses `ActualTechnicianUserId ?? AssignedTechnicianUserId`, making historical reports stable. Existing API clients can omit the new optional request field. Existing response consumers tolerate the additive nullable response property.

No cross-service foreign key is introduced. Technician identifiers remain external user references owned outside Maintenance.

## Validation

- Domain tests prove that completion records a different actual technician without changing assignment, and falls back to assignment when omitted.
- Command and endpoint tests prove validation, normalization, and request mapping.
- Query tests prove reliability grouping prefers actual technician and falls back for historical records.
- Schema tests prove the nullable column name, length, and comment.
- Gateway contract tests prove request and response propagation.
- Business Console tests prove selector defaulting and completion payload mapping.
- Run targeted Maintenance and BusinessGateway tests, facade coverage tests, frontend typecheck/tests/build, OpenAPI export/codegen checks, and migration/schema gates required by `AGENTS.md`.

## Deferred Capability

Multiple technicians, per-worker time entries, labor rates, crews, and partial confirmations require a future `MaintenanceLaborEntry` model. They are intentionally not represented by additional scalar technician fields in this issue.
