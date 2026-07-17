# Connector Observability and Tag Manifest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close #951 and #947 by shipping one canonical connector identity, authoritative field-connection facts, a configured tag manifest, connector-to-tag coverage, and a deterministic plus real-process acceptance chain in PR #952.

**Architecture:** Tranche A first separates Connector Host liveness, field connectivity, collector health, and sample presence, then persists and exposes those facts through AppHub and BusinessGateway. Tranche B makes IndustrialTelemetry the owner of a replace-style connector tag manifest and derives coverage by left-joining current bindings to `TelemetrySummary`; the Host reports manifests independently of sample delivery. A governed loopback Modbus acceptance proves disconnect and recovery through the real process boundary without adding cross-solution references.

**Tech Stack:** .NET 10, FastEndpoints, EF Core/PostgreSQL, Connector Protocol v1 additive records, Aspire, PowerShell/ScriptAutomation, Vue 3, TanStack Vue Query, Vitest, Vite+, Hey API code generation.

## Global Constraints

- Keep `connector-hosts/` separate from `backend/` and `frontend/`; do not add cross-solution project references.
- Preserve `sourceConnector` as nullable provenance; use `(organizationId, environmentId, collectionConnectorId)` as the canonical connector identity.
- All protocol additions are optional trailing fields within protocol major version 1; old Hosts and existing nullable rows remain valid.
- Field connection detection is at most 4 seconds, transition reporting/persistence at most 2 seconds, Gateway allowance at most 2 seconds, and backend visibility at most 8 seconds.
- Use `ConnectorHost:HeartbeatSeconds=2`, `ConnectorHost:ConnectionProbeSeconds=4`, and `CollectionHealth:HostLivenessTimeout=00:00:06`; validate timeout is at least three heartbeat periods and no more than 8 seconds.
- Sampling intervals may be slower than 4 seconds; only the Modbus liveness probe is forced into the connection-detection budget.
- Keep `staleReason=offline|fault`; add `offlineReason=field-connection|host-liveness` without deriving field loss from sample silence.
- #947 exposes `disabled`, `activation error`, `never sampled`, and `sampled`; do not fabricate `current`, `stale`, or `bad` without an authoritative threshold and quality fact.
- Manifest ingestion uses `InternalServiceAuthorizationPolicy.Name` and facade classification `internal`; coverage read uses the same service policy and BusinessGateway classification `exposed`.
- Register both IndustrialTelemetry operations in `IndustrialTelemetryEndpointContracts.All`; refresh OpenAPI and generated client only through repository commands.
- Generate EF migrations with `dotnet-ef`; do not hand-edit generated snapshots or generated client files.
- Every production behavior follows RED-GREEN-REFACTOR and is committed only after its focused tests pass.
- Leave the existing uncommitted `skills-lock.json` change untouched and out of every commit.

---

## Tranche A — #951 authoritative connection state

### Task 1: Canonical connector identity and additive connection contract

**Files:**
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/ConnectorProtocolContracts.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/ConnectorAbstractions.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.OpcUa/OpcUaContracts.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Modbus/ModbusContracts.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Mqtt/MqttContracts.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.OpcUa/OpcUaConnector.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Modbus/ModbusConnector.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Mqtt/MqttConnector.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/ConnectorReportingLoop.cs`
- Test: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/ReportingLoopTests.cs`
- Test: the three existing `*TelemetryCollectorTests.cs` files under `connector-hosts/tests/`

**Interfaces:**
- Produces: `ConnectorConnectionState` and `ConnectorConnectionStateSnapshot` with identical fields.
- Produces: effective connector ID `CollectionConnectorId ?? $"{protocol}-{ConnectorId}"` in all three adapters.
- Produces: `ConnectorCollectionHealth(..., ConnectorConnectionState? Connection = null)` and matching Host snapshot.

- [ ] **Step 1: Write the failing protocol and identity tests**

```csharp
[Fact]
public async Task Configured_collection_connector_id_is_identical_in_registration_and_collection_health()
{
    var target = ConnectorFixtures.Target(collectionConnectorId: "line-a-primary");
    await RunReportingAsync(target);
    Assert.Equal("line-a-primary", client.Registration.InstanceKey);
    Assert.Equal("line-a-primary", client.State.CollectionHealth!.ConnectorId);
}

[Fact]
public async Task Missing_collection_connector_id_preserves_legacy_derived_identity()
{
    var connector = CreateModbusConnector(collectionConnectorId: null, connectorId: "line-1");
    var target = Assert.Single(await connector.DiscoverAsync(default));
    Assert.Equal("modbus-line-1", target.InstanceKey);
}

[Fact]
public async Task Reports_optional_connection_state_without_changing_legacy_payload()
{
    var observed = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
    await RunReportingAsync(ConnectorFixtures.Target(connection: new("lost", observed, null, observed, "transport", "socket-closed")));
    Assert.Equal("lost", client.State.CollectionHealth!.Connection!.Status);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/Nerv.IIP.ConnectorHost.Application.Tests.csproj --filter "FullyQualifiedName~ReportingLoopTests"`

Expected: FAIL because the connection records and explicit collection connector ID do not exist.

- [ ] **Step 3: Add the exact additive records and effective-ID rule**

```csharp
public sealed record ConnectorConnectionState(
    string Status,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ConnectedSinceUtc = null,
    DateTimeOffset? DisconnectedSinceUtc = null,
    string? ReasonCategory = null,
    string? DiagnosticCode = null);

public sealed record ConnectorConnectionStateSnapshot(
    string Status,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ConnectedSinceUtc = null,
    DateTimeOffset? DisconnectedSinceUtc = null,
    string? ReasonCategory = null,
    string? DiagnosticCode = null);
```

Append `Connection = null` to both collection-health records and `string? CollectionConnectorId = null` to each protocol options record. Add a single `EffectiveCollectionConnectorId` property per options record and use it for target `InstanceKey`, collection health `ConnectorId`, and later sample/manifest payloads. Bind `{Protocol}:CollectionConnectorId` in Host configuration and set the development example to the former derived value.

- [ ] **Step 4: Run all connector contract tests and verify GREEN**

Run: `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln`

Expected: PASS with legacy and explicit identities both covered.

- [ ] **Step 5: Commit**

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol connector-hosts/src connector-hosts/tests
git commit -m "feat(connector-protocol): add canonical connection identity"
```

### Task 2: Monotonic connection tracker, coalesced signal, and decoupled workers

**Files:**
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/ConnectorConnectionStateTracker.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/ConnectorReportSignal.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/ConnectorHostWorkerOptions.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/ConnectorAbstractions.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Worker.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/IndustrialTelemetryCollectorRunner.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/ConnectorReportingLoop.cs`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/ConnectorReportSignalTests.cs`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/WorkerTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/IndustrialTelemetryCollectorRunnerTests.cs`

**Interfaces:**
- Produces: `IConnectorReportSignal.Signal(string)` and `WaitAsync(TimeSpan, TimeProvider, CancellationToken)` backed by a capacity-one channel.
- Produces: `IConnectorConnectionMonitor.RunConnectionCheckAsync(CancellationToken)` for Modbus only.
- Produces: independent collection, connection-monitor, serialized reporting, and Ops loops.

- [ ] **Step 1: Write failing state-machine and scheduling tests**

```csharp
[Fact]
public void Repeated_identical_state_is_coalesced()
{
    tracker.MarkLost("transport", "socket-closed");
    var first = tracker.Snapshot;
    tracker.MarkLost("transport", "socket-closed");
    Assert.Same(first, tracker.Snapshot);
    Assert.Equal(1, signal.Count);
}

[Fact]
public async Task Connection_monitor_runs_independently_of_slow_collection()
{
    collection.Block();
    await clock.AdvanceAsync(TimeSpan.FromSeconds(4));
    Assert.True(connectionMonitor.Calls >= 1);
    Assert.True(reporting.Calls >= 2);
}
```

Also cover recovery creating a new `ConnectedSinceUtc`, loss preserving `DisconnectedSinceUtc`, one slow collector not blocking another, a transition waking reporting before the periodic tick, registration preceding heartbeat/state for each target, and Ops polling while collection is blocked.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/Nerv.IIP.ConnectorHost.Host.Tests.csproj --filter "FullyQualifiedName~WorkerTests|FullyQualifiedName~IndustrialTelemetryCollectorRunnerTests"`

Expected: FAIL because Worker is one serial cycle and no monitor/signal exists.

- [ ] **Step 3: Implement the state tracker and fixed product profile**

```csharp
public interface IConnectorConnectionMonitor
{
    Task RunConnectionCheckAsync(CancellationToken cancellationToken);
}

public sealed class ConnectorHostWorkerOptions
{
    public const string SectionName = "ConnectorHost";
    public int HeartbeatSeconds { get; init; } = 2;
    public int ConnectionProbeSeconds { get; init; } = 4;
    public int CollectionCycleSeconds { get; init; } = 30;
    public int OperationPollSeconds { get; init; } = 30;
    public int ConnectionDetectionBudgetSeconds { get; init; } = 4;
    public int BackendDeadlineSeconds { get; init; } = 8;
}
```

Validate positive periods, detection budget `<=4`, heartbeat `==2` for the governed profile, and backend deadline `<=8`. Run per-collector collection loops, per-monitor probe loops, one serialized report loop awakened by either its 2-second period or the capacity-one signal, and one Ops loop under `Task.WhenAll`. A normal heartbeat always reports Host process reachability as `true`; collector degradation stays in collection health.

- [ ] **Step 4: Run connector Host and Application tests and verify GREEN**

Run: `dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/Nerv.IIP.ConnectorHost.Application.Tests.csproj`

Run: `dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/Nerv.IIP.ConnectorHost.Host.Tests.csproj`

Expected: PASS without wall-clock sleeps; use fake `TimeProvider` and controllable tasks.

- [ ] **Step 5: Commit**

```powershell
git add connector-hosts/src connector-hosts/tests
git commit -m "refactor(connector-host): decouple health reporting loops"
```

### Task 3: Protocol-specific alive, lost, and recovery semantics

**Files:**
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Modbus/ModbusTcpClient.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Modbus/ModbusConnector.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Modbus/ModbusContracts.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Mqtt/MqttNetSubscriptionClient.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Mqtt/MqttConnector.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Mqtt/MqttContracts.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.OpcUa/OpcUaNetStandardClient.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.OpcUa/OpcUaConnector.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.OpcUa/OpcUaContracts.cs`
- Test: all corresponding connector unit and integration test files.

**Interfaces:**
- Consumes: `ConnectorConnectionStateTracker`, `IConnectorReportSignal`, `IConnectorConnectionMonitor`.
- Produces: explicit unknown/alive/lost snapshots without using sample POST success as connectivity.

- [ ] **Step 1: Add failing adapter tests**

For Modbus, assert a successful protocol transaction—not TCP connect alone—marks alive; a transport timeout marks lost; recovery creates a new interval; sample POST failure does not mark lost; and `ProbeAsync(firstMapping)` changes no sample counters. For MQTT, assert CONNACK plus subscription acknowledgement marks alive, `DisconnectedAsync` marks lost immediately, resubscription recovers, and invalid payload does not mark lost. For OPC UA, assert Session plus Subscription `ApplyChanges` marks alive, bad keep-alive marks lost immediately, reconnect recovers, and telemetry POST failure does not mark lost.

- [ ] **Step 2: Run each adapter suite and verify RED**

Run the three commands:

```powershell
dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Modbus.Tests/Nerv.IIP.ConnectorHost.Connectors.Modbus.Tests.csproj
dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Mqtt.Tests/Nerv.IIP.ConnectorHost.Connectors.Mqtt.Tests.csproj
dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests/Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests.csproj
```

Expected: the new transition assertions fail while existing sampling tests remain green.

- [ ] **Step 3: Implement the minimal authoritative transitions**

Use a short async gate around Modbus protocol I/O, a maximum four-second linked cancellation timeout, and the first enabled mapping as an active probe; no mapping leaves state unknown. Wire MQTTnet disconnect and OPC UA bad keep-alive callbacks directly to `MarkLost("transport", stableCode)`. Only protocol transport/session failures change connection state; parsing, validation, and IndustrialTelemetry delivery failures remain collector errors.

- [ ] **Step 4: Re-run all three suites and verify GREEN**

Expected: PASS, including recovery interval and non-connection-error cases.

- [ ] **Step 5: Commit**

```powershell
git add connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.* connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.*
git commit -m "feat(connector-host): report protocol connection transitions"
```

### Task 4: AppHub connection projection, unified liveness, and migration

**Files:**
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/AggregatesModel/ApplicationInstanceAggregate/ApplicationInstance.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/ApplicationInstanceEntityTypeConfiguration.cs`
- Create: generated `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/*_AddConnectorConnectionState.cs`
- Create: matching generated Designer file
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Connectors/ConnectorCollectionHealthOptions.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Connectors/ConnectorCollectionHealthEvaluator.cs`
- Modify: both AppHub connector collection-health endpoints
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Commands/AppHubHeartbeatTimeoutScanner.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/appsettings.json`
- Modify: Connector Protocol response records in `ConnectorProtocolContracts.cs`
- Test: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/ConnectorCollectionHealthProjectionTests.cs`
- Test: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubConnectorEndpointTests.cs`
- Test: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubSchemaConventionTests.cs`

**Interfaces:**
- Produces nullable projection fields `ConnectionStatus`, `ConnectionObservedAtUtc`, `ConnectedSinceUtc`, `DisconnectedSinceUtc`, `ConnectionReasonCategory`, `ConnectionDiagnosticCode`.
- Produces additive response `Connection`, `StaleReason`, and `OfflineReason` for single and list reads.
- Produces one `CollectionHealth:HostLivenessTimeout` consumed by evaluator and scanner.

- [ ] **Step 1: Write failing ordering, precedence, and schema tests**

```csharp
[Fact]
public void Newer_connection_transition_updates_even_when_counter_report_is_stale()
{
    projection.Record(NewerCountersAlive);
    projection.Record(OlderCountersNewerLost);
    Assert.Equal(NewerCountersAlive.ReceivedCount, projection.ReceivedCount);
    Assert.Equal("lost", projection.ConnectionStatus);
}

[Fact]
public void Explicit_field_loss_precedes_fresh_host_heartbeat()
{
    var result = evaluator.Derive(FreshHeartbeat, LostConnection, RunningCollector, now);
    Assert.Equal(("stale", "offline", "field-connection"), (result.Status, result.StaleReason, result.OfflineReason));
}
```

Also cover stale connection observations unable to revive lost, null legacy reports not clearing facts, new counters with stale connection, host timeout producing `host-liveness`, simultaneous field and host loss preferring field, live connection plus terminal collector reporting fault, and invalid cadence/timeout configuration failing startup.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/Nerv.IIP.AppHub.Domain.Tests.csproj --filter "FullyQualifiedName~ConnectorCollectionHealthProjectionTests"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter "FullyQualifiedName~AppHubConnectorEndpointTests|FullyQualifiedName~AppHubSchemaConventionTests"
```

Expected: FAIL because connection observations are not persisted and the evaluator/scanner use different constants.

- [ ] **Step 3: Implement independent ordering and unified options**

Split `ConnectorCollectionHealthProjection.Record` into counter ordering by epoch/report time and connection ordering solely by `Connection.ObservedAtUtc`. Null legacy connection never clears a known fact. Validate state timestamps: alive requires only `ConnectedSinceUtc`, lost requires only `DisconnectedSinceUtc`, unknown has neither. Bind:

```json
"CollectionHealth": {
  "HostHeartbeatCadence": "00:00:02",
  "HostLivenessTimeout": "00:00:06",
  "BackendDeadline": "00:00:08"
}
```

Remove `ConnectorCollectionHealthEvaluator.StaleAfter`; inject the same options into query derivation and timeout scanning.

- [ ] **Step 4: Generate the migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddConnectorConnectionState --project backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure --startup-project backend/services/AppHub/src/Nerv.IIP.AppHub.Web
```

Expected: nullable columns with lengths 32/64/128 and database comments; no historical backfill.

- [ ] **Step 5: Re-run focused tests and commit**

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol backend/services/AppHub
git commit -m "feat(apphub): persist authoritative connector connectivity"
```

### Task 5: Forward and render field-vs-host connection facts

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleConnectorCollectionHealthModels.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayMaintenanceTelemetryTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Regenerate: BusinessGateway OpenAPI snapshot and generated business-console client
- Modify: `frontend/packages/api-client/src/business-console.ts` only if the stable barrel needs new generated symbols
- Modify: `frontend/apps/business-console/src/components/equipment/ConnectorHealthCard.vue`
- Modify: `frontend/apps/business-console/src/pages/equipment/connectorsPage.test.ts`
- Modify: `frontend/apps/business-console/src/composables/useBusinessTelemetry.test.ts`

**Interfaces:**
- Consumes: AppHub additive connection and offline-reason fields.
- Produces: generated API types and operator-facing separate field/Host disconnect labels and durations.

- [ ] **Step 1: Write failing Gateway and Vue tests**

Assert the Gateway preserves explicit lost connection and `field-connection`, preserves null connection for old Hosts, and exposes all new schema fields. In the card test, assert field loss renders “现场连接断开”, host timeout renders “采集主机离线”, and legacy null renders “连接状态未知”; do not infer any state from `lastSampleAtUtc`.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter "FullyQualifiedName~BusinessGatewayMaintenanceTelemetryTests|FullyQualifiedName~BusinessGatewayOpenApiTests"
pnpm -C frontend --filter @nerv-iip/business-console test -- connectorsPage.test.ts
```

- [ ] **Step 3: Mirror additive DTOs, export, codegen, and render**

Add `BusinessConsoleConnectorConnectionState` and append nullable `Connection`, `StaleReason`, and `OfflineReason` to both single/list models. Export the real snapshot and generate the client:

```powershell
scripts/export-gateway-openapi.ps1
pnpm -C frontend generate:api
```

Use generated types in the Vue component; keep existing 10-second polling unchanged.

- [ ] **Step 4: Run Gateway and frontend focused tests and commit**

```powershell
git add backend/gateway/BusinessGateway frontend/packages/api-client frontend/apps/business-console
git commit -m "feat(console): distinguish connector and host disconnects"
```

## Tranche B — #947 authoritative configured tag coverage

### Task 6: Manifest aggregate, sample connector provenance, and schema

**Files:**
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/ConnectorTagManifestAggregate/ConnectorTagManifest.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/ConnectorTagManifestAggregate/ConnectorTagBinding.cs`
- Create: two matching Infrastructure entity configurations
- Modify: `ApplicationDbContext.cs`
- Modify: `TelemetrySummary.cs` and `TelemetryRawSample.cs`
- Modify: both corresponding entity configurations
- Modify: sample request/command/handler paths in `IndustrialTelemetryEndpoints.cs` and `IndustrialTelemetryCommands.cs`
- Create: generated IndustrialTelemetry migration `*_AddConnectorTagManifestCoverage`
- Modify: migration snapshot
- Test: `IndustrialTelemetryAggregateTests.cs`, `IndustrialTelemetrySchemaConventionTests.cs`, and `IndustrialTelemetryHistorianTests.cs`

**Interfaces:**
- Produces: one manifest root per organization/environment/connector and one reusable current binding projection per connector/device/tag.
- Produces: nullable `CollectionConnectorId` on raw and summary samples; only summary receives the coverage join index.

- [ ] **Step 1: Write failing aggregate and schema tests**

Cover idempotent replay; older observation stale; same observation/different revision conflict; later observation rolling back to an earlier hash; omitted binding retirement; older activation unable to overwrite newer; organization/environment/connector isolation; nullable sample connector ID; summary coverage index; and absence of a raw coverage index.

- [ ] **Step 2: Run Domain and Web tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --filter "FullyQualifiedName~IndustrialTelemetrySchemaConventionTests|FullyQualifiedName~IndustrialTelemetryHistorianTests"
```

- [ ] **Step 3: Implement the manifest state model**

```csharp
public enum ManifestApplyDisposition { Accepted, Idempotent, Stale, Conflict }

public sealed record ManifestApplyResult(
    ManifestApplyDisposition Disposition,
    string AcceptedManifestRevision,
    DateTimeOffset AcceptedManifestObservedAtUtc);
```

Use unique keys `(organization, environment, collectionConnectorId)` and `(organization, environment, collectionConnectorId, deviceAssetId, tagKey)`. Keep removed bindings with `IsCurrent=false` and `RetiredAtUtc`; re-adding revives the same business projection. Limit revision to lowercase SHA-256, activation status to `pending|active|error|disabled`, and error text to sanitized bounded fields.

- [ ] **Step 4: Generate migration and verify GREEN**

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool run dotnet-ef migrations add AddConnectorTagManifestCoverage --project backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure --startup-project backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web
```

Re-run both test projects. Expected: PASS and no model drift.

- [ ] **Step 5: Commit**

```powershell
git add backend/services/Business/IndustrialTelemetry
git commit -m "feat(iiot): persist connector tag manifests"
```

### Task 7: Internal manifest ingestion and authoritative coverage query

**Files:**
- Modify: `IndustrialTelemetryCommands.cs`
- Modify: `IndustrialTelemetryQueries.cs`
- Modify: `IndustrialTelemetryEndpoints.cs`
- Modify: `IndustrialTelemetryEndpointContractTests.cs`
- Create: `ConnectorTagManifestTests.cs` in IndustrialTelemetry Web tests
- Modify: `docs/architecture/facade-coverage-matrix.json`

**Interfaces:**
- Produces: `POST /api/business/v1/iiot/connector-tag-manifests`, operation `reportBusinessIiotConnectorTagManifest`, classification `internal`.
- Produces: `GET /api/business/v1/iiot/connectors/{collectionConnectorId}/tag-coverage`, operation `getBusinessIiotConnectorTagCoverage`, classification `exposed`.

- [ ] **Step 1: Write failing command/query/contract tests**

Use these request/result shapes:

```csharp
public sealed record ReportConnectorTagManifestCommand(
    string OrganizationId,
    string EnvironmentId,
    string CollectionConnectorId,
    string SourceSystem,
    string ManifestRevision,
    DateTimeOffset ManifestObservedAtUtc,
    IReadOnlyCollection<ReportConnectorTagManifestEntry> Entries)
    : ICommand<ReportConnectorTagManifestResult>;

public sealed record ConnectorTagCoverageItem(
    string DeviceAssetId,
    string TagKey,
    bool Enabled,
    string ActivationStatus,
    DateTimeOffset ActivationObservedAtUtc,
    string? ActivationErrorCode,
    string? ActivationErrorMessage,
    DateTimeOffset? FirstSampleAtUtc,
    DateTimeOffset? LastSampleAtUtc);
```

Tests must prove never-sampled bindings remain, two connectors on the same device/tag do not share samples, retired bindings disappear, unavailable differs from a valid empty manifest, future clock skew is rejected, duplicate device/tag entries fail validation, anonymous access is rejected, and both contracts are registered.

- [ ] **Step 2: Run focused Web tests and verify RED**

Run: `dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --filter "FullyQualifiedName~ConnectorTagManifestTests|FullyQualifiedName~IndustrialTelemetryEndpointContractTests"`

- [ ] **Step 3: Implement transactional ingestion and summary-only query**

Recompute the canonical hash server-side using ordinal sorting by device/tag and deterministic field serialization. Accept later observation with either the same or different revision; return the accepted server observation for stale/conflict. Apply activation by its independent observation. Start coverage from current bindings and aggregate `MIN(summary.BucketStart)`/`MAX(summary.BucketEnd)` using the full organization/environment/connector/device/tag key. Never query raw samples or device-control bindings.

- [ ] **Step 4: Register facade classifications and verify GREEN**

Add one `internal` row with Connector Host callback rationale and one `exposed` row naming `getBusinessConsoleTelemetryConnectorTagCoverage`. Run the focused tests and the facade gate:

`dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj`

- [ ] **Step 5: Commit**

```powershell
git add backend/services/Business/IndustrialTelemetry docs/architecture/facade-coverage-matrix.json
git commit -m "feat(iiot): expose connector tag coverage"
```

### Task 8: Host manifest hashing, retry, activation, and sample identity

**Files:**
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/ConnectorManifestHasher.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/ConnectorManifestReporter.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/HttpConnectorTagManifestClient.cs`
- Modify: `ConnectorAbstractions.cs`, `ConnectorTarget`, `Worker.cs`, and Host DI/configuration
- Modify: all three adapter contracts/connectors and sample request clients
- Create: `ConnectorManifestReporterTests.cs`
- Modify: all three adapter test suites
- Create: matching IndustrialTelemetry hash-vector tests using the same fixed input/output values without a project reference

**Interfaces:**
- Produces: `ConnectorTagManifestSnapshot` and entries from configured mappings, including failed/disabled entries.
- Produces: bounded retry state cleared only by acknowledgement and coalesced only by newer observation.

- [ ] **Step 1: Write failing deterministic hash and retry tests**

Fix one canonical vector in both solutions: differently ordered input entries yield the same lowercase SHA-256; changing enabled/address changes the hash; activation changes do not. Assert same observation retries unchanged, stale/conflict advances to `max(now, accepted+1 tick)`, later observation coalesces pending state, successful acknowledgement clears it, and exponential delays cap at 30 seconds.

- [ ] **Step 2: Run Application and adapter tests and verify RED**

Run: `dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/Nerv.IIP.ConnectorHost.Application.Tests.csproj --filter "FullyQualifiedName~ConnectorManifest"`

- [ ] **Step 3: Implement manifest reporting and canonical sample identity**

Expose every configured mapping regardless of activation/sample result. Use protocol address metadata but no secrets or exception stacks. Maintain `lastAttemptedObservation` per connector in the process and advance by one tick when necessary; use service acknowledgement after restart/clock lag. Send `CollectionConnectorId` with every new sample while preserving `SourceConnector`.

- [ ] **Step 4: Run the full Connector Host solution and verify GREEN**

Run: `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln`

- [ ] **Step 5: Commit**

```powershell
git add connector-hosts
git commit -m "feat(connector-host): report configured tag manifests"
```

### Task 9: BusinessGateway coverage facade, OpenAPI, and generated client

**Files:**
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleConnectorTagCoverageModels.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Telemetry/BusinessConsoleConnectorTagCoverageEndpoint.cs`
- Modify: `BusinessServiceClients.cs`
- Create: `BusinessGatewayConnectorTagCoverageTests.cs`
- Modify: `BusinessGatewayProxyTests.cs` recording client
- Modify: `BusinessGatewayOpenApiTests.cs`
- Regenerate: Gateway OpenAPI and generated business-console client
- Modify: `frontend/packages/api-client/src/business-console.ts`
- Modify: `frontend/packages/api-client/src/generated-contract.test.ts`

**Interfaces:**
- Produces: `GET /api/business-console/v1/telemetry/connectors/{connectorId}/tag-coverage` with operation `getBusinessConsoleTelemetryConnectorTagCoverage` and permission `IiotTelemetryRead`.

- [ ] **Step 1: Write failing proxy and OpenAPI tests**

Assert route connector identity and organization/environment are forwarded, bearer token is an internal token, response preserves unavailable/empty and nullable sample timestamps, authorization uses resource type `connector`, and the operation/schema is in OpenAPI.

- [ ] **Step 2: Run Gateway tests and verify RED**

Run: `dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter "FullyQualifiedName~BusinessGatewayConnectorTagCoverageTests|FullyQualifiedName~BusinessGatewayOpenApiTests"`

- [ ] **Step 3: Implement facade and generate contracts**

Add `IBusinessIndustrialTelemetryClient.GetConnectorTagCoverageAsync(string internalBearerToken, BusinessConsoleConnectorTagCoverageRequest request, CancellationToken cancellationToken)` and update every fake implementation. Export and generate:

```powershell
scripts/export-gateway-openapi.ps1
pnpm -C frontend generate:api
```

- [ ] **Step 4: Run contract tests and commit**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj
pnpm -C frontend --filter @nerv-iip/api-client test
git add backend/gateway/BusinessGateway frontend/packages/api-client docs/architecture/facade-coverage-matrix.json
git commit -m "feat(gateway): expose connector tag coverage"
```

### Task 10: Lazy configured-tag coverage panel

**Files:**
- Create: `frontend/apps/business-console/src/components/equipment/ConnectorTagCoveragePanel.vue`
- Create: `frontend/apps/business-console/src/components/equipment/ConnectorTagCoveragePanel.test.ts`
- Modify: `ConnectorHealthCard.vue`
- Modify: `useBusinessTelemetry.ts`
- Modify: `useBusinessTelemetry.test.ts`
- Modify: `connectorsPage.test.ts`
- Read and obey: `frontend/apps/business-console/AGENTS.md`

**Interfaces:**
- Produces: `useBusinessTelemetryConnectorCoverage(collectionConnectorId: Ref<string>)` using the generated query options.
- Produces: panel mounted only while the card is expanded.

- [ ] **Step 1: Write failing composable and component tests**

Assert collapsed cards issue zero coverage calls; expanding one card requests only its connector; unavailable, valid empty, disabled, activation error, active never-sampled, and sampled states render distinct operator copy; errors offer a local retry; collapse/reopen preserves canonical ID; and engineering details such as revision/hash are not displayed.

- [ ] **Step 2: Run focused Vitest and verify RED**

Run: `pnpm -C frontend --filter @nerv-iip/business-console test -- ConnectorTagCoveragePanel.test.ts useBusinessTelemetry.test.ts connectorsPage.test.ts`

- [ ] **Step 3: Implement with NvUI stable imports**

Mount the panel with `v-if="expanded"`, use only bare `@nerv-iip/ui` imports and existing `Nv*` components, and show authoritative most recent sample time without quality/freshness inference.

- [ ] **Step 4: Run focused tests, typecheck, touched-file format, and commit**

```powershell
pnpm -C frontend --filter @nerv-iip/business-console test -- ConnectorTagCoveragePanel.test.ts useBusinessTelemetry.test.ts connectorsPage.test.ts
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend exec vp fmt --check frontend/apps/business-console/src/components/equipment/ConnectorTagCoveragePanel.vue frontend/apps/business-console/src/components/equipment/ConnectorHealthCard.vue frontend/apps/business-console/src/composables/useBusinessTelemetry.ts
git add frontend/apps/business-console
git commit -m "feat(business-console): expand connector tag coverage"
```

## Cross-layer acceptance and documentation

### Task 11: Real Modbus disconnect acceptance and Aspire wiring

**Files:**
- Create: `scripts/verify-connector-health-disconnect.ps1`
- Create: `scripts/support/modbus-tcp-simulator.ps1`
- Create: `scripts/tests/connector-health-disconnect-verify-script.Tests.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`

**Interfaces:**
- Produces: acceptance-only AppHost configuration forwarding IndustrialTelemetry endpoint/internal token and explicit Modbus mappings to Connector Host.
- Produces: `artifacts/script-logs/connector-health-disconnect/<timestamp>/evidence.json` with monotonic elapsed and phase timestamps.

- [ ] **Step 1: Write failing script governance/runtime tests**

Assert the verify script dot-sources `scripts/lib/ScriptAutomation.ps1`, uses `Start-ManagedBackgroundProcess`, fixes its deadline at 10 seconds, uses `Stopwatch`, cleans up session/simulator in `finally`, and contains no direct `dotnet`, `docker`, `pnpm`, `pwsh`, or `Start-Process`. Assert AppHost injects the IndustrialTelemetry URL and session internal token and enables simulator mappings only under `ConnectorHealthAcceptance:Enabled=true`.

- [ ] **Step 2: Run the script tests and verify RED**

Run: `pwsh scripts/tests/connector-health-disconnect-verify-script.Tests.ps1`

- [ ] **Step 3: Implement the simulator and acceptance flow**

The simulator must bind loopback, publish its selected port as ready JSON, serve one valid mapping and one configured mapping that remains never-sampled, close its accepted socket and listener on stop, and restart on the same port. The verify script starts a fullstack session, waits for alive plus current manifest, stops the simulator, polls BusinessGateway for explicit lost/offline/field-connection with a fresh advancing Host heartbeat before 10 seconds, restarts the simulator, verifies a new alive interval, and verifies the never-sampled binding. Record `disconnectStartUtc`, `connectionObservedAtUtc`, `gatewayObservedAtUtc`, `elapsedMilliseconds`, `lastHeartbeatAtUtc`, and recovery timestamps.

- [ ] **Step 4: Run governance, AppHost build, and real acceptance**

```powershell
pwsh scripts/tests/connector-health-disconnect-verify-script.Tests.ps1
pwsh scripts/check-script-governance.ps1
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
pwsh scripts/verify-connector-health-disconnect.ps1 -Runs 3
```

Expected: three passes, each disconnect observed in `<10000ms`; if Docker is unavailable, retain deterministic gates and record the environment limitation in the PR rather than weakening the deadline.

- [ ] **Step 5: Commit**

```powershell
git add scripts infra/aspire/Nerv.IIP.AppHost
git commit -m "test(connectors): verify end-to-end disconnect timing"
```

### Task 12: Governed docs, complete verification, and PR readiness

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/connector-platform-protocol-v1.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/api-contract-and-codegen.md`
- Modify: `docs/architecture/facade-coverage-matrix.md`
- Modify: `docs/architecture/frontend-navigation-map.md`
- Modify: `docs/architecture/script-automation-governance.md`
- Modify: `docs/architecture/local-dev-troubleshooting.md`
- Modify: `docs/architecture/deployment-baseline.md`
- Modify: `frontend/apps/docs/docs/roles/equipment-engineer.md`
- Modify: this plan by checking completed boxes only after evidence exists.

**Interfaces:**
- Produces: documented minimum Host compatibility, four-axis semantics, schema/catalog rows, facade declaration, operator workflow, and acceptance command.

- [ ] **Step 1: Update documentation from delivered code facts**

Document null compatibility, no historical identity backfill, canonical ID configuration, governed timing values, manifest unavailable behavior, sample-presence-only coverage, acceptance-only simulator topology, exact facade operations, and the real verification evidence location. State product docs impact as yes.

- [ ] **Step 2: Run the complete required gates**

```powershell
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
dotnet test backend/Nerv.IIP.sln
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
scripts/export-gateway-openapi.ps1
pnpm -C frontend generate:api
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
pwsh scripts/check-script-governance.ps1
```

Expected: all gates pass with no generated drift. Run `./nerv.ps1 fullstack run -Scenario smoke` as a regression when Docker is available.

- [ ] **Step 3: Perform final repository and PR audit**

Verify `git diff origin/main...HEAD`, ensure no secrets/artifacts or `skills-lock.json` are staged, verify both endpoint registrations and facade rows, re-fetch all PR review threads and require zero unresolved, and update PR body with:

```text
Closes #947
Closes #951
Refs #796

Facade declarations:
- reportBusinessIiotConnectorTagManifest: internal (Connector Host callback)
- getBusinessIiotConnectorTagCoverage: exposed via getBusinessConsoleTelemetryConnectorTagCoverage

文档：有影响，已更新设备工程师采集健康说明。
```

- [ ] **Step 4: Commit documentation and push**

```powershell
git add docs frontend/apps/docs docs/superpowers/plans/2026-07-17-connector-observability-and-tag-manifest.md
git commit -m "docs: close connector observability delivery"
git push origin codex/issue-947-951-connector-observability
```

- [ ] **Step 5: Mark PR #952 ready for review**

Only after all available gates pass and any environment-limited gate is explicitly documented, convert the draft PR to ready. Do not merge without explicit user authorization.
