# MAN-632 Searchable Directories Design

## Scope

Deliver one BusinessGateway searchable-directory contract for every directory type named by MAN-632. Each type declares exactly one authoritative source and one availability status. The contract supplies stable IDs and readable display text, enforces organization/environment and optional site/workshop/work-center/team scope, pushes keyword and paging to the owning service, and never writes business values.

## Authority map

| Directory type                                                        | Authority                                            | Delivery                                                                                                                               |
| --------------------------------------------------------------------- | ---------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `personnel`                                                           | MasterData worker directory                          | available                                                                                                                              |
| `team`, `equipment`, `work-center`, `station`, `workshop`, `material` | MasterData resources                                 | available                                                                                                                              |
| `location`, `batch`, `serial`                                         | Inventory stock locations/ledgers                    | available through a new internal-auth Inventory read endpoint                                                                          |
| `defect-code`, `scrap-reason`                                         | Quality reason catalog                               | available, with the requested group filter applied by Quality                                                                          |
| `downtime-reason`, `maintenance-reason`                               | Maintenance downtime reason catalog                  | available after adding keyword filtering to the existing service read endpoint                                                         |
| `priority`                                                            | MasterData reserved FactoryCustom `priority` CodeSet | configurable values only; no seed or hardcoded enum, and an empty authority returns `unavailable` / `directory-authority-unconfigured` |

No business-domain directory is copied into MasterData. The Gateway maps each authority's real response into a common item containing `id`, `code`, `display`, `source`, optional parent/scope metadata, and snapshot/freshness facts. Because `StationCode` is only a local position inside a production line, its stable `id` is an opaque length-prefixed encoding of organization, environment, site, workshop, line, work-center, and station components while the readable `code` remains the station code. Inventory batch/serial IDs likewise use UTF-8 byte-length-prefixed SKU plus lot/serial components, remain stable across site filtering, and keep readable code/context separate; clients never parse either opaque ID to recover display or scope fields.

## Scope and authorization

`organizationId` and `environmentId` are mandatory and must match the bearer claims. Each directory type selects its own read permission. IAM returns permission-aware scope grants for that request. An explicit supported scope must match an exact grant or be covered by an organization-wide grant for the same organization; when scope is omitted, an organization-wide grant may query the organization, while exactly one compatible restricted grant is derived and forwarded as the effective owner filter. Multiple compatible grants, incompatible grants, malformed grants, blank tenant scope, an incomplete scope pair, or an unsupported scope dimension fail closed rather than silently widening to the organization.

Inventory location/batch/serial queries filter organization and environment first, then optional site/material scope, keyword, distinct identity, deterministic ordering, and paging. A location material filter requires a matching positive-stock ledger before total and paging. Empty scope identifiers and page offsets beyond Int32 fail validation before authorization; owner queries receive the already checked offset. MasterData worker scope uses its existing fail-closed work-center-to-workshop resolution.

## Ranking

`default` is the only always-available ranking mode. `recent` and `suggested` are metadata requests, not alternate business values. They may reorder only when the authority returns real usage/default facts. This slice has no trustworthy cross-domain usage/default fact, so those modes return `ranking.status=unavailable` with a stable reason code while retaining ordinary search results. Alphabetical/code order is never described as recent or suggested.

The endpoint never returns or changes quantities, measurements, dispositions, scrap/rework decisions, or root causes.

## Contract and failure behavior

The Gateway accepts the repositories' real raw DTO and `ResponseData.data` wire forms, while treating only the expected typed payload as authority data. Missing data, primitive/array data, `success:false`, malformed fields, timeout, and transport failure fail closed through the existing proxy error mapping. The configurable priority type returns HTTP 200 with `status=unavailable`, no items, and `reasonCode=directory-authority-unconfigured` until the tenant has configured at least one active priority value.

All changed service endpoints are registered in `facade-coverage-matrix.json`. The BusinessGateway contract is exported through the governed OpenAPI script, regenerated through Hey API, and re-exported only through the stable package barrel.

## Verification

Use TDD for Inventory, Maintenance, and Gateway behavior. A real Docker PostgreSQL profile proves Inventory distinct paging, keyword and scope filters, and natural planner index use without environment-variable skips or `enable_seqscan` overrides. EXPLAIN consumes the same parameterized EF count/page query builders as production after representative seeding and `ANALYZE`. Run affected service/Gateway suites, facade coverage, contract boundary and migration/schema checks, OpenAPI drift, api-client/frontend typecheck/test/build, per-file formatting, and verify zero task-owned containers/volumes remain.
