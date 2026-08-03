# MAN-662 Task 4 Report — Inventory expiration clock and Prometheus registry isolation

## Status

Implemented from base `a237eecf8846b527ca8ab08c62d6caf411f5750c`. The Inventory expiration metrics and worker now consume the same injected `TimeProvider`; metrics collectors are instance-owned and created against an injected `CollectorRegistry`. Production DI supplies `TimeProvider.System` and `Metrics.DefaultRegistry`.

## Constructor-consumer audit

- `InventoryReservationMetrics`: production construction is through `Program.cs`; the only direct construction was the expiration test and is now registry/time-provider explicit.
- `ExpiredStockReservationHostedService`: production construction is through `AddHostedService`; there were no direct constructors before the new hosted-service recurrence test.
- No endpoint, schema, facade, generated contract, or business-semantic consumer changed.

## TDD evidence

### RED

Command:

```text
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --configuration Release --filter FullyQualifiedName~InventoryReservationExpirationTests
```

Result: exit 1. Compilation failed at the wished-for dependency boundary with four `CS1729` errors for `InventoryReservationMetrics(TimeProvider, CollectorRegistry)` and one `CS1729` error for `ExpiredStockReservationHostedService(..., TimeProvider)`. This was the expected missing-feature failure.

### GREEN

Focused command (final run):

```text
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --configuration Release --filter FullyQualifiedName~InventoryReservationExpirationTests
```

Result: exit 0; 9 passed, 0 failed, 0 skipped. The original 250 ms sleep is gone. `FakeTimeProvider` advances two minutes for the hanging gauge, two custom registries export exactly one hanging gauge sample each with isolated values `1` and `2`, and the hosted service expires a second reservation after one configured scan interval.

Full Inventory project command (final run):

```text
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --configuration Release
```

Result: exit 0; 233 passed, 0 failed, 2 skipped, 235 total.

The first full-project run had 232 passed, 1 failed, and 2 skipped: unchanged `InventorySourceLookupTests.Exact_mes_source_returns_only_its_movements_and_current_balances_for_the_posted_dimensions` failed in EF InMemory sorting with `ArgumentException: At least one object must implement IComparable`. The exact failing test then passed alone (1/1), and the unchanged full command passed on rerun. No SourceLookup file was changed.

## Files changed

- `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Program.cs`
- `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Expiry/InventoryReservationMetrics.cs`
- `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Expiry/ExpiredStockReservationHostedService.cs`
- `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryReservationExpirationTests.cs`
- `.superpowers/sdd/2026-08-03-man-662/task-4-report.md`

## Self-review and boundaries

- Metric names remain exactly `nerv_iip_inventory_hanging_stock_reservations` and `nerv_iip_inventory_stock_reservations_expired_total`.
- Both static collectors were deleted; each metrics instance creates its gauge and counter through `Metrics.WithCustomRegistry(registry)`.
- Both metric refresh and expiration scan use `timeProvider.GetUtcNow().UtcDateTime`; recurrence uses `PeriodicTimer(interval, timeProvider)`.
- Production DI registers the system clock and default registry before the metrics singleton and hosted service.
- `git diff --check` passed. No endpoint, schema, facade declaration, OpenAPI/generated code, domain logic, push, PR, or Linear action is part of this task.
- Remaining concern: the unrelated full-project EF InMemory ordering test was non-deterministic on the first run; both its isolated rerun and the final full-project rerun passed, and the original failure is retained above rather than hidden.
