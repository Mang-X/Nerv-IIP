# MAN-602 Equipment Health Score Demo Slice Design

## Scope

Close GitHub #1087 / Linear MAN-602 only. Deliver a deterministic, explainable equipment-health read model inside IndustrialTelemetry, expose it through BusinessGateway, and render it on the existing Business Console device-detail page. The implementation may consume the public facts produced by #1086, but it must not implement that simulator, scheduling-risk propagation, a standalone prediction page, statistical/AI models, or any other #1055 post-demo scope.

## Architecture

- IndustrialTelemetry owns the calculation because every input used by this slice already lives in its schema: enabled alarm rules provide specification thresholds, raw telemetry provides current and historical values, device-state snapshots provide runtime hours, and alarm events provide active/recent alarm facts.
- Health is calculated on each GET. There is no health-score table, migration, background scheduler, cross-schema query, or fabricated seed result.
- A pure `EquipmentHealthScoringPolicy` evaluates normalized observations and returns a score, level, triggered risk factors, all five rule evaluations, calculation time, and data freshness. The HTTP query handler only loads scoped facts and builds observations.
- The service endpoint is `GET /api/business/v1/iiot/devices/{deviceAssetId}/health`; the BusinessGateway facade is `GET /api/business-console/v1/equipment/devices/{deviceAssetId}/health`. Both require telemetry-read permission.
- Business Console consumes the generated client through a composable. The health query uses Pinia Colada auto-refetch every five seconds while organization, environment, and device scope are present.

## Rule Semantics

All time windows are anchored to injected `TimeProvider.GetUtcNow()` so tests and callers receive deterministic evidence.

| Rule | Inputs and threshold | Risk penalty | Insufficient data |
|---|---|---:|---|
| Threshold proximity | For each enabled alarm rule, use the newest raw `LastValue`. A high/low threshold is near or breached when the signed safe-side distance is at most 20% of `max(abs(threshold), 1)`. Select the closest/highest-severity result. | 15 | `no-current-sample` when no enabled rule has a current sample. |
| Runtime hours | Reuse the existing runtime-hours calculation for the trailing 24 hours. Trigger at 20 productive runtime hours. | 10 | `no-runtime-samples` when the window has no device-state facts. |
| Alarm frequency | Count alarms raised in the trailing 24 hours. Trigger for any active alarm or at least three raised alarms. Active warning/critical alarms use 45/65 penalty; other/repeated-only alarms use 20. A single cleared alarm does not keep the recovered demo state degraded. | 20/45/65 | Never fabricated; an empty alarm set is a normal zero-frequency observation. |
| Sustained exceedance | Per enabled rule, evaluate raw samples from the trailing 24 hours. At least six samples spanning at least 30 minutes are required. Trigger when at least 80% breach the rule threshold. | 20 | `history-accumulating` until the sample-count/span requirement is met. |
| Trend growth | Use the same sufficiently populated trailing-24-hour history. Compare the first and last thirds in the alarm-risk direction. Trigger when deterioration is at least 20% of `max(abs(first average), 1)`. | 15 | `history-accumulating` until the sample-count/span requirement is met. |

The score is `clamp(100 - sum(triggered penalties), 0, 100)`. Levels are `Healthy` for 90-100, `Watch` for 70-89, `Warning` for 40-69, and `Critical` for 0-39. This produces the intended demo progression: near-threshold telemetry moves the device to Watch; an active warning/critical alarm moves it to Warning/Critical; after alarm clear and recovered telemetry, the active-alarm and proximity penalties disappear.

Each rule evaluation carries a stable rule code, Chinese business label, status (`normal`, `risk`, or `accumulating`), current value/text, threshold value/text, unit, evidence summary, source-fact type, source-fact label, and source occurrence time. `riskFactors` contains only triggered evaluations. The UI renders all five evaluations so the two historical rules can explicitly show `历史数据积累中`.

## Freshness and Failure Semantics

- Freshness uses the newest consumed raw-sample, device-state, or alarm lifecycle timestamp. It is `fresh` through two minutes, `delayed` through ten minutes, `stale` afterward, and `unavailable` when no source fact exists.
- Missing current telemetry or runtime state does not produce a penalty. It produces an explicit unavailable/accumulating rule evaluation.
- Historical insufficiency never throws and never synthesizes a trend. Both history rules remain visible as `历史数据积累中`.
- Organization, environment, and device filters are applied to every service query. The Gateway preserves the authenticated business scope and resource ID.
- No response exposes a raw database GUID. Source labels use device IDs, tag keys, alarm codes, rule codes, values, and timestamps.

## Frontend

The existing device-detail page receives a focused `EquipmentHealthCard` rather than adding more logic to the already large page. The card shows score, Chinese level, freshness, calculation time, triggered-risk count, and one row per rule with its current value, threshold, rule name, status, and evidence. Risk, normal, and accumulating states use existing NvUI/semantic tokens.

The page keeps prior health data mounted while polling and distinguishes loading, permission/network failure, no-scope, and successful empty-risk states. Manual page refresh also refetches health. The product guide for equipment engineers documents the health card, five-rule interpretation, polling behavior, and historical-accumulation state.

## Facade and Contract Governance

- Register the service endpoint in `IndustrialTelemetryEndpointContracts`.
- Declare the endpoint `exposed` in `docs/architecture/facade-coverage-matrix.json` with Gateway operation `getBusinessConsoleEquipmentDeviceHealth`.
- Export BusinessGateway OpenAPI, regenerate `@nerv-iip/api-client`, and add stable barrel exports. Generated files and OpenAPI snapshots are never hand-edited.
- Add service endpoint/query tests, pure five-rule boundary tests, Gateway authorization/proxy/OpenAPI tests, facade-coverage verification, composable/page/component tests, and touched-file formatter checks.

## #1086 Dependency

Deterministic tests and all build/contract gates are independently executable in this branch. The real `normal/degrading/alarm/recovered` acceptance requires #1086 to be merged and running through its public ingestion path. This PR must report that dependency honestly and must not add a private data injector or seed workaround.
