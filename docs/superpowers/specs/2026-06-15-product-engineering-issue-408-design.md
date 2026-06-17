# ProductEngineering Issue 408 Design

## Context

GitHub issue #408 identifies ProductEngineering gaps after the MVP: ECO release does not affect referenced versions, ProductionVersion commands fake MBOM/Routing status, BOM lines lack common manufacturing semantics, and Routing ignores StandardOperation timing/control data.

The service already owns EngineeringDocument, EngineeringItem, EBOM, MBOM, Routing, StandardOperation, EngineeringChange and ProductionVersion facts in the `product_engineering` schema. BusinessGateway is only a facade; the business rules belong in ProductEngineering.

## Scope

This change closes the smallest backend business loop inside ProductEngineering:

1. ProductionVersion commands load the referenced MBOM and Routing versions, verify they exist, are `Published`, match the requested SKU, and are effective for the requested window.
2. ECO release validates each affected version reference and marks ProductEngineering-owned affected released versions as archived/obsolete within the same command transaction.
3. EBOM and MBOM material lines capture substitute/alternate, phantom, reference designator, yield/scrap and backflush semantics as released snapshots.
4. Routing release requires enabled StandardOperation references and stores setup/run/teardown minutes plus control/reporting/quality/outsourcing flags as immutable routing operation snapshots.

## Non-Goals

1. Do not add cross-schema foreign keys or read other services' tables.
2. Do not implement full BusinessApproval state validation; this remains an external approval reference until the approval integration issue lands.
3. Do not create downstream MES/Scheduling consumers in this branch.
4. Do not replace existing OpenAPI snapshots or generated frontend client by hand.

## Data Model

No new aggregate roots are needed. Existing owned rows gain columns:

1. `engineering_bom_lines`: `is_phantom`, `alternate_group`, `alternate_priority`, `reference_designators`, `scrap_rate`, `yield_rate`, `backflush`.
2. `manufacturing_bom_material_lines`: `is_phantom`, `alternate_group`, `alternate_priority`, `substitute_sku_codes`, `reference_designators`, `yield_rate`, `backflush`.
3. `routing_operations`: `setup_minutes`, `run_minutes`, `teardown_minutes`, `control_key`, `requires_reporting`, `requires_quality_inspection`, `is_outsourced`.

Existing `standard_minutes` is kept as a compatibility snapshot and computed from setup + run + teardown for new releases.

## Behavior

Version references use the current service convention `Code:Revision` for EBOM, MBOM and Routing, and GUID string for ProductionVersion.

ECO release accepts affected version kinds `engineering-bom`, `manufacturing-bom`, `routing` and `production-version`. The command handler loads each referenced aggregate, rejects missing or cross-scope references, releases the ECO, then archives affected published/active versions with the ECO number as the reason.

ProductionVersion create/update still exposes only public fields. The handler resolves internal MBOM/Routing facts and passes real statuses to the aggregate.

## Verification

Focused tests cover:

1. Domain invariants for BOM line and routing operation snapshots.
2. Command handler rejection of missing/draft/wrong-SKU/wrong-effectivity MBOM or Routing references.
3. ECO release propagation to affected versions.
4. EF schema comments for new columns.
5. Existing ProductEngineering verify script.
