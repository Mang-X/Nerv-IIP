# Business Wave 2.5 Equipment Reliability Closure

## Context

IndustrialTelemetry #129 and Maintenance #130 were originally listed as later business slices. After Wave 2, they were completed as an Equipment Reliability side wave and registered in the platform before ERP. This record prevents future plans from treating them as zero-code Wave 3 work.

## Current Code Facts

| Service | Current Fact | Verification |
| --- | --- | --- |
| IndustrialTelemetry | Domain/Infrastructure/Web service exists under `backend/services/Business/IndustrialTelemetry`; it owns telemetry tags, device state snapshots, alarm events and telemetry summaries. Public alarm/state event contracts live under `backend/common/Contracts/Nerv.IIP.Contracts.IndustrialTelemetry`. | `scripts/verify-business-industrial-telemetry-mvp.ps1` |
| Maintenance | Domain/Infrastructure/Web service exists under `backend/services/Business/Maintenance`; it owns maintenance work orders, plans, inspections, downtime reasons and spare-part lines. It consumes `industrialTelemetry.AlarmRaised` through public contracts and publishes maintenance asset availability contracts. | `scripts/verify-business-maintenance-mvp.ps1` |
| Equipment Reliability aggregate | Both services are in `backend/Nerv.IIP.sln`, Aspire AppHost and readiness docs. Local ports are 5116 and 5117. | `scripts/verify-business-equipment-reliability.ps1` |

## Boundary Decisions

1. IndustrialTelemetry does not own PLC/DCS/SCADA control commands or credentials.
2. Maintenance does not own device master data, Inventory balances or MES work order state.
3. MES consumes Maintenance asset availability events through `Nerv.IIP.Contracts.Maintenance`, not Maintenance internals.
4. Alarm-triggered work order creation must remain idempotent by alarm/source reference in later hardening.

## Remaining Follow-Ups

These are not blockers for ERP Wave 3, but they matter before or during Full-chain acceptance:

1. Confirm repeated alarm handling cannot open duplicate maintenance work orders.
2. Decide whether Maintenance work-order opened/completed events should be promoted to public contracts.
3. Add acceptance coverage for alarm -> maintenance work order -> asset unavailable/restored -> MES scheduling constraint.
4. Keep high-frequency telemetry retention and raw time-series storage outside the MVP profile until a dedicated telemetry storage spec exists.

## Issue Mapping

| Issue | State |
| --- | --- |
| #129 IndustrialTelemetry MVP | Closed; implemented. |
| #130 Maintenance MVP | Closed; implemented. |
| #77 Full-chain acceptance | Must verify the equipment-to-maintenance-to-capacity chain. |
