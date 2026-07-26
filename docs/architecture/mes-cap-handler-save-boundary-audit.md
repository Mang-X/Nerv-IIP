# MES CAP Handler Save-Boundary Audit

## Scope and evidence model

MAN-421 / GitHub #754 audits every current BusinessMES consumer discovered by:

```text
rg -n --glob '*.cs' 'IIntegrationEventHandler<|ICapSubscribe|\[IntegrationEventConsumer' \
  backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers
```

The 2026-07-26 pre-change inventory contains **16 consumer classes in 13
files**. Each row separates CAP subscription/invocation, handler outcome, and
independently reloaded PostgreSQL persistence outcome so that these states are
not conflated:

- **not invoked**: CAP never selected or scheduled the subscriber;
- **invoked and failed**: the subscriber ran but threw or the envelope guard
  rejected the event;
- **invoked successfully but not saved**: the subscriber returned, while its
  scoped EF changes remained only in the change tracker.

An explicit `ApplicationDbContext.SaveChangesAsync`, command UnitOfWork, or
scope coordinator that saves and commits is an equivalent durable boundary.
The persistent dead-letter store uses the same scoped `ApplicationDbContext`
and calls `SaveChangesAsync`; therefore it can save other tracked state and is
not, by itself, proof that a handler has a safe atomic boundary.

## Exhaustive handler matrix

`Pre-change` records the source audit before MAN-421 production edits. `Final
evidence` is completed by the focused PostgreSQL regressions and the full MES
targeted gate.

| Handler / consumer | Topic | MES mutations | Boundary and inbox | Business exception / dead letter | Replay / duplicate semantics | Pre-change | Independent-DbContext PostgreSQL evidence | Final verdict |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AssetUnavailableIntegrationEventHandlerForReschedule` / `business-mes.asset-unavailable` | `AssetUnavailableIntegrationEvent` | Open work-center unavailability; optional schedule result | Inbox first; explicit `SaveChangesAsync` after all mutations | Envelope guard persists terminal rejections | Event/idempotency inbox suppresses replay | Explicit atomic save delivered by MAN-507 / #920; MAN-421 does not modify it | Real CAP InMemory-transport/PostgreSQL test reloaded one inbox, open window, and schedule result (focused GREEN 11/11) | MAN-507 baseline; sound |
| `AssetRestoredIntegrationEventHandlerForReschedule` / `business-mes.asset-restored` | `AssetRestoredIntegrationEvent` | Close open unavailability; optional schedule result | Inbox first; MAN-421 adds explicit `SaveChangesAsync` after all optional mutations | Envelope guard only | Durable inbox suppresses replay across new scopes | Invoked successfully but not saved | PostgreSQL RED reloaded neither close/result nor inbox; GREEN independently reloads the closed window, one result, and one inbox before and after replay | Fixed by MAN-421 |
| `NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect` / `business-mes.quality-ncr-disposition` | `NcrDispositionDecidedIntegrationEvent` | Accept NCR disposition on a defect | Inbox first; MAN-421 explicitly saves successful mutation and no-match/blank-source terminal branches | Required disposition fields are validated before mutation; `ArgumentException`/`InvalidOperationException` reload the tracked defect before terminal persistent DLQ `quality-ncr-disposition-divergence` is saved | Durable inbox suppresses replay; concurrent same-key deliveries converge to one inbox and the matching winner mutation, while the loser tracker is cleared by the existing inbox unique-conflict policy | Invoked successfully but not saved; business divergence could poison CAP and a persistent DLQ save could commit partial tracked fields | PostgreSQL proves durable disposition/replay, terminal DLQ with no partial defect mutation, and concurrent unique-conflict convergence with one inbox | Fixed by MAN-421 |
| `PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder` / `business-mes.demand-planning-suggestion-accepted` | `PlanningSuggestionAcceptedIntegrationEvent` | Work order, operation tasks, optional schedule/material snapshots, coding state | Successful create uses `IMesSkuAvailabilityScopeCoordinator`, which saves/commits inbox and mutations; MAN-421 explicitly saves the existing-work-order terminal branch | Disabled SKU/routing absence becomes persistent dead letter; the store saves all currently tracked state | Created source reference and durable inbox suppress replay, including an already-existing business fact | Main mutation was coordinator-backed; early existing-fact branch was invoked successfully but not saved | PostgreSQL early-return test independently reloads the existing work order and one accepted-event inbox without creating another work order | Fixed early return by MAN-421 |
| `EngineeringChangeReleasedIntegrationEventHandlerForMesWip` / `business-mes.product-engineering-change-released` | `EngineeringChangeReleasedIntegrationEvent` | Archived-version markers, WIP impacts, work-order rebind/hold, derived events | Runtime `ITransactionUnitOfWork` owns transaction; handler saves then commits inbox and all mutations | Envelope guard; handler rollback on failure | Inbox plus impact uniqueness checks suppress replay | Explicit transaction/save/rollback boundary | Existing provider-backed tests; final gate rechecks | Sound |
| `ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders` / `business-mes.product-engineering-production-version-created` | `ProductionVersionCreatedIntegrationEvent` | Bind matching Created work orders lacking a production version | Inbox first; MAN-421 adds explicit `SaveChangesAsync` after all matching work orders are bound | Envelope guard only | Durable inbox suppresses replay across new scopes | Invoked successfully but not saved | PostgreSQL RED reloaded an unbound work order; GREEN reloads the production-version binding and one inbox before and after replay | Fixed by MAN-421 |
| `QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext` / `business-mes.quality-inspection-result` | `InspectionResultIntegrationEvent` | Hold projection and append-only transitions | Explicit `SaveChangesAsync` commits inbox, hold, transition, or terminal DLQ | Unknown/divergent source and domain `ArgumentException`/`InvalidOperationException` become persistent dead letters | Inbox suppresses replay; unique-conflict loser is retried to winner inbox | Explicit boundary delivered by MAN-429 / #777; persistent-store save is safe only because mutation validation precedes assignment | Existing real CAP test reloads four hold transitions/inboxes | Sound |
| `StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted` / `business-mes.stock-movement-posted` | `StockMovementPostedIntegrationEvent` | Mark matching finished-goods receipt posted | Matching branch explicitly saves inbox and receipt; MAN-421 also saves the inbox before missing/mismatched receipt returns | Envelope guard only; irrelevant source/type is filtered before inbox | Matching and accepted no-match deliveries both have durable replay suppression | Main mutation saved; post-inbox early returns were invoked successfully but not saved | PostgreSQL early-return test reloads one accepted-event inbox and no unrelated receipt fact | Fixed early returns by MAN-421 |
| `StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed` / `business-mes.stock-movement-posting-failed` | `StockMovementPostingFailedIntegrationEvent` | Mark receipt, production consumption, or material transfer posting failed | Recognized branches explicitly save inbox and mutation; MAN-421 saves the inbox after an unknown idempotency-prefix warning | Envelope guard; unmatched fact is warning-only | Recognized and unknown accepted deliveries both have durable replay suppression | Main mutations saved; unknown branch was invoked successfully but not saved | PostgreSQL early-return test reloads one unknown-key inbox and no unrelated posting fact | Fixed unknown branch by MAN-421 |
| `InventoryReservationExpiredIntegrationEventHandlerForMarkMesRequestExpired` / `business-mes.stock-reservation-expired` | `InventoryReservationExpiredIntegrationEvent` | Mark matching MES material issue request reservation expired | MES-scoped event records inbox then explicitly saves even when request is absent | Envelope guard; non-MES source filtered before inbox | Inbox suppresses replay | Explicit save | Existing consumer tests; final gate rechecks | Sound |
| `SchedulePlanReleasedIntegrationEventHandlerForDispatch` / `business-mes.schedule-plan-released-dispatch` | `SchedulePlanReleasedIntegrationEvent` | Assignment provenance, queued task upsert, legacy reconciliation | `IMesScheduleReleaseScopeCoordinator` serializes, saves, and commits inbox/mutations/DLQ in one transaction | Invalid payload, stale/revoked release, invalid/closed/active operations become persistent DLQ | Inbox suppresses replay; release revision/provenance preserves ordering | Coordinator-owned atomic boundary; no redundant save required | Required PostgreSQL proof reloads plan id, release revision, resource/work-center assignment, planned timing, and one inbox before and after replay | Sound; no MAN-421 production change |
| `SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch` / `business-mes.schedule-plan-revoked-withdraw-dispatch` | `SchedulePlanRevokedIntegrationEvent` | Revocation watermark; withdraw eligible assignments | Same schedule-release coordinator saves/commits inbox and mutations | Invalid revocation becomes persistent DLQ inside coordinator transaction | Inbox and revision watermark suppress stale replay | Coordinator-owned atomic boundary | Existing handler/PostgreSQL provenance tests; final gate rechecks | Sound |
| `SchedulePlanInvalidatedIntegrationEventHandlerForMarkInvalidated` / `business-mes.schedule-plan-invalidated` | `SchedulePlanInvalidatedIntegrationEvent` | Mark affected operation assignments invalidated | Explicit save on both empty and non-empty operation sets | Envelope guard | Inbox suppresses replay | Explicit save | Existing handler tests; final gate rechecks | Sound |
| `SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability` / `business-mes.sku-availability` | `SkuDisabledIntegrationEvent` | Create/update disabled SKU projection | `IMesSkuAvailabilityScopeCoordinator` serializes and saves inbox plus projection | Unexpected source/invalid payload becomes persistent DLQ before business tracking | Inbox and event-time projection rules make replay idempotent | Coordinator-owned atomic boundary | Existing provider tests rechecked; one routing-fixture timing assertion fails identically on frozen base `68dae3c8`, before entering the coordinator lock | Sound source boundary; unrelated frozen-base test gap |
| `TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport` / `business-mes.industrial-telemetry-production-count` | `TelemetryProductionCountDeltaIntegrationEvent` | Draft/pending candidate or posted production report and related domain facts | Candidate branches explicitly save inbox/fact; posted branch delegates successful work to the MediatR command UnitOfWork | Invalid payload becomes DLQ before inbox; MAN-421 catches terminal `KnownException`/`ArgumentException`/`InvalidOperationException`, clears all command-side tracked mutations, re-records inbox, and persists `telemetry-production-report-divergence` | Candidate and successful posted paths retain existing idempotency; terminal posted divergence becomes one durable inbox/DLQ and replay no-op | Posted command could increment cost-report state before a later quantity rule threw, poisoning CAP; saving DLQ on that tracker would commit the partial mutation | PostgreSQL RED captured the escaping over-tolerance `KnownException`; GREEN reloads zero progress/cost-report mutation, no report, one inbox, and one terminal DLQ before and after replay | Fixed by MAN-421 |
| `WorkOrderCostCapitalizedIntegrationEventHandler` / `business-mes.work-order-cost-capitalized` | `WorkOrderCostCapitalizedIntegrationEvent` | Capitalized unit-cost projection; requested receipt backfill and outbox events | Work-order scope coordinator owns PostgreSQL transaction/lock; successful path uses `SaveEntitiesAsync`; aggregate validation/conflict clears tracked business state, re-records inbox, and persists terminal `work-order-capitalization-divergence` in the same coordinator transaction | Missing work order still throws and rolls back; only aggregate `ArgumentException`/`InvalidOperationException` is normalized; infrastructure and cancellation failures escape | Same-cost replay remains a success no-op; terminal divergence replay observes the single durable inbox/DLQ | Review found work-order or later-receipt cost conflict could escape after inbox/tracked mutation, so coordinator rolled back and CAP retried forever | Two PostgreSQL REDs captured work-order and later-receipt conflicts; GREEN reloads original costs, no CAP outbox, and exactly one inbox/DLQ before and after fresh-context replay | Fixed terminal divergence by MAN-421 review |

## Governance impact

- HTTP endpoints and Gateway facades: unchanged.
- OpenAPI snapshots, generated clients, and public integration contracts:
  unchanged.
- Database schema, migrations, and schema catalog: unchanged.
- Product documentation under `frontend/apps/docs`: no impact.
- `integration-event-consumption-matrix.md`: update only if the final audit
  changes a consumer's integration semantics or classification.

MAN-421 changes terminal business-divergence and durable no-match semantics, so
the consumption matrix is updated without changing any row's
`consumed-internally` classification.

## Verification record

- Pre-change focused RED (PostgreSQL 18): 7 tests selected, 7 failed. Five
  failures were the expected missing durable facts or escaping NCR business
  exception. Two assertions were corrected as test-model issues before final
  evidence: schedule assignments use `EarliestStartUtc`/`Duration` rather than
  execution timestamps, and the existing inbox policy absorbs the PostgreSQL
  unique-key loser instead of surfacing `DbUpdateException`.
- Additional Telemetry RED: 1 selected, 1 failed with
  `KnownException: Reported quantity exceeds work order tolerance.`
- Review-follow-up capitalization RED: 2 selected, 2 failed with the expected
  different-cost conflicts from the work order and a later requested receipt.
  The receipt case ran after the handler had already mutated the tracked work
  order, proving the partial-state risk before the terminal DLQ fix.
- Focused GREEN: the required save-boundary class plus the MAN-507 real CAP
  baseline selected 11 tests; 11 passed, 0 failed, 0 skipped.
- Complete MES Web test project: 379 tests selected; 378 passed and 1 failed.
  The remaining failure is the unchanged stacked-base
  `SkuDisabledConsumerTests.PostgreSQL_disable_commit_serializes_before_new_work_order_creation`.
  Its fixture supplies no routing snapshot, so the accepted-suggestion handler
  records a routing-missing dead letter and returns before entering the
  coordinator whose lock the assertion expects. The test, coordinator, and
  command path are unchanged from the stacked base. The exact isolated command
  was rerun in the existing frozen-base worktree at
  `68dae3c8befabf0957eeb7f4449ea1d2027be332`; it selected 1 test and failed 1
  with the identical assertion. MAN-421 does not widen scope to alter this
  separately proven frozen-base failure.
