# MAN-603 Simulated Device Connector Design

## Scope

Deliver Linear MAN-603 / GitHub #1088 as a first-class Connector Host adapter. The adapter runs continuously inside
`Nerv.IIP.ConnectorHost.Host`, models multiple device profiles, emits deterministic telemetry through the existing
IndustrialTelemetry samples contract, reports three AppHub instances with CollectionHealth, and executes
`device.control.command` with auditable correlated receipts.

The three canonical connector identities and AppHub instance keys are:

- `CONN-OPCUA-01` for the 17 machining/grinding/welding devices;
- `CONN-MQTT-01` for the 16 assembly/test devices;
- `CONN-MODBUS-01` for the 13 coating/packaging/utility devices.

The governed script simulator delivered by #1086 is not modified. No backend or frontend implementation project is
referenced from `connector-hosts`; only the already-approved public Contracts/SDK project boundary remains.

## Considered Approaches

1. **A dedicated simulated connector adapter in the Connector Host solution (chosen).** One adapter owns three
   configured logical connector runtimes. It uses the same `IConnector`, `IIndustrialTelemetryCollectionConnector`,
   `IConnectorConnectionMonitor`, and `IConnectorOperationExecutor` seams as physical adapters, so AppHub reporting,
   tag manifests, collection health, Ops leasing, and result submission all exercise production paths.
2. **Mock transports under the OPC UA, MQTT, and Modbus adapters.** This would couple simulation semantics to three
   protocol client implementations, duplicate orchestration, and make device isolation and command idempotency harder
   to prove.
3. **Extend the #1086 PowerShell simulator.** This would remain an external short-run HTTP writer and could not prove
   Connector Host registration, heartbeat, CollectionHealth, operation leasing, cancellation, or resource cleanup.

## Internal Boundary and Shared Sample Contract

`RecordIndustrialTelemetrySampleRequest` and `IIndustrialTelemetrySamplesClient` are transport-neutral Connector Host
abstractions, but currently live in the OPC UA project. Move them to
`Nerv.IIP.ConnectorHost.Connectors.Abstractions`, and move the HTTP implementation to
`Nerv.IIP.ConnectorHost.Application`. The OPC UA, MQTT, Modbus, and simulated projects consume that stable internal
seam. Remove MQTT/Modbus references to the OPC UA adapter when the move makes them unnecessary.

This is not a platform protocol change. The public Connector Protocol v1 and Ops v1 DTOs remain owned by
`backend/common/Contracts` and their SDKs; the new adapter only supplies data to those existing contracts.

## Configuration Model

`Simulated:Enabled` is false by default. The section contains:

- a deterministic integer `Seed`;
- bounded `MaxDeliveryAttempts`, `RetryBaseMilliseconds`, `MaxPendingSamples`, and
  `CommandReceiptCacheCapacity`;
- four named phases `normal`, `degrading`, `alarm`, and `recovered`, each with a configurable duration;
- three connector profiles with exact connector/instance key, source system, display name, and device groups;
- compact device groups with a prefix, count, starting ordinal, default profile, and tag definitions;
- per-device overrides for the three rolling alarm demonstrations:
  `DEV-CNC-03/vibration`, `DEV-CTG-02/bath-temperature`, and `DEV-AUX-04/air-pressure`.

The checked-in Development profile describes the complete 46-device/96-tag L0 world through compact groups. It
contains ranges, units, protocol-address templates, and writable limits, but no credentials or customer secrets.
Aspire enables the adapter only for the existing leader-demo world profile and supplies service URLs/tokens through
the existing secret/reference mechanisms.

Default phase durations total 45 minutes. The three alarm devices use phase offsets so their degrading, alarm, and
recovered windows are staggered. Tests and process acceptance override durations to seconds; production behavior never
depends on wall-clock sleeps in tests.

## Deterministic Scenario and Device Isolation

A pure scenario evaluator consumes `TimeProvider.GetUtcNow()`, the configured epoch, connector/device/tag identity,
the phase profile, and the configured seed. The pseudo-random value for a point is derived from a stable hash of
`seed + connector + device + tag + cycle`, never from a shared mutable `Random`. Therefore:

- advancing a controlled clock reaches exact phase boundaries;
- the same seed, identity, and cycle produces the same value and source sequence;
- adding/reordering devices cannot change another device's stream;
- a command override for one device/tag cannot leak into another runtime.

`normal` emits bounded baseline noise, `degrading` interpolates toward the configured alarm value, `alarm` stays
beyond the configured threshold, and `recovered` returns to the normal band. Successful control commands update only
the addressed device runtime and become observable in subsequent samples.

## Delivery, Health, and Cancellation

Each sample uses the existing IndustrialTelemetry internal HTTP client and a stable source sequence derived from
connector/device/tag/cycle. Failed deliveries retain the identical request for retry. Retry delays use `TimeProvider`
and exponential backoff, honor cancellation immediately, and never convert cancellation into collection failure.

The pending outbox and command-receipt cache are bounded. When the pending sample capacity is exhausted, the oldest
undelivered request is discarded and `DroppedCount` increases; a failed attempt increases `ErrorCount`; successful
submission increases `ReceivedCount` and advances `LastSampleAtUtc`. Counters have a process-stable `CounterEpoch`.
Each logical connector exposes an independent snapshot and an explicit simulated `alive` field-connection fact.

Discovery returns exactly three targets. Existing `ConnectorReportingLoop` therefore sends independent registration,
continuous heartbeat, state snapshot, CollectionHealth, and replace-style tag manifest messages for the three exact
instance keys.

## Control Command and Receipt Contract

The adapter accepts `device.control.command` only when organization, environment, Connector Host, instance key, and
device route match its configured ownership. It supports the existing command types:

- `write-tag`;
- `parameter-set`;
- `start-stop`.

`OperationTaskId` is the in-process idempotency key. A duplicate task returns the cached immutable execution result
without applying state twice. The bounded cache stores both successes and terminal failures.

Every execution output contains `connectorId`, `protocol`, `commandType`, `operationTaskId`, `correlationId`,
`deviceReceiptCode`, and `deviceReceiptMessage`. Multi-value commands also emit indexed receipt fields compatible
with the current OPC UA executor convention. Terminal outcomes are:

- `Good` for a successful write/state change;
- `BadNotFound` for an unknown device or tag;
- `BadNotSupported` for an unknown command type or non-writable tag;
- `BadOutOfRange` for a configured value-domain violation.

Validation failures are non-retryable and keep their receipt data. Transport/runtime exceptions remain retryable
through the existing operation loop classification. `ConnectorOperationLoop` uses its injected `TimeProvider` for
started/finished timestamps so command-result evidence is controllable and correlated.

## Runtime Wiring and Real Evidence

The Host registers the simulated adapter only when enabled and adds it to all four existing interface collections.
The leader-demo AppHost profile sets the flag, a two-second collection cycle, and a one-second operation poll; it
references and waits for IndustrialTelemetry in addition to the existing AppHub/IAM/Ops dependencies.

A real-process acceptance test launches the built Connector Host executable (not AppHost and not `dotnet run`) against
bounded loopback HTTP fakes. It waits for all three registrations, multiple heartbeats, three CollectionHealth state
snapshots, telemetry samples, one claimed control task, and its correlated result, then cancels the child and proves
bounded exit/resource cleanup. A separate controlled-time long-run test advances many cycles without sleeping and
asserts deterministic phase repetition, bounded queues/caches, and clean cancellation.

Because another task owns the backend full-solution gate, this delivery never runs
`dotnet test backend/Nerv.IIP.sln`. Platform-side validation is limited to the AppHost project build unless a later
fact requires the governed `nerv.ps1 fullstack run` path.

## Verification and Documentation Impact

Required evidence:

- RED then GREEN focused tests for phase progression, stable seed, device isolation, three-instance reporting,
  CollectionHealth, retry/cancellation, command success/failure/unknown/duplicate behavior, and result correlation;
- full `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln`;
- the controlled-time long-run test and real OS-process acceptance test;
- `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`;
- `git diff --check`;
- script governance only if a governed script is changed.

Product documentation is unaffected because no end-user page or flow changes. Architecture readiness, Connector
Protocol v1 operational guidance, and Aspire operator documentation are updated. No HTTP endpoint, database schema,
facade matrix row, OpenAPI snapshot, or generated frontend client changes.
