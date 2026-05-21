# ADR 0013: Business Master Data Governance

- Status: Accepted
- Date: 2026-05-21

## Context

BusinessMasterData is the first business platform slice and is the base for ProductEngineering, DemandPlanning, Inventory, Quality, ERP, WMS, MES, IndustrialTelemetry and Maintenance. The initial slice correctly placed SKU, business partners, business organization attributes, work centers, calendars and device assets in Layer 0, but review found that this is only a minimum skeleton.

The same MasterData service must support discrete manufacturing and process manufacturing. Process manufacturing adds requirements such as UOM conversion, material potency or concentration, shelf-life, storage constraints, recipe/formula versions, process parameters, equipment capacity and compatibility, quality specifications and regulated release rules. If these decisions are postponed until ERP, MES, WMS or Quality implementation, the downstream domains will create parallel master data and the platform will lose a stable fact source.

## Decision

1. BusinessMasterData is the owner of common business identity and static reference facts that multiple business domains must share before creating transactions.
2. MasterData must distinguish four categories:
   - Master data: durable business identities and static attributes such as SKU, partner, plant, work center, device asset and unit of measure.
   - Reference data: controlled code lists such as material type, partner role, asset class, storage condition, hazard class, quality characteristic and process parameter definition.
   - Transactional data: orders, movements, production reports, inspection records, alarms, work orders and financial postings. These do not belong in MasterData.
   - External reference: IAM user, organization and environment IDs, File Storage file IDs, Connector Host/AppHub IDs and external system identifiers. MasterData can reference them but does not own their facts.
3. The current MasterData implementation plan must not proceed to API freeze or downstream dependency rollout until a MasterData realignment plan has clarified:
   - UOM and conversion ownership.
   - SKU/material industrial attributes and traceability policy.
   - partner identity, roles and sensitive commercial fields.
   - site/plant/area/line/work-center/device resource hierarchy.
   - device static capacity, compatibility and external references.
   - process manufacturing supplement and ProductEngineering recipe/formula boundary.
   - downstream resolve APIs and MasterData change IntegrationEvents.
4. Downstream services must not read the MasterData database directly and must not create parallel master facts. They consume MasterData through public APIs, reference snapshots and IntegrationEvents.
5. MasterData changes that can affect downstream decisions must be publishable as IntegrationEvents following ADR 0011. At minimum, SKU, UOM, partner, resource, work calendar and device asset changes must have stable event names and versioned payloads before downstream services cache or snapshot those facts.
6. MasterData must define lifecycle states beyond physical deletion. Disabling, archiving, replacing, merging and effective-date changes must preserve historical references and must not silently invalidate existing business documents.
7. Sensitive partner and personnel-adjacent fields must be explicitly classified. IAM remains the owner of user, role, permission and membership facts. ERP remains the owner of purchase/sales/finance transactions. Quality remains the owner of inspection standards and release decisions unless a field matrix explicitly marks a field as a MasterData reference definition.

## Rationale

1. SKU, UOM, partner, resource and device data are shared by most downstream domains. Treating them as local fields in each service would create inconsistent planning, inventory, quality and finance behavior.
2. Process manufacturing cannot be modeled safely by a discrete-only SKU plus MBOM/Routing interpretation. It needs material properties, recipe/formula boundaries and process parameter definitions to be decided before MES and ProductEngineering become hard to change.
3. Business services are intentionally separated by database schema. Public resolve APIs and events replace cross-schema foreign keys.
4. A small, governed MasterData service is preferable to a giant master data service. The field matrix decides what belongs in MasterData and what remains in ProductEngineering, Quality, Inventory, MES, ERP, Telemetry or Maintenance.

## Consequences

1. The MasterData slice gains a realignment step before completing API, permission seed and readiness documentation.
2. The ProductEngineering MVP must explicitly support Recipe/Formula and process parameters as a process-manufacturing extension of MBOM/Routing, while keeping versioned engineering facts out of MasterData.
3. Inventory keeps actual stock locations, batch/serial instances, stock balances and movements. MasterData can own SKU traceability policy and UOM rules that Inventory consumes.
4. Quality keeps inspection standards, plans, records, nonconformance and release decisions. MasterData can own reusable characteristic definitions and reference codes.
5. MES keeps batch production execution, actual consumption, actual yield, batch record, deviations, cleaning execution and genealogy. MasterData owns static resource and material facts.
6. Additional documentation and tests are required before downstream service implementation can rely on MasterData.

## Implementation Notes

1. The field-level source of truth is `docs/architecture/business-master-data-field-matrix.md`.
2. Process manufacturing additions are governed by `docs/architecture/business-master-data-process-manufacturing-supplement.md`.
3. The executable adjustment plan is `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`.
4. The original MasterData foundation plan remains valid as historical input, but Task 4 and Task 5 must be executed only after the realignment plan updates the domain model, events and API contracts.
