# Business IIoT Runtime Facts APS/MES Design

## Goal

Implement issue #207 by turning device IIoT, IndustrialTelemetry and Maintenance facts into a shared runtime availability contract that APS lite and MES readiness can consume consistently.

The first slice must prove that a device alarm, downtime or maintenance window changes Scheduling availability, MES release/dispatch/start readiness and the Business Console equipment view for the shock absorber manufacturing scenario. It does not implement historian storage, PLC/DCS control or solver-grade automatic rescheduling.

## Current State

`BusinessIndustrialTelemetry` already exists under `backend/services/Business/IndustrialTelemetry` with the `industrial_telemetry` schema. It currently supports telemetry tag mapping, telemetry summary recording, device state snapshots, alarm raise/clear, device timeline query and ADR 0011 events:

1. `industrialTelemetry.DeviceStateChanged`
2. `industrialTelemetry.AlarmRaised`
3. `industrialTelemetry.AlarmCleared`
4. `industrialTelemetry.TelemetryTagCreated`
5. `industrialTelemetry.TelemetrySampleRecorded`

`BusinessMaintenance` already exists under `backend/services/Business/Maintenance` with the `maintenance` schema. It supports maintenance work orders, plans, inspections and events:

1. `maintenance.AssetUnavailable`
2. `maintenance.AssetRestored`

`BusinessScheduling` and `Nerv.IIP.Contracts.Scheduling` already exist. `SchedulingProblemContract` includes `SchedulingUnavailabilityWindowContract`, and `ScheduleConflictReasonCodeContract` already has `Equipment`.

MES currently has work-center unavailability facts and readiness surfaces, but #207 must make equipment reason codes and time-window availability come from the same normalized facts that Scheduling consumes.

## Boundary

IndustrialTelemetry owns:

1. External source/tag mapping, source sequence and sample idempotency.
2. Device state snapshots from controlled upstream facts.
3. Alarm lifecycle: active alarm, cleared alarm and duplicate/late alarm handling.
4. Telemetry summaries and OEE input summaries.
5. Runtime availability projection derived from telemetry-side state and alarms.

Maintenance owns:

1. Maintenance work orders, preventive plans, inspections and downtime reasons.
2. Maintenance, inspection, planned service and manual downtime windows.
3. Asset unavailable/restored events.
4. Spare-part demand references for maintenance work.

BusinessScheduling owns:

1. Schedule problems, plans, assignments, resource load and conflicts.
2. Translating normalized equipment unavailability into `SchedulingUnavailabilityWindowContract`.
3. Reporting equipment conflicts as `ScheduleConflictReasonCodeContract.Equipment` with stable detail messages.

MES owns:

1. Work orders, operation tasks, dispatch, start/pause/resume/complete and reporting.
2. Release/dispatch/start readiness decisions that consume the same equipment availability reason catalog as Scheduling.
3. MES downtime facts that are produced by MES execution or consumed from Maintenance, without becoming the IIoT fact owner.

BusinessGateway and Business Console own:

1. Page-level aggregation, authorization, generated API client and Chinese operator-facing copy.
2. No persistence of equipment runtime facts.
3. No alarm merge, availability calculation or scheduling logic.

## Approach

Recommended approach: query-first normalized availability with event-assisted projections.

IndustrialTelemetry and Maintenance expose time-window availability query surfaces. Scheduling and MES call those surfaces or consume service-local projections built from ADR 0011 events. The first implementation can use explicit HTTP query adapters for deterministic tests, while retaining events for asynchronous projections and future replay. This keeps ownership clear and avoids stale page-only logic.

Alternative 1: event-only projection into Scheduling and MES.

This reduces synchronous calls during scheduling, but it makes the first slice harder to verify because every consumer must build and repair its own projection. It also risks MES and Scheduling drifting if replay or DLQ behavior differs.

Alternative 2: create a new central EquipmentAvailability service.

This creates a clean facade, but it introduces another service before the existing IndustrialTelemetry and Maintenance contracts are mature. For P0 it is unnecessary; the semantic contract can exist without a new owner service.

The recommended approach is accepted for the #207 spec.

## Runtime Availability Contract

`EquipmentRuntimeAvailability` is the shared semantic contract for #207. It may be implemented as service-local DTOs first, but the fields must stay stable enough to move into `backend/common/Contracts` if more than one compiled consumer needs the same shape.

| Field | Meaning |
| --- | --- |
| `contractVersion` | Initial value `1`. |
| `organizationId` / `environmentId` | IAM context used by all services. |
| `queryWindowStartUtc` / `queryWindowEndUtc` | Time window requested by APS, MES or Console. |
| `deviceAssetId` | MasterData device asset public ID. |
| `workCenterId` | Optional work-center scope for Scheduling/MES. |
| `availabilityStatus` | `available`, `unavailable` or `unknown`. |
| `reasonCode` | Stable equipment reason code when status is not available. |
| `severity` | `info`, `warning`, `blocked` or `critical`. |
| `startUtc` / `endUtc` | Occupied or affected time window; open-ended facts use null `endUtc` only at source, then are clipped by query window for Scheduling input. |
| `sourceType` | `device-state`, `alarm`, `downtime`, `maintenance-window`, `inspection`, `stale-source` or `manual-block`. |
| `sourceReferenceId` | Public source fact ID, not database row internals. |
| `messageKey` | Stable UI copy key; frontend copy remains Chinese and business-facing. |
| `substituteDeviceAssetIds` | Optional alternatives from static capability/resource facts. |

## Reason Code Catalog

P0 reason codes:

| Code | Source | Scheduling mapping | MES readiness meaning |
| --- | --- | --- | --- |
| `equipment.activeAlarm` | IndustrialTelemetry alarm lifecycle | `Equipment` conflict and unavailability window | Device has an active alarm that blocks release, dispatch or start. |
| `equipment.stateUnavailable` | IndustrialTelemetry state snapshot | `Equipment` conflict and unavailability window | Current device state is stopped, faulted, offline or otherwise not runnable. |
| `equipment.downtime` | MES or Maintenance downtime fact | `Equipment` conflict and unavailability window | Device/work center has recorded downtime in the requested window. |
| `equipment.maintenanceWindow` | Maintenance plan/work order | `Equipment` conflict and unavailability window | Planned or active maintenance occupies the device. |
| `equipment.inspectionRequired` | Maintenance inspection/plan | `Equipment` conflict; MES readiness warning or block by policy | Required inspection or check is missing or failed. |
| `equipment.sourceStale` | IndustrialTelemetry source heartbeat/sequence policy | `Equipment` conflict when freshness is mandatory | Runtime state is too old to trust. |
| `equipment.tagMappingMissing` | IndustrialTelemetry tag mapping | MES/Scheduling input error or readiness block | Device lacks required tag/source mapping for the requested capability. |
| `equipment.noEligibleSubstitute` | MasterData/Scheduling capability resolution | `NoEligibleResource` or `Equipment` detail | No equivalent available device can replace the blocked one. |

New reason codes require updates in contracts/tests, Scheduling mapping, MES readiness, BusinessGateway facade tests and frontend Chinese copy. Services must not introduce ad hoc strings that only one consumer understands.

## Ingestion Rules

1. External data sources are OPC UA, MQTT, SCADA/PLC/DCS adapters or manual imports through Connector Host or a controlled service API. They are fact sources, not control channels.
2. Telemetry sample and state idempotency is scoped by `organizationId + environmentId + sourceSystem/sourceConnector + deviceAssetId + sourceSequence`.
3. Alarm idempotency is scoped by `organizationId + environmentId + deviceAssetId + alarmCode + externalAlarmId`.
4. Duplicate payloads with the same idempotency key return the existing fact.
5. Same idempotency key with a different payload is a conflict and must not silently overwrite the original fact.
6. State snapshots are ordered by source sequence when available, then by `occurredAtUtc`; a late older state can be kept in history but must not replace the current device state.
7. Alarm clear closes the matching active alarm. Clear for an unknown alarm returns a clear diagnostic or creates a pending-clear marker only if the implementation can test it deterministically.
8. Open-ended active alarms and downtime are clipped to the query window when projected into Scheduling availability.
9. P0 stores state snapshots, active/cleared alarms, summaries and availability projections; high-frequency raw historian samples remain out of scope.

## API Surface

IndustrialTelemetry service additions:

| API | Purpose | Permission |
| --- | --- | --- |
| `GET /api/business/v1/iiot/devices/{deviceAssetId}/runtime-availability` | Return normalized availability for one device and time window. | `business.iiot.telemetry.read` |
| `GET /api/business/v1/iiot/runtime-availability` | Return normalized availability for multiple devices or work centers in one window. | `business.iiot.telemetry.read` |
| `GET /api/business/v1/iiot/devices/{deviceAssetId}/current-state` | Return current state, active alarms and source freshness summary. | `business.iiot.telemetry.read` |

Maintenance service additions:

| API | Purpose | Permission |
| --- | --- | --- |
| `GET /api/business/v1/maintenance/assets/{deviceAssetId}/availability-windows` | Return maintenance, inspection and downtime occupancy for a device and time window. | `business.maintenance.work-orders.read` or `business.maintenance.plans.read` |
| `GET /api/business/v1/maintenance/availability-windows` | Batch query maintenance windows by device or work center. | `business.maintenance.work-orders.read` or `business.maintenance.plans.read` |

Scheduling integration:

1. Convert runtime availability entries into `SchedulingUnavailabilityWindowContract`.
2. Preserve `reasonCode` and source references in conflict messages or detail fields.
3. Keep the pure scheduler independent of HTTP, EF and clocks. Availability adapters run before constructing `SchedulingProblemContract`.

MES integration:

1. Release, dispatch and start readiness use the shared reason catalog.
2. Backend commands decide allowed/blocked action state; frontend only renders the result.
3. MES downtime recording can create MES-owned downtime facts, but it must not overwrite telemetry alarm facts or maintenance windows.

BusinessGateway/Business Console additions:

| Facade | Purpose |
| --- | --- |
| `GET /api/business-console/v1/equipment/overview` | Status board for devices, active alarms and occupied windows. |
| `GET /api/business-console/v1/equipment/devices/{deviceAssetId}` | Runtime detail with tags, timeline, active alarms and linked work orders/schedules. |
| `GET /api/business-console/v1/equipment/availability` | Page query for APS/MES impact within a selected time window. |
| `GET /api/business-console/v1/equipment/alarms` | Operator-facing active and recent alarm list. |

## Shock Absorber Scenario

Use the same fixture family as #206:

1. Line: shock absorber assembly line.
2. Work centers: tube welding, rod assembly, oil filling/sealing, damping test/packing.
3. Devices: `DEV-WELD-01`, `DEV-ROD-01`, `DEV-OIL-01`, `DEV-TEST-01`.
4. Critical tags: run state, fault code, oil pressure, fill volume, seal temperature, damping test result.
5. Critical alarm: `OIL_PRESSURE_LOW` on `DEV-OIL-01`, severity `critical`, blocks oil/seal operations.
6. Maintenance window: `DEV-OIL-01` unavailable on 2026-06-01 10:00-12:00 UTC.
7. Downtime event: manual or maintenance-confirmed stop for `DEV-TEST-01`.

Expected evidence:

1. Active alarm on `DEV-OIL-01` produces `equipment.activeAlarm`.
2. Scheduling avoids or reports conflict for oil/seal operation windows affected by the active alarm or maintenance window.
3. MES release/dispatch/start readiness returns the same reason code and blocks the same affected operation.
4. Alarm clear or asset restored updates availability and removes the readiness block for future checks.
5. Console equipment page shows device state, active alarm, maintenance/downtime occupancy and linked work order/schedule impact with Chinese business copy.

## Tests

Acceptance requires:

1. IndustrialTelemetry command/query tests for duplicate source sequence, conflicting duplicate payload, late state handling, alarm raise, duplicate raise and clear.
2. IndustrialTelemetry contract tests for runtime availability DTO shape and ADR 0011 event envelope compliance.
3. Maintenance tests for planned maintenance window, active maintenance work order, inspection required and asset restored availability.
4. Scheduling adapter tests that transform runtime availability into `SchedulingUnavailabilityWindowContract` and produce equipment conflicts.
5. MES readiness tests that return the same reason code catalog for release, dispatch and start checks.
6. BusinessGateway authorization/proxy tests for equipment facade routes.
7. API client generation and Business Console typecheck/test/build after facade routes exist.
8. Browser smoke of the shock absorber equipment/IIoT pages after backend facts exist.

## Out of Scope

1. PLC/DCS/SCADA control commands or write-back.
2. Saving field credentials, PLC passwords, OPC UA certificates or MQTT broker secrets in domain entities.
3. High-frequency historian storage and raw sample replay.
4. OEE loss tree, detailed performance analytics and predictive maintenance models.
5. Solver-grade APS optimization, automatic rescheduling and schedule simulation.
6. Mobile/PDA equipment workflows.
