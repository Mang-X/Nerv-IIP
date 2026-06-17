# Quality Business Gap #415 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close Quality issue #415 by adding structured inspection specifications/AQL, Inventory release via public Quality events, NCR MRB review facts and CAPA lifecycle support.

**Architecture:** Quality owns inspection/NCR/CAPA facts and emits enriched public events. Inventory consumes Quality public contracts and posts service-local stock transfer movements; no service crosses Domain/Infrastructure boundaries or writes another service schema.

**Tech Stack:** .NET 10, CleanDDD/netcorepal, EF Core PostgreSQL, FastEndpoints, CAP integration events, xUnit, `Nerv.IIP.Testing` schema convention helpers.

---

## Files

- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Quality/QualityIntegrationEvents.cs`
- Modify: `backend/tests/Nerv.IIP.Contracts.Quality.Tests/QualityContractJsonTests.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionPlanAggregate/InspectionPlan.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/NonconformanceReportAggregate/NonconformanceReport.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/CorrectiveActionAggregate/CorrectiveAction.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/DomainEvents/NonconformanceReportDomainEvents.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionPlanEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionRecordEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/NonconformanceReportEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/CorrectiveActionEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Repositories/CorrectiveActionRepository.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionPlans/CreateInspectionPlanCommand.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionRecords/CreateInspectionRecordCommand.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/NonconformanceReports/SubmitNonconformanceReportDispositionCommand.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventConverters/InspectionIntegrationEventConverters.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventConverters/NonconformanceReportIntegrationEventConverters.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/CorrectiveActions/CorrectiveActionEndpoints.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Program.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Program.cs`
- Modify: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/InspectionAggregateTests.cs`
- Create: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/CorrectiveActionTests.cs`
- Modify: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionIntegrationEventTests.cs`
- Modify: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityEndpointContractTests.cs`
- Update: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryMovementRequestedConsumerTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`

## Task 1: Red Tests For Inspection Specs And AQL

- [ ] Add failing Quality domain tests that create a plan with a variable characteristic `length` using lower/upper limits and assert a planned record with measured value outside the limits is rejected.
- [ ] Add failing Quality domain tests that create an attribute characteristic with AQL sample size, acceptance number and rejection number and assert pass/reject/conditional outcomes.
- [ ] Run `dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~InspectionAggregateTests`.
- [ ] Implement characteristic specification fields, sampling fields and planned-record calculation until the tests pass.

## Task 2: Red Tests For Quality Events And Inventory Release

- [ ] Add failing Quality contract/event tests asserting inspection event payload includes stock release dimensions and numeric result line facts.
- [ ] Add failing Inventory Web tests where `quality.InspectionPassed` transfers stock from `quality` to `unrestricted`, `quality.InspectionRejected` transfers stock from `quality` to `blocked`, and explicit Quality stock release dimensions disambiguate multiple matching ledgers.
- [ ] Run the focused Quality contract and Inventory consumer tests and confirm failure before implementation.
- [ ] Implement event payload enrichment and Inventory consumer with deterministic idempotency keys.

## Task 3: Red Tests For NCR MRB And CAPA

- [ ] Add failing NCR tests asserting disposition types `rework`, `scrap`, `return-to-supplier` and `conditional-release` require at least one MRB review entry.
- [ ] Add failing CAPA tests for open-from-NCR, add containment/corrective/preventive action, verify effectiveness and close.
- [ ] Run focused Quality domain tests and confirm failure before implementation.
- [ ] Implement MRB review entries, CAPA aggregate, commands and internal endpoints.

## Task 4: Persistence, Migrations And Contracts

- [ ] Update EF configurations for new Quality fields and CAPA tables with comments.
- [ ] Generate a Quality migration with `dotnet tool run dotnet-ef migrations add AddQualityBusinessGap415 ...`.
- [ ] Update schema convention tests where needed and run the focused Quality Web tests.
- [ ] Update schema catalog and readiness docs to describe the new Quality and Inventory closure behavior.

## Task 5: Verification And PR

- [ ] Run focused backend tests: Quality Domain/Web, Inventory Web, Contracts Quality and Contracts IntegrationEvents.
- [ ] Run `dotnet test backend/Nerv.IIP.sln` unless blocked by unrelated baseline failures; report exact failures if blocked.
- [ ] Commit all changes on `codex/issue-415-quality-business-gap`.
- [ ] Push the branch and create a PR with `Closes #415` in the body.
