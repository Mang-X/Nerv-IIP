# Business Master Data Field Matrix

This matrix freezes the first governance pass for BusinessMasterData. It is not a UI form specification. It defines which facts are owned by MasterData, which downstream services consume them, and which facts must stay in other domains.

## Classification Rules

| Category | Definition | Examples | Owner rule |
| --- | --- | --- | --- |
| Master data | Durable business identity or static attribute reused by multiple domains | SKU, UOM, partner, plant, work center, device asset | BusinessMasterData unless a more specific source is listed |
| Reference data | Controlled code list or reusable definition | material type, storage condition, asset class, quality characteristic definition | BusinessMasterData when cross-domain |
| Transactional data | Time-bound business event or process state | purchase order, stock movement, batch record, alarm, inspection record | Owning process domain |
| External reference | ID owned by another platform or external system | IAM userId, fileId, connectorHostId, external ERP code | Original source; MasterData stores reference only |

## Core Objects

| Object | MasterData-owned fields | Required downstream consumers | Explicitly not owned by MasterData |
| --- | --- | --- | --- |
| SKU / Material | organizationId, environmentId, code, name, materialType, category, baseUomCode, inventoryUomCode, purchaseUomCode, salesUomCode, manufacturingUomCode, batchTrackingPolicy, serialTrackingPolicy, shelfLifePolicyCode, storageConditionCode, defaultBarcodeRuleCode, procurementMode, manufacturingEnabled, purchasingEnabled, salesEnabled, qualityRequired, disabled, lifecycle timestamps | ProductEngineering, Planning, Inventory, Quality, ERP, WMS, MES, BarcodeLabel, Maintenance | EBOM/MBOM/recipe versions, stock balance, batch instance, serial instance, order price, actual cost |
| UnitOfMeasure | code, name, dimensionType, precision, roundingMode, disabled | SKU, ProductEngineering, Planning, Inventory, Quality, ERP, MES, Telemetry | Actual measured values in inspections, telemetry samples or reports |
| UomConversion | fromUomCode, toUomCode, factor, offset, precision, roundingMode, effectiveFrom, effectiveTo | Planning, Inventory, ERP, MES, Quality | Formula-specific yield or batch-size scaling; those belong to ProductEngineering |
| BusinessPartner | partnerCode, name, partnerRoles, status, taxRegionCode, defaultCurrencyCode, addresses, contacts, complianceTags, disabled | ERP Procurement/Sales/Finance, WMS, Quality, Planning | RFQ, quotation, purchase order, sales order, AR/AP, supplier scorecard transactions |
| PartnerQualification | partnerCode, qualificationType, materialScope, certificateFileId, validFrom, validTo, status | ERP Procurement, Quality, Planning | Supplier audit workflow, quality release decision, purchase transaction |
| Site / Plant / Area / Line | code, name, hierarchyParentCode, type, timezone, addressRef, disabled | Planning, Inventory, WMS, MES, Maintenance, Telemetry | IAM organization/environment; those remain IAM facts |
| WorkCenter | code, name, resourceType, plantCode, lineCode, defaultCalendarCode, capacityUnit, capacityPerDay, finiteCapacity, bottleneck, costCenterCode, disabled | ProductEngineering, Planning, MES, ERP Costing, Maintenance | Schedule result, actual downtime, operation report |
| WorkCalendar | code, name, timezone, workingTimeRules, exceptionDates, holidayCalendarCode, effectiveFrom, effectiveTo, disabled | Planning, MES, Maintenance | Actual shift attendance, overtime approval, production report |
| Shift | code, name, startTime, endTime, crossesMidnight, paidMinutes, breakRules, disabled | MES, Planning, HR-adjacent business scheduling | IAM membership, payroll calculation |
| Department | code, name, parentDepartmentCode, disabled | Approval, MES, Planning, reporting | IAM organization, IAM role or permission |
| Team | code, name, departmentCode, shiftCode, effectiveFrom, effectiveTo, disabled | MES, Planning, Approval | IAM membership |
| PersonnelSkill | userId, skillCode, level, qualificationRef, effectiveFrom, effectiveTo, disabled | MES dispatch, Maintenance, Approval, Quality | login name, email, roles, permissions, HR payroll |
| DeviceAsset | code, name, assetClassCode, model, manufacturer, serialNo, plantCode, lineCode, workCenterCode, location, criticality, maintainable, telemetryEnabled, externalRefs, disabled | Telemetry, Maintenance, MES, Planning, ERP Costing | PLC/DCS/SCADA secret, tag sample, alarm, maintenance work order |
| ResourceCapability | resourceCode, resourceType, capabilityCode, validMaterialTypes, capacityMin, capacityMax, capacityUomCode, compatibleStorageConditions, effectiveFrom, effectiveTo | ProductEngineering, Planning, MES, Maintenance | Product-specific routing parameter; belongs to ProductEngineering |
| ReferenceData | codeSet, code, name, description, status, effectiveFrom, effectiveTo | All business domains | Transaction-specific states that only one domain owns |

## Process-Manufacturing-Sensitive Fields

| Fact | MasterData role | ProductEngineering role | Quality / Inventory / MES role |
| --- | --- | --- | --- |
| Concentration, potency, density, purity, moisture | Define reusable material attributes and allowed units when stable master attributes | Use values in formula version, yield and process parameter calculations | Record actual measured values in inspections, batch records and telemetry |
| Shelf life and expiry rule | Store default shelf-life policy code and storage-condition dependency | Reference policy when recipe/formula version requires it | Inventory calculates actual expiry per batch; Quality controls release |
| Hazard, allergen, regulatory tag | Store stable material and partner compliance tags | Use tags to validate recipe compatibility | WMS enforces storage segregation; Quality controls release |
| Batch/serial tracking policy | Store whether SKU requires lot, serial, furnace heat, date code or expiry tracking | Reference policy in recipe and routing | Inventory owns actual batch/serial/heat/date-code instances |
| Equipment capacity and compatibility | Store static capacity range, UOM, material compatibility and cleaning class | Use compatible resource class in routing/recipe | MES records actual equipment usage and cleaning execution |
| Quality characteristic definition | Store reusable characteristic code, name, dimension and UOM | Reference required characteristics in product/recipe version | Quality owns inspection standard, sampling rule, result and release |

## Downstream Reference Contract

Downstream services must use one of these patterns:

1. Resolve by code or ID through MasterData public API before creating a new business document.
2. Store a lightweight immutable reference snapshot on the downstream document when historical readability is required.
3. Subscribe to MasterData IntegrationEvents when caching active master data.
4. Keep existing document history valid when a master record is disabled, archived, replaced or merged.

MasterData public API must include batch resolve and validity-check endpoints before downstream services rely on it at scale.

## Governance Matrix

| Area | Steward role | Approval needed before change | Minimum audit |
| --- | --- | --- | --- |
| SKU and UOM | Business administrator + planning/material owner | Required for UOM, traceability, shelf-life or enabled-role change | before/after values, reason, effective date, actor |
| Partner identity | Sales/procurement owner | Required for role, tax/compliance or qualification change | before/after values, reason, actor |
| Resource hierarchy | Production/maintenance owner | Required for plant/line/work-center/device reassignment | old/new parent, effective date, actor |
| Device asset | Maintenance owner | Required for asset class, criticality, maintainable or telemetry-enabled change | before/after values, actor |
| Reference data | Domain steward for code set | Required for code removal or semantic change | code set, code, old/new meaning, actor |

## Open Questions

| Question | Why it matters | Owner |
| --- | --- | --- |
| Should warehouse and storage area identity move from Inventory to MasterData? | Warehouses can be static facilities, but stock location and balances are Inventory facts. | Inventory owner + architecture owner |
| Which planning attributes belong on SKU versus DemandPlanning? | Lead time, lot size and MRP policy may be shared defaults or planning-specific settings. | Planning owner + material owner |
| Should quality characteristic definitions live in MasterData or Quality? | Reusable code definitions are cross-domain, but inspection standards are Quality facts. | Quality owner + architecture owner |
| How much partner commercial data is allowed in MasterData? | Tax, bank, settlement and credit fields have privacy and authorization implications. | ERP owner + security owner |
