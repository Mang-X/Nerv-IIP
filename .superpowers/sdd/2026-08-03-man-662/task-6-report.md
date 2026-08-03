# MAN-662 Task 6 implementation report

## Status

Implemented the IndustrialTelemetry historian/alarm scheduler timer migration from base
`c3fb436dd39a6682ef09b8736efaba539d6a2661`. Both schedulers now construct their
`PeriodicTimer` from the already injected `TimeProvider`. Scope stayed within the four
brief-listed implementation/test files plus this report.

## Delivered behavior

- `TelemetryHistorianScheduler` and `AlarmEscalationScheduler` use
  `new PeriodicTimer(interval, timeProvider)`.
- The historian scheduler test now uses `FakeTimeProvider` and shared
  `Eventually.WaitAsync`; its timeout diagnostic preserves the observed
  `rollupExists` and `rawOldExists` values.
- A focused recurrence test waits for the initial historian run, inserts a second raw
  window, advances fake time by the configured one-hour interval, and observes the
  second hourly rollup without sleeping.
- The invalid alarm-enabled configuration test registers one concrete singleton and
  maps `IHostedService` to that same instance. It resolves the concrete scheduler and
  awaits `ExecuteTask` through `TestTimeout.RunAsync` with a one-second budget before
  checking that the host is not stopping.
- The former test-local polling loop and fixed 100 ms wait were removed.

## TDD RED evidence

Command:

```bash
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --configuration Release --filter 'FullyQualifiedName~IndustrialTelemetryHistorianTests|FullyQualifiedName~Alarm_escalation_scheduler_does_not_stop_host_for_invalid_enabled_configuration'
```

Before the production timer changes, the command exited 1: 15 passed, 1 failed,
1 PostgreSQL-gated test skipped, 17 total. The expected recurrence test failed after
two seconds with `EventuallyTimeoutException`; its last observation was
`rollupExists=False; rawOldExists=False`. This proved that advancing the injected fake
clock did not advance the existing system-time timer.

## GREEN evidence

The same focused command exited 0 after the minimal timer changes: 16 passed,
0 failed, 1 PostgreSQL-gated test skipped, 17 total; test duration 561 ms.

Complete IndustrialTelemetry project:

```bash
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --configuration Release
```

Result: exit 0; 241 passed, 0 failed, 8 PostgreSQL-gated tests skipped, 249 total;
test duration 54 seconds.

## Files

- `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Scheduling/TelemetryHistorianScheduler.cs`
- `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Scheduling/AlarmEscalationScheduler.cs`
- `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryHistorianTests.cs`
- `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointContractTests.cs`
- `.superpowers/sdd/2026-08-03-man-662/task-6-report.md`

## Self-review and boundaries

- Historian retention/downsampling behavior and alarm command payloads are unchanged.
- Endpoint discovery, test-file decomposition, host sharing, contracts, database,
  OpenAPI, generated clients, and MAN-664 work are unchanged.
- No fixed 100 ms wait remains in the invalid alarm configuration test, and the test
  does not assume `AddHostedService<T>()` makes `T` directly resolvable.
- No push, PR, or Linear action was performed.

Concerns: none within Task 6 scope. Real PostgreSQL tests remain environment-gated and
were reported as skipped rather than passed.
