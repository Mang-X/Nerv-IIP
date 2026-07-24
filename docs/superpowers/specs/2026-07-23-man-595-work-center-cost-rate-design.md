# MAN-595 Work-Center Cost-Rate Governance Design

## Scope

Close GitHub #1070 / Linear MAN-595 only. Business ERP owns work-center labor cost rates. The change must make a newly created MES work center configurable through public BusinessGateway HTTP, preserve missing-rate fail-closed behavior, and prove the existing Redis/PostgreSQL production-cost chain without a seeded rate or explicit finished-goods unit cost.

## Decisions

- A `WorkCenterCostRate` row is an immutable, append-only revision scoped by `organizationId + environmentId + workCenterId`.
- Every revision records a positive hourly rate, ISO 4217-style three-letter `currencyCode`, inclusive `effectiveFromUtc`, optional exclusive `effectiveToUtc`, authenticated `changedBy`, non-empty `reason`, `changedAtUtc`, and monotonically increasing `revision`.
- Overlapping revisions are allowed only as an audit/correction mechanism. For a production report occurrence time, ERP deterministically selects the matching revision with the highest revision number. This preserves replay of historic reports while making corrections explicit and auditable.
- The existing table is migrated in place. Existing rows become revision 1, use `CNY`, start at the Unix epoch, have no expiry, and record `system:migration` with a migration reason. The previous tenant/work-center unique index is replaced with tenant/work-center/revision uniqueness plus an effective lookup index.
- The labor-cost consumer selects a rate at `ProductionReportRecordedPayload.ReportedAtUtc`. No match still writes `missing-work-center-cost-rate` and does not record the consumer inbox, so configuring a rate and replaying the same event remains recoverable.
- ERP exposes `POST /api/business/v1/erp/finance/work-center-cost-rates` for an audited revision and `GET /api/business/v1/erp/finance/work-center-cost-rates` for scoped history/current inspection. Both use existing ERP finance permissions and internal-service authorization.
- BusinessGateway exposes the same write/read capability under `/api/business-console/v1/erp/finance/work-center-cost-rates`, performs IAM finance permission checks, and forwards the authenticated actor for the write. The service never trusts an actor supplied in the JSON body.
- Both service endpoints are declared `exposed` in the facade coverage matrix, included in BusinessGateway OpenAPI, regenerated into `@nerv-iip/api-client`, and re-exported through the stable business-console barrel.

## Validation and failure semantics

- Blank scope/work-center/reason/actor, non-positive rate, non-three-letter currency, non-UTC timestamps, or `effectiveToUtc <= effectiveFromUtc` are rejected.
- The command calculates the next revision inside ERP from the scoped history. A database unique constraint prevents duplicate revisions under concurrent writers; callers retry explicitly rather than silently overwriting history.
- Reads never cross organization or environment. A work-center filter is required so the public audit query cannot become a tenant-wide unbounded scan.
- Missing and expired rates remain fail-closed. The diagnostic includes the work center and report occurrence time but no secrets.

## Real-chain proof

The existing `leader-demo-main-chain` scenario configures a run-scoped CNY rate through the new BusinessGateway facade after creating the work center and before reporting production. It then omits the prior explicit FGR `unitCost`, polls the public inventory link, and requires evidence for rate configuration, ERP labor accumulation/capitalization, MES-applied unit cost, and Inventory posting. The managed session remains PostgreSQL + Redis and must clean up all owned resources.

## Non-goals

- No ERP finance UI page, currency conversion engine, base-currency master-data service, automatic dead-letter replay, or cross-service schema reference.
- No weakening of MES, ERP, or Inventory missing-cost gates.
