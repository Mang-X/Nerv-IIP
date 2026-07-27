# MAN-603 Simulated Device Connector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a configurable, deterministic, long-running simulated device adapter that exercises Connector Host registration, health, telemetry, and control-receipt paths for MAN-603/#1088.

**Architecture:** Add one Connector Host adapter project that owns three isolated logical connector runtimes and consumes only Connector Host abstractions plus public protocol contracts. Controlled time and identity-keyed deterministic streams drive telemetry; bounded outbox/receipt stores provide resilient delivery and idempotent command execution; the existing Worker and reporting loops provide scheduling and AppHub/Ops transport.

**Tech Stack:** .NET 10, Microsoft.Extensions.Hosting/Configuration, `TimeProvider`, xUnit, Connector Protocol v1 SDK, Ops SDK, Aspire AppHost.

## Global Constraints

- Work only in `/Users/mang/.codex/worktrees/1bdd/Nerv-IIP` on `codex/man-603-simulated-device-connector`.
- `connector-hosts/` remains a separate solution and must never reference backend/frontend implementation projects; only its existing public Contracts/SDK references are allowed.
- Do not modify the #1086 script simulator or run `dotnet test backend/Nerv.IIP.sln`.
- Use test-driven development: capture RED command/output before production implementation and GREEN command/output after it.
- Use exact instance and collection connector identities `CONN-OPCUA-01`, `CONN-MQTT-01`, and `CONN-MODBUS-01`.
- Simulation is opt-in, configuration-driven, deterministic under controlled `TimeProvider` and seed, and contains no committed credential or secret.
- Retries, polling, long-run behavior, and shutdown must honor cancellation; queues and caches are bounded.
- Existing Connector Protocol v1 and Ops public DTOs remain unchanged.
- The ready PR must include `Fixes #1088`; do not merge it.

---

### Task 1: First-class simulated device Connector Host adapter

**Files:**
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/IndustrialTelemetrySampleContracts.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.OpcUa/OpcUaContracts.cs`
- Delete: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.OpcUa/HttpIndustrialTelemetrySamplesClient.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/HttpIndustrialTelemetrySamplesClient.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/ConnectorOperationLoop.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Simulated/Nerv.IIP.ConnectorHost.Connectors.Simulated.csproj`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Simulated/SimulatedConnectorOptions.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Simulated/SimulatedScenarioEvaluator.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Simulated/SimulatedSampleOutbox.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Simulated/SimulatedCommandReceiptStore.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Simulated/SimulatedConnector.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Modbus/Nerv.IIP.ConnectorHost.Connectors.Modbus.csproj`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Mqtt/Nerv.IIP.ConnectorHost.Connectors.Mqtt.csproj`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.OpcUa/Nerv.IIP.ConnectorHost.Connectors.OpcUa.csproj`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Nerv.IIP.ConnectorHost.Host.csproj`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests.csproj`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/SimulatedScenarioEvaluatorTests.cs`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/SimulatedConnectorTests.cs`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/SimulatedDeliveryTests.cs`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/SimulatedCommandTests.cs`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/SimulatedLongRunningTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/OperationLoopTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests/OpcUaTelemetryCollectorTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests/OpcUaSimulatorIntegrationTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Modbus.Tests/ModbusTelemetryCollectorTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Modbus.Tests/Nerv.IIP.ConnectorHost.Connectors.Modbus.Tests.csproj`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Mqtt.Tests/MqttTelemetryCollectorTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Mqtt.Tests/MqttNetSubscriptionClientIntegrationTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Mqtt.Tests/Nerv.IIP.ConnectorHost.Connectors.Mqtt.Tests.csproj`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/SimulatedConnectorHostProcessTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/WorkerTests.cs`
- Modify: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/Nerv.IIP.ConnectorHost.Host.Tests.csproj`
- Modify: `connector-hosts/Nerv.IIP.ConnectorHost.sln`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `docs/architecture/connector-platform-protocol-v1.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `infra/aspire/README.md`

**Interfaces:**
- Consumes: `IConnector`, `IIndustrialTelemetryCollectionConnector`, `IConnectorConnectionMonitor`, `IConnectorOperationExecutor`, `IIndustrialTelemetrySamplesClient`, `IConnectorReportSignal`, `IConnectorManifestSignal`, `OperationTaskDispatchItem`, and `TimeProvider`.
- Produces: `SimulatedConnectorOptions`, `SimulatedScenarioEvaluator`, `SimulatedSampleOutbox`, `SimulatedCommandReceiptStore`, and `SimulatedConnector`, with the connector registered under all four existing Connector Host interfaces when `Simulated:Enabled=true`.

- [ ] **Step 1: Write configuration, scenario, and isolation tests**

  Add tests that bind a compact three-connector configuration, expand it to exactly 46 distinct devices and 96 distinct tags, and reject duplicate connector/device/tag identities, missing phase durations, non-positive capacities, invalid ranges, and secrets/credential fields. With a controlled clock, assert exact `normal → degrading → alarm → recovered → normal` boundaries and literal deterministic values for a fixed seed. Reorder devices and update one device/tag override; assert every other device produces the same values and source sequences.

- [ ] **Step 2: Run focused scenario tests and capture RED**

  Run:

  ```bash
  dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests.csproj --filter "FullyQualifiedName~SimulatedScenarioEvaluatorTests|FullyQualifiedName~SimulatedConnectorTests"
  ```

  Expected: build/test failure because the simulated project and types do not exist. Record the command and relevant failure in the task report before adding production code.

- [ ] **Step 3: Add the transport-neutral samples seam and deterministic model**

  Move `RecordIndustrialTelemetrySampleRequest` and `IIndustrialTelemetrySamplesClient` to the abstractions project without changing their serialized shape. Move `HttpIndustrialTelemetrySamplesClient` to Application and remove now-unneeded cross-adapter project references. Implement options validation/expansion and the pure evaluator described by
  `docs/superpowers/specs/2026-07-26-man-603-simulated-device-connector-design.md`. Stable point identity must be derived from configured seed plus connector/device/tag/cycle, never collection order or a shared `Random`.

- [ ] **Step 4: Make scenario and isolation tests GREEN**

  Run the Step 2 command. Expected: all selected tests pass with no warning. Commit this focused slice as:

  ```bash
  git commit -m "feat(connector-host): add deterministic simulated profiles"
  ```

- [ ] **Step 5: Write RED delivery, health, and cancellation tests**

  Add tests for three exact discovered instance keys, independent `CounterEpoch`/counts/last-sample facts, replace-style manifests, and `alive` connection facts. Use a fake samples client that fails selected attempts: assert exponential retry due times through controlled `TimeProvider`, identical payload/source sequence on retry, correct received/error/dropped accounting, bounded outbox eviction, and immediate cancellation without a follow-up attempt.

- [ ] **Step 6: Implement bounded collection delivery and health**

  Implement one runtime per configured logical connector and one state/outbox per device/tag identity. Generate at most one new point per configured cycle, retain failed requests unchanged, bound pending samples by `MaxPendingSamples`, and expose each connector's own CollectionHealth and manifest through discovery. Signal existing report/manifest seams when health or activation facts change.

- [ ] **Step 7: Make delivery tests GREEN**

  Run:

  ```bash
  dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests.csproj --filter "FullyQualifiedName~SimulatedConnectorTests|FullyQualifiedName~SimulatedDeliveryTests"
  ```

  Expected: all selected tests pass, no warning, and no real-time sleep. Commit:

  ```bash
  git commit -m "feat(connector-host): report simulated collection health"
  ```

- [ ] **Step 8: Write RED command idempotency and receipt tests**

  Construct `device.control.command` dispatch items for `write-tag`, `parameter-set`, and `start-stop`. Assert a success changes only the addressed device, an exact duplicate `OperationTaskId` returns the same immutable output without reapplying state, and unknown device/tag/command plus range violations return `BadNotFound`, `BadNotSupported`, or `BadOutOfRange`. Every path must include connector/protocol/command/task/correlation fields and device receipt code/message. Fill past cache capacity and assert deterministic bounded eviction.

- [ ] **Step 9: Implement command execution and controlled result timestamps**

  Route by full organization/environment/host/instance/device identity. Cache terminal executions by
  `OperationTaskId`; preserve indexed receipt output for multi-value parameter writes. Inject `TimeProvider` into
  `ConnectorOperationLoop` and use it for result start/finish/context timestamps. Do not log or echo secret
  configuration values.

- [ ] **Step 10: Make command tests GREEN**

  Run:

  ```bash
  dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests.csproj --filter "FullyQualifiedName~SimulatedCommandTests"
  dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/Nerv.IIP.ConnectorHost.Application.Tests.csproj --filter "FullyQualifiedName~OperationLoopTests"
  ```

  Expected: all selected tests pass with controlled timestamps. Commit:

  ```bash
  git commit -m "feat(connector-host): execute auditable simulated controls"
  ```

- [ ] **Step 11: Write RED Host wiring, long-run, and real-process tests**

  Add a controlled-time test that advances at least 1,000 collection cycles and multiple full phase periods, proving deterministic repetition, device isolation, bounded pending samples/receipt cache, continuous three-target health, and clean cancellation. Add a real-process test that launches the built Host executable against loopback AppHub/Ops/IndustrialTelemetry fakes and waits for exactly the three canonical registrations, at least two heartbeats per instance, CollectionHealth state snapshots, telemetry from each source system, a claimed control task, and a correlated `Good` result. The test must cancel the process, wait within a fixed timeout, and kill only that exact child if cleanup fails.

- [ ] **Step 12: Wire opt-in Host and leader-demo Aspire configuration**

  Register the simulated adapter under all four existing Host interface collections only when
  `Simulated:Enabled=true`. Add the complete compact 46-device/96-tag Development configuration with 45-minute
  defaults and the three staggered alarm overrides. In AppHost, enable it only when `LeaderDemo:World:Enabled`,
  set collection cadence to two seconds and operation polling to one second, inject the existing internal-service
  token and IndustrialTelemetry endpoint, and reference/wait for IndustrialTelemetry. Do not change #1086 scripts.

- [ ] **Step 13: Make runtime tests GREEN and freeze focused implementation commits**

  Run:

  ```bash
  dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests.csproj
  dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/Nerv.IIP.ConnectorHost.Host.Tests.csproj --filter "FullyQualifiedName~SimulatedConnectorHostProcessTests"
  ```

  Expected: all simulated tests, the 1,000-cycle test, and the real-process test pass with bounded exit and zero warning. Commit:

  ```bash
  git commit -m "feat(aspire): enable simulated connectors for leader demo"
  ```

- [ ] **Step 14: Update architecture and operator documentation**

  Document opt-in configuration, exact identities, four-axis health semantics, profile/seed determinism, control
  receipt/idempotency behavior, real-process evidence, resource bounds, and safe shutdown. Mark MAN-603/#1088
  delivered in implementation readiness. State product docs are unaffected and public contracts/facade/OpenAPI are
  unchanged.

- [ ] **Step 15: Run fresh full verification**

  From a clean committed `HEAD`, run:

  ```bash
  dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
  dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests/Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests.csproj --filter "FullyQualifiedName~SimulatedLongRunningTests"
  dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Host.Tests/Nerv.IIP.ConnectorHost.Host.Tests.csproj --filter "FullyQualifiedName~SimulatedConnectorHostProcessTests"
  dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
  git diff --check origin/main...HEAD
  ```

  Expected: all tests/builds pass with zero new warning, process cleanup succeeds, and the diff check is empty.
  If a governed script was touched, also run `pwsh scripts/check-script-governance.ps1`; otherwise record
  `not applicable`.

- [ ] **Step 16: Commit documentation and report**

  Commit remaining documentation/test-evidence notes as:

  ```bash
  git commit -m "docs(connector-host): document simulated device evidence"
  ```

  Write the full task report with RED/GREEN excerpts, all commit SHAs, verification totals/timings, process evidence,
  files changed, and self-review findings. Do not push, create a PR, update Linear, or merge; the controller owns
  independent review and publication.
