# Issue 407 BusinessMasterData Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the BusinessMasterData P0/P1 gaps from GitHub issue #407 for SKU planning attributes, channel UOMs, capacity attributes, lifecycle flags, partner commercial fields, UOM effective end dates, and calendar/shift scheduling facts.

**Architecture:** Keep BusinessMasterData as the Layer 0 owner of durable SKU/UOM/partner/resource/calendar static facts, matching ADR 0013 and `business-master-data-field-matrix.md`. Extend existing aggregates and generic MasterData resource API surfaces instead of introducing cross-service coupling or moving planning logic into platform services. DemandPlanning, MES, ERP, WMS, Scheduling and Quality can snapshot these static fields through existing internal HTTP/OpenAPI contracts.

**Tech Stack:** .NET 10, CleanDDD/netcorepal, FastEndpoints, EF Core migrations, PostgreSQL schema `business_masterdata`, xUnit.

---

### Task 1: Regression Tests

**Files:**
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/MasterDataAggregateTests.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataApiContractTests.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataSchemaConventionTests.cs`

- [ ] Add domain tests proving SKU can keep distinct inventory/purchase/sales/manufacturing UOMs, planning defaults, lifecycle status and usage gates.
- [ ] Add domain tests proving BusinessPartner.Update can change primary role and keep commercial/tax/contact defaults.
- [ ] Add domain tests proving WorkCenter stores utilization, efficiency, capacity count, cost center and bottleneck flag.
- [ ] Add domain tests proving UomConversion supports `EffectiveTo`, WorkCalendar supports timezone/effective range/holiday calendar, and Shift supports break minutes.
- [ ] Run `dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --filter MasterDataAggregateTests --no-restore`; expected first run fails because production fields are missing.
- [ ] Add Web/API tests for create/update/detail projection of the new fields.
- [ ] Run `dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --filter MasterDataApiContractTests --no-restore`; expected first run fails because commands/DTOs are missing.

### Task 2: Domain And Command Implementation

**Files:**
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/SkuAggregate/Sku.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/BusinessPartnerAggregate/BusinessPartner.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCenterAggregate/WorkCenter.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/UomConversionAggregate/UomConversion.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCalendarAggregate/WorkCalendar.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ShiftAggregate/Shift.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/CreateMasterDataCommands.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/MasterDataLifecycleCommands.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/GetMasterDataResourceDetailQuery.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/MasterData/MasterDataEndpoints.cs`

- [ ] Add nullable/defaulted SKU planning fields: `ProcurementType`, `MrpType`, `LotSizingPolicy`, `MinimumLotSize`, `MaximumLotSize`, `LotSizeMultiple`, `SafetyStockQuantity`, `ReorderPointQuantity`, `PlannedDeliveryTimeDays`, `InHouseProductionTimeDays`, `GoodsReceiptProcessingTimeDays`, `AbcClass`.
- [ ] Add SKU lifecycle and usage gates: `LifecycleStatus`, `PurchasingEnabled`, `ManufacturingEnabled`, `SalesEnabled`.
- [ ] Add SKU channel UOM input/update parameters and stop collapsing all channel UOMs to base UOM.
- [ ] Validate non-base SKU channel UOMs against active UOM conversions when command handlers have `ApplicationDbContext`.
- [ ] Add `EffectiveTo` to UOM conversion and reject an end date before the start date.
- [ ] Add WorkCenter capacity fields: `UtilizationRate`, `EfficiencyRate`, `NumberOfCapacities`, `CostCenterCode`, `Bottleneck`, plus computed effective daily capacity if useful for tests.
- [ ] Add partner commercial/contact fields: `TaxRegionCode`, `DefaultCurrencyCode`, `PaymentTermsCode`, `PrimaryAddress`, `PrimaryContactName`, `PrimaryContactEmail`, `PrimaryContactPhone`.
- [ ] Fix `BusinessPartner.Update` so role normalization uses the new role input, not the old `PartnerType`.
- [ ] Add WorkCalendar `Timezone`, `HolidayCalendarCode`, `EffectiveFrom`, `EffectiveTo`.
- [ ] Add Shift `BreakMinutes`.
- [ ] Run the failing tests again and make them pass.

### Task 3: EF Configuration And Migration

**Files:**
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/<timestamp>_CloseBusinessMasterData407Gaps.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] Add column mappings with max lengths, precision, required/default values, and comments for every new persisted field.
- [ ] Generate EF migration with `Persistence__Provider=PostgreSQL`.
- [ ] Run schema convention tests and update any missing comments/defaults.

### Task 4: Documentation

**Files:**
- Modify: `docs/architecture/business-master-data-field-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] Update field matrix open questions to state that SKU default planning attributes live in BusinessMasterData for shared defaults; planning services may still own site-specific overrides.
- [ ] Update BusinessMasterData schema catalog row notes for `skus`, `uom_conversions`, `business_partners`, `work_centers`, `work_calendars`, and `shifts`.
- [ ] Update readiness status to mention issue #407 MasterData static planning/resource/partner field closure.

### Task 5: Verification And PR

**Files:**
- No code edits unless verification finds failures.

- [ ] Run focused Domain tests.
- [ ] Run focused Web/API/schema tests.
- [ ] Run `dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj`.
- [ ] Run `dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj`.
- [ ] Run `dotnet test backend/Nerv.IIP.sln --filter "FullyQualifiedName~Business.MasterData"`.
- [ ] If OpenAPI snapshots or generated client drift is required by changed public contract workflow, run the governed OpenAPI/codegen script rather than hand-edit generated files.
- [ ] Commit all scoped changes.
- [ ] Push `codex/issue-407-business-master-data-gap`.
- [ ] Create PR with `Fix #407` in the title or `Closes #407` in the body.
