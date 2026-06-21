# Issue 414 MES Business Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the main MES backend business-loop gaps from issue #414 through lifecycle state, public integration events, NCR disposition consumption and genealogy fields.

**Architecture:** MES remains the execution fact owner and communicates with Inventory, Quality and WMS through public contracts and integration events. Inventory owns stock posting, Quality owns NCR lifecycle, WMS owns warehouse execution; MES records request/intent and local execution state only.

**Tech Stack:** .NET 10, CleanDDD, EF Core, FastEndpoints, CAP integration event converters, xUnit.

---

## Files

- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/DomainEvents/MesDomainEvents.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/WorkOrderAggregate/WorkOrder.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReport.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReportMaterialConsumption.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/MaterialSupplyAggregate/MaterialIssueRequest.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/FinishedGoodsReceiptRequestAggregate/FinishedGoodsReceiptRequest.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/QualityAggregate/DefectRecord.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/MesProductionQueries.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/*.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/*`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj`
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/MesAggregateTests.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesIntegrationEventTests.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesQualityDispositionConsumerTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`

## Tasks

- [ ] Add failing MES domain tests for work order progress, hold/cancel/close, report genealogy and defect disposition.
- [ ] Implement minimal domain fields, state transitions and domain events.
- [ ] Add failing converter tests for production consumption, finished-goods receipt, material issue and defect handoff events.
- [ ] Implement MES integration converters and add Inventory/Quality contract references.
- [ ] Add failing Quality disposition consumer tests, then implement idempotent consumer.
- [ ] Update command handlers to pass produced lot/serial, rework/scrap reason and emit aggregate events through domain methods.
- [ ] Update EF mappings and add a migration for the new MES fields.
- [ ] Update traceability/read models to return produced lot/serial without fabricated data.
- [ ] Update readiness/schema catalog docs.
- [ ] Run focused MES domain/web tests, schema tests, and the MES verification script.
