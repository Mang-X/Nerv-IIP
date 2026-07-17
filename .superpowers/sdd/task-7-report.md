# Task 7 Implementation Report

## Status

DONE — IndustrialTelemetry now accepts authoritative Connector Host tag manifests and exposes an internal, summary-only tag coverage read face. The manifest callback remains internal; coverage is deliberately `deferred` to #947 / PR #952 Task 9, which must flip the governance row to `exposed` when the BusinessGateway facade ships.

No Connector Host, Gateway, frontend, generated client, migration, or `skills-lock.json` change is included.

## RED evidence

- The first focused test run failed to compile because the report command, entry contract, handler, revision calculator, and coverage query did not exist (`CS0246`).
- After the application layer turned green, the facade gate failed on exactly the two unregistered live endpoints: POST manifest and GET coverage.
- The Npgsql provider-translation test was added before extracting the reusable coverage projection and failed to compile because `ConnectorTagCoverageQueryProjection` did not exist (`CS0103`).

## GREEN implementation

- Added internal-service-authorized POST `/api/business/v1/iiot/connector-tag-manifests` (`reportBusinessIiotConnectorTagManifest`) with endpoint/command validators, deterministic server-side revision verification, and aggregate apply semantics. The command relies on the governed unit-of-work behavior and never calls `SaveChanges` explicitly.
- Canonical revision JSON contains normalized `sourceSystem` and entries sorted ordinally by normalized `(deviceAssetId, tagKey)`, with `enabled` and sanitized `protocolAddress`. Activation status/time/error are intentionally excluded. The fixed SHA-256 vector is `e0ff8c1111083580a719587480101437f3fcd5bf76bb822fc3ae5f2698631e44`.
- Added configurable future-observation protection at `IndustrialTelemetry:ConnectorTagManifest:MaxFutureObservationSkew`. The default is 5 minutes, enough for bounded field-host/NTP drift while still rejecting future authoritative facts; startup validation caps configuration at 15 minutes so the guard cannot be effectively disabled. Manifest and activation observation times are both protected.
- Same-revision retries revalidate the normalized revision shape, then accept only independently newer activation facts. Stale and conflict results remain accepted domain facts rather than transport failures.
- Added internal GET `/api/business/v1/iiot/connectors/{collectionConnectorId}/tag-coverage` (`getBusinessIiotConnectorTagCoverage`). Missing roots return `unavailable`; existing empty manifests return `current` with zero counts.
- Coverage projects current manifest bindings and aggregates only `telemetry_summaries` over the full organization/environment/connector/device/tag key, using `Min(BucketStart)` and `Max(BucketEnd)`. It does not query raw samples or control data. An Npgsql `ToQueryString()` test proves provider translation and the full-key join.
- Registered both endpoints in the IndustrialTelemetry endpoint contract registry and updated facade governance totals. Manifest is `internal` with the Connector Host callback rationale; coverage is `deferred` with the mandatory Task 9 follow-up.

## Verification

- Focused manifest/coverage/contracts: 85 passed / 0 failed / 0 skipped.
- IndustrialTelemetry Domain full: 37 passed / 0 failed / 0 skipped.
- IndustrialTelemetry Web full: 162 passed / 0 failed / 5 existing PostgreSQL-environment conditional skips.
- Facade coverage gate: 9 passed / 0 failed / 0 skipped.
- IndustrialTelemetry Web build: succeeded with 0 warnings / 0 errors.
- `git diff --check`: passed.

## Scope and follow-up

The coverage service endpoint is intentionally not exposed through BusinessGateway in Task 7. #947 / PR #952 Task 9 owns the facade, OpenAPI/codegen/barrel delivery and must change this endpoint's matrix classification from `deferred` to `exposed`. The pre-existing `skills-lock.json` worktree modification is preserved and excluded from this commit.
