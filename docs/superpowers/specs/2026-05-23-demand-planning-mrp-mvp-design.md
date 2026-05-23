# DemandPlanning MPS/MRP MVP Design

## Goal

Build DemandPlanning as the planning fact source for demand sources, master production schedules, deterministic daily-bucket MRP runs, planned purchase suggestions, planned work order suggestions and pegging.

DemandPlanning explains why ERP should procure material and why MES should create work. It does not create the formal procurement or manufacturing documents itself.

## Current State

DemandPlanning has no service directory. Wave 1 now provides the two required upstream facts:

1. ProductEngineering exposes released BOM, routing and ProductionVersion facts, including a ProductionVersion resolve API.
2. Inventory exposes stock movement, stock ledger and stock availability facts.

## Owned Facts

DemandPlanning owns:

1. DemandSource: forecast, sales-order demand, safety-stock demand or manual planning demand.
2. MasterProductionSchedule: planned finished-good demand bucketed by SKU and date.
3. MrpRun: a calculation run, input snapshot metadata, horizon and status.
4. PlanningSuggestion: planned purchase, planned work order, reschedule, cancel or expedite suggestion.
5. PeggingLink: traceability from suggestion to demand, BOM component, inventory input and upstream version reference.

DemandPlanning does not own:

1. Released EBOM, MBOM, Routing or ProductionVersion facts.
2. Stock balances, reservations or movements.
3. Purchase requisitions, RFQs, purchase orders or receipts.
4. Formal MES work orders or operation tasks.
5. Customer order or invoice state.

## Inputs

The MVP accepts input through adapters so the MRP algorithm can be tested without starting other services:

| Input | Source | MVP Handling |
| --- | --- | --- |
| Released production version | ProductEngineering resolve/list APIs or fixture adapter | Snapshot productionVersionId, mbomVersionId and routingVersionId in the run. |
| Released MBOM lines | ProductEngineering event/API snapshot | Use single-level BOM explosion in MVP. |
| Inventory availability | Inventory `GET /api/inventory/v1/availability` or fixture adapter | Snapshot available quantity by SKU/UOM/site. |
| Demand source | DemandPlanning command/API | Owned as planning input. |
| Planning parameters | DemandPlanning local defaults | Daily buckets, no finite capacity optimization. |

## MVP Calculation Rules

1. MRP runs by daily buckets.
2. The first release supports single-level MBOM explosion.
3. Finished-good net requirement equals demand quantity minus available finished-good quantity.
4. Planned work order suggestions are generated only for positive finished-good net requirement.
5. Component gross requirement equals planned work order quantity multiplied by MBOM quantity per parent.
6. Planned purchase suggestions are generated only for positive component net requirement.
7. All suggestions carry pegging refs back to the demand source and input version facts.
8. Suggestions are immutable once accepted, rejected or closed.
9. Rerun creates a new MrpRun and does not rewrite past accepted suggestions.

## Deterministic Fixture

The implementation must keep this fixture as a focused regression test:

| Input | Value |
| --- | --- |
| Demand | `SKU-FG-1000`, quantity `10`, due `2026-06-01` |
| Finished-good availability | `SKU-FG-1000`, quantity `2` |
| MBOM | `SKU-FG-1000` requires `SKU-RM-1000`, quantity `3` |
| Component availability | `SKU-RM-1000`, quantity `5` |

Expected suggestions:

| Suggestion | Quantity |
| --- | --- |
| planned work order for `SKU-FG-1000` | `8` |
| planned purchase for `SKU-RM-1000` | `19` |

## API Surface

| API | Purpose | Permission |
| --- | --- | --- |
| `POST /api/business/v1/planning/demands` | Create or update a demand source. | `business.planning.demands.manage` |
| `GET /api/business/v1/planning/demands` | List demand sources. | `business.planning.demands.read` |
| `POST /api/business/v1/planning/mrp-runs` | Run deterministic MRP for a horizon. | `business.planning.mrp.run` |
| `GET /api/business/v1/planning/mrp-runs` | List MRP runs. | `business.planning.mrp.read` |
| `GET /api/business/v1/planning/mrp-runs/{runId}/pegging` | Read pegging for a run. | `business.planning.mrp.read` |
| `GET /api/business/v1/planning/suggestions` | List suggestions. | `business.planning.mrp.read` |
| `POST /api/business/v1/planning/suggestions/{suggestionId}/accept` | Mark a suggestion accepted by a downstream service. | `business.planning.suggestions.manage` |

## Events

DemandPlanning publishes ADR 0011 envelope events:

1. `demandPlanning.MrpRunCompleted`
2. `demandPlanning.PlannedPurchaseSuggested`
3. `demandPlanning.PlannedWorkOrderSuggested`
4. `demandPlanning.PlanningSuggestionAccepted`

Events carry public IDs, SKU/UOM/site/date dimensions, quantities, productionVersionId when relevant and pegging refs. They must not carry database row internals or cross-schema foreign keys.

## Permissions

Initial permission codes:

1. `business.planning.demands.read`
2. `business.planning.demands.manage`
3. `business.planning.mrp.read`
4. `business.planning.mrp.run`
5. `business.planning.suggestions.manage`

## Persistence

Default schema: `demand_planning`.

Required tables:

1. `demand_sources`
2. `master_production_schedules`
3. `mrp_runs`
4. `planning_suggestions`
5. `mrp_pegging_links`

Each table and business column requires schema comments. PostgreSQL migrations history must use `demand_planning.__EFMigrationsHistory`.

## Testing

Acceptance requires:

1. Domain tests for demand source lifecycle, MRP run status and suggestion lifecycle.
2. Pure MRP calculator tests using the deterministic fixture above.
3. Web tests for route shape, authorization, validation and operation IDs.
4. Schema convention tests using `Nerv.IIP.Testing`.
5. Integration event converter/serialization tests for the DemandPlanning event names.
6. Adapter tests proving ProductEngineering and Inventory inputs are represented as snapshots, not cross-service table reads.

