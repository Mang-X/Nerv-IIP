# Business Master Data Process Manufacturing Supplement

This supplement extends the BusinessMasterData baseline so the business platform can support process manufacturing such as chemical, food, pharmaceutical, metallurgy and similar batch or continuous-production scenarios. It does not replace the discrete manufacturing model. It adds boundary rules that prevent discrete-only assumptions from leaking into ProductEngineering, MES, Quality, Inventory and Planning.

## Scope

Process manufacturing support must cover these scenario families:

1. Chemical: concentration, density, purity, hazardous material handling, compatible equipment and process parameters.
2. Food: allergen, shelf life, storage condition, batch traceability, packaging and release rules.
3. Pharmaceutical: GMP recipe version, potency, batch record, equipment cleanliness, quality release and regulated change control.
4. Metallurgy: grade, furnace heat, co-product/by-product, yield and batch genealogy.

## MasterData Additions

| Capability | MasterData responsibility | Notes |
| --- | --- | --- |
| Material attributes | Store stable attributes such as material form, grade, storage condition, hazard class, allergen tags, regulatory tags, shelf-life policy, default quality-required flag | Actual inspection values stay in Quality or MES |
| Unit system | Own UOM, unit groups, conversion, precision and rounding | Formula-specific scaling remains ProductEngineering |
| Plant/resource hierarchy | Own Site/Plant/Area/Line/WorkCenter/DeviceAsset hierarchy | IAM organization/environment remains IAM |
| Equipment capability | Own static capacity, capacity UOM, material compatibility, cleanliness class, temperature/pressure design range and utility requirement references | Actual running values stay in Telemetry and MES |
| Reference definitions | Own cross-domain code sets such as material form, storage condition, hazard class, quality characteristic definition and process parameter definition | Domain-specific workflow states stay in each domain |
| Partner compliance | Own partner identity, partner roles and stable compliance tags or certificate references | Supplier audit workflow and release decisions stay in Quality/SRM |

## ProductEngineering Boundary

ProductEngineering must not treat process manufacturing as a simple MBOM variant. It owns versioned engineering facts:

| Object | ProductEngineering responsibility |
| --- | --- |
| Recipe / Formula | Versioned recipe identity, product/material output, batch basis, effective date, release status |
| Formula line | Input material, quantity or proportion, UOM, yield contribution, loss factor, alternative material, rework/reuse rule |
| Co-product / by-product | Expected output material, yield, costing relevance and traceability requirement |
| Process step / phase | Ordered stage, required resource capability, work center, expected duration and setup/cleaning dependency |
| Process parameter target | Temperature, pressure, flow, pH, speed or other target value and tolerance tied to a released recipe/routing version |
| Change control | ECO/ECN or equivalent release flow for recipe, formula, routing and parameter versions |

MasterData owns reusable definitions and static resource facts. ProductEngineering owns the versioned product-specific recipe, formula and routing content.

## Domain Boundaries

| Fact | Owner |
| --- | --- |
| SKU material identity and default attributes | BusinessMasterData |
| UOM and conversion | BusinessMasterData |
| Recipe/formula version and process parameters for a product | ProductEngineering |
| Actual batch, lot, heat, serial or date-code instance | Inventory |
| Stock balance, FEFO execution and inventory status | Inventory |
| Inspection standard, sampling rule, result, COA and release decision | Quality |
| Batch production order, actual input/output, deviation, cleaning execution and genealogy | MES |
| Runtime temperature, pressure, flow, alarm and state snapshot | IndustrialTelemetry |
| Maintenance order, inspection, downtime and asset restoration | Maintenance |

## Acceptance Scenarios

These scenarios must be represented before declaring process manufacturing support:

1. Chemical mixing: convert kg and L using configured UOM rules; apply density/concentration references; select compatible vessel capacity; record actual process values outside MasterData.
2. Food production: mark allergen and storage condition on materials; enforce shelf-life and FEFO through Inventory; keep recipe version in ProductEngineering.
3. Pharmaceutical batch: use released formula version and GMP-relevant quality characteristics; keep batch record and release decision outside MasterData.
4. Metallurgy heat: track grade and heat/lot policy on SKU; keep actual heat genealogy in MES/Inventory.

## Implementation Implications

1. MasterData Task 4 must expose UOM, SKU industrial attributes, resource hierarchy and device capability APIs before downstream business services depend on them.
2. ProductEngineering MVP must be updated to include Recipe/Formula and ProcessParameter as first-class versioned facts, not only EBOM/MBOM/Routing.
3. Inventory MVP must keep actual lot, serial, heat, expiry and stock state facts, while consuming SKU traceability and UOM policy from MasterData.
4. Quality MVP must distinguish reusable characteristic definitions from inspection standards and release decisions.
5. MES MVP must model batch execution and genealogy separately from static master facts.

## Non-Goals

1. MasterData does not store batch production records, actual process values, inspection results or telemetry samples.
2. MasterData does not implement GMP electronic batch record workflow.
3. MasterData does not control PLC/DCS/SCADA or store control credentials.
4. MasterData does not calculate cost, MRP or inventory balance.
