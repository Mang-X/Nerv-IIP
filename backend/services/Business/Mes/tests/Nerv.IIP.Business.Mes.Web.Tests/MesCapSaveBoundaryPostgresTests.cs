using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using Npgsql;
using DomainWorkCenterUnavailability = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesCapSubscriptionCollection.Name)]
public sealed class MesCapSaveBoundaryPostgresTests
{
    private static readonly DateTimeOffset OccurredAtUtc = DateTimeOffset.Parse("2026-07-26T08:00:00Z");

    [MesRealPostgresFact]
    public async Task Asset_restored_CAP_boundary_persists_window_result_and_inbox_and_replay_is_idempotent()
    {
        await using var database = await TemporaryDatabase.CreateAsync(ReadConnectionString());
        var options = CreateOptions(database.ConnectionString);
        await MigrateAsync(options);
        await using (var seed = CreateContext(options))
        {
            seed.WorkCenterUnavailabilities.Add(DomainWorkCenterUnavailability.Open(
                "org-001",
                "env-dev",
                "UNAV-MAN421-001",
                "WC-MAN421",
                OccurredAtUtc.AddHours(-1),
                null,
                "maintenance",
                "ASSET-MAN421"));
            await seed.SaveChangesAsync();
        }

        var integrationEvent = CreateAssetRestoredEvent("evt-man421-restored", "restore-man421");
        await using (var handlerContext = CreateContext(options))
        {
            await CreateAssetRestoredHandler(handlerContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertAssetRestoredFactsAsync(options, expectedInboxCount: 1, expectedScheduleCount: 1);

        await using (var replayContext = CreateContext(options))
        {
            await CreateAssetRestoredHandler(replayContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertAssetRestoredFactsAsync(options, expectedInboxCount: 1, expectedScheduleCount: 1);
    }

    [MesRealPostgresFact]
    public async Task Schedule_plan_released_CAP_boundary_persists_assignment_provenance_and_inbox_and_replay_is_idempotent()
    {
        await using var database = await TemporaryDatabase.CreateAsync(ReadConnectionString());
        var options = CreateOptions(database.ConnectionString);
        await MigrateAsync(options);
        await using (var seed = CreateContext(options))
        {
            seed.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-MAN421-SCHEDULE",
                "SKU-MAN421",
                "PV-MAN421",
                10m,
                10,
                OccurredAtUtc.AddDays(1),
                "PCS"));
            seed.OperationTasks.Add(OperationTask.Queue(
                "org-001",
                "env-dev",
                "WO-MAN421-SCHEDULE",
                "OP-MAN421-10",
                10,
                "WC-OLD",
                [],
                OccurredAtUtc,
                TimeSpan.FromHours(1)));
            await seed.SaveChangesAsync();
        }

        var integrationEvent = CreateScheduleReleasedEvent();
        await using (var handlerContext = CreateContext(options))
        {
            await CreateScheduleReleasedHandler(handlerContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertScheduleReleasedFactsAsync(options);

        await using (var replayContext = CreateContext(options))
        {
            await CreateScheduleReleasedHandler(replayContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertScheduleReleasedFactsAsync(options);
    }

    [MesRealPostgresFact]
    public async Task NCR_disposition_CAP_boundary_persists_defect_and_inbox_and_replay_is_idempotent()
    {
        await using var database = await TemporaryDatabase.CreateAsync(ReadConnectionString());
        var options = CreateOptions(database.ConnectionString);
        await MigrateAsync(options);
        await SeedDefectAsync(options, "DEF-MAN421");

        var integrationEvent = CreateNcrDispositionEvent(
            "evt-man421-ncr",
            "ncr-man421",
            "DEF-MAN421",
            QualityNcrDispositionTypes.Rework,
            ncrCode: "NCR-MAN421",
            reworkWorkOrderId: "RW-MAN421");
        await using (var handlerContext = CreateContext(options))
        {
            await CreateNcrHandler(handlerContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertNcrFactsAsync(options, "evt-man421-ncr", DefectRecord.ReworkPendingStatus, "NCR-MAN421", "RW-MAN421");

        await using (var replayContext = CreateContext(options))
        {
            await CreateNcrHandler(replayContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertNcrFactsAsync(options, "evt-man421-ncr", DefectRecord.ReworkPendingStatus, "NCR-MAN421", "RW-MAN421");
    }

    [MesRealPostgresFact]
    public async Task Production_version_created_CAP_boundary_persists_work_order_binding_and_inbox_and_replay_is_idempotent()
    {
        await using var database = await TemporaryDatabase.CreateAsync(ReadConnectionString());
        var options = CreateOptions(database.ConnectionString);
        await MigrateAsync(options);
        await using (var seed = CreateContext(options))
        {
            seed.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-MAN421-PV",
                "SKU-MAN421-PV",
                null,
                5m,
                10,
                OccurredAtUtc.AddDays(1),
                "PCS"));
            await seed.SaveChangesAsync();
        }

        var integrationEvent = CreateProductionVersionCreatedEvent();
        await using (var handlerContext = CreateContext(options))
        {
            await CreateProductionVersionHandler(handlerContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertProductionVersionFactsAsync(options);

        await using (var replayContext = CreateContext(options))
        {
            await CreateProductionVersionHandler(replayContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertProductionVersionFactsAsync(options);
    }

    [MesRealPostgresFact]
    public async Task Post_inbox_early_returns_persist_business_inbox_without_creating_unrelated_facts()
    {
        await using var database = await TemporaryDatabase.CreateAsync(ReadConnectionString());
        var options = CreateOptions(database.ConnectionString);
        await MigrateAsync(options);
        await using (var seed = CreateContext(options))
        {
            seed.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-MAN421-EXISTING",
                "SKU-MAN421-EXISTING",
                "PV-MAN421-EXISTING",
                3m,
                10,
                OccurredAtUtc.AddDays(1),
                "PCS",
                new SourcePlanReference(
                    DemandPlanningSourceReferences.DemandPlanning,
                    DemandPlanningSourceReferences.PlanningSuggestion,
                    "SUG-MAN421-EXISTING",
                    "DEMAND-MAN421")));
            await seed.SaveChangesAsync();
        }

        var planningEvent = CreatePlanningSuggestionEvent();
        await using (var context = CreateContext(options))
        {
            var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
                context,
                new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(context));
            await handler.HandleCapAsync(planningEvent, CancellationToken.None);
        }

        var postedEvent = CreateStockMovementPostedEvent();
        await using (var context = CreateContext(options))
        {
            var handler = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
                context,
                new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(context));
            await handler.HandleCapAsync(postedEvent, CancellationToken.None);
        }

        var failedEvent = CreateUnknownStockMovementPostingFailedEvent();
        await using (var context = CreateContext(options))
        {
            var handler = new StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
                context,
                new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(context));
            await handler.HandleCapAsync(failedEvent, CancellationToken.None);
        }

        await using var assertion = CreateContext(options);
        var inbox = await assertion.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync();
        Assert.Contains(inbox, x =>
            x.ConsumerName == PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName &&
            x.EventId == planningEvent.EventId);
        Assert.Contains(inbox, x =>
            x.ConsumerName == StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted.ConsumerName &&
            x.EventId == postedEvent.EventId);
        Assert.Contains(inbox, x =>
            x.ConsumerName == StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed.ConsumerName &&
            x.EventId == failedEvent.EventId);
        Assert.Single(await assertion.WorkOrders.AsNoTracking().ToArrayAsync());
        Assert.Empty(await assertion.FinishedGoodsReceiptRequests.AsNoTracking().ToArrayAsync());
    }

    [MesRealPostgresFact]
    public async Task NCR_business_divergence_is_dead_lettered_without_partial_defect_mutation_or_poison_throw()
    {
        await using var database = await TemporaryDatabase.CreateAsync(ReadConnectionString());
        var options = CreateOptions(database.ConnectionString);
        await MigrateAsync(options);
        await SeedDefectAsync(options, "DEF-MAN421-DIVERGENCE");

        var integrationEvent = CreateNcrDispositionEvent(
            "evt-man421-ncr-divergence",
            "ncr-man421-divergence",
            "DEF-MAN421-DIVERGENCE",
            QualityNcrDispositionTypes.Scrap,
            ncrCode: string.Empty,
            scrapMovementId: "MOV-MAN421");
        await using (var handlerContext = CreateContext(options))
        {
            await CreateNcrHandler(handlerContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await using var assertion = CreateContext(options);
        var defect = await assertion.DefectRecords.AsNoTracking().SingleAsync();
        Assert.Equal(DefectRecord.OpenStatus, defect.Status);
        Assert.Null(defect.NcrId);
        Assert.Null(defect.NcrCode);
        Assert.Null(defect.DispositionType);
        Assert.Single(await assertion.ProcessedIntegrationEvents.AsNoTracking().Where(x =>
            x.ConsumerName == NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName &&
            x.EventId == integrationEvent.EventId).ToArrayAsync());
        var deadLetter = Assert.Single(await assertion.Set<IntegrationEventDeadLetter>().AsNoTracking().Where(x =>
            x.ConsumerName == NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName &&
            x.EventId == integrationEvent.EventId).ToArrayAsync());
        Assert.Equal("quality-ncr-disposition-divergence", deadLetter.FailureCode);
    }

    [MesRealPostgresFact]
    public async Task Concurrent_NCR_inbox_unique_conflict_converges_without_partial_loser_or_replay_poison()
    {
        await using var database = await TemporaryDatabase.CreateAsync(ReadConnectionString());
        var setupOptions = CreateOptions(database.ConnectionString);
        await MigrateAsync(setupOptions);
        await SeedDefectAsync(setupOptions, "DEF-MAN421-RACE");

        var firstEvent = CreateNcrDispositionEvent(
            "evt-man421-race-rework",
            "ncr-man421-race",
            "DEF-MAN421-RACE",
            QualityNcrDispositionTypes.Rework,
            ncrCode: "NCR-MAN421-REWORK",
            reworkWorkOrderId: "RW-MAN421");
        var secondEvent = CreateNcrDispositionEvent(
            "evt-man421-race-scrap",
            "ncr-man421-race",
            "DEF-MAN421-RACE",
            QualityNcrDispositionTypes.Scrap,
            ncrCode: "NCR-MAN421-SCRAP",
            scrapMovementId: "MOV-MAN421");
        var barrier = new AsyncBarrier(2);
        var racingOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(new SaveBarrierInterceptor(barrier))
            .Options;

        await using var firstContext = CreateContext(racingOptions);
        await using var secondContext = CreateContext(racingOptions);
        var firstTask = CaptureExceptionAsync(() =>
            CreateNcrHandler(firstContext).HandleCapAsync(firstEvent, CancellationToken.None));
        var secondTask = CaptureExceptionAsync(() =>
            CreateNcrHandler(secondContext).HandleCapAsync(secondEvent, CancellationToken.None));
        var exceptions = await Task.WhenAll(firstTask, secondTask);

        Assert.All(exceptions, Assert.Null);

        await using (var assertion = CreateContext(setupOptions))
        {
            var inbox = Assert.Single(await assertion.ProcessedIntegrationEvents.AsNoTracking().Where(x =>
                x.ConsumerName == NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName &&
                x.IdempotencyKey == "ncr-man421-race").ToArrayAsync());
            var defect = await assertion.DefectRecords.AsNoTracking().SingleAsync();
            if (inbox.EventId == firstEvent.EventId)
            {
                Assert.Equal(DefectRecord.ReworkPendingStatus, defect.Status);
                Assert.Equal("NCR-MAN421-REWORK", defect.NcrCode);
                Assert.Equal("RW-MAN421", defect.DispositionReferenceId);
            }
            else
            {
                Assert.Equal(secondEvent.EventId, inbox.EventId);
                Assert.Equal(DefectRecord.ScrapAcceptedStatus, defect.Status);
                Assert.Equal("NCR-MAN421-SCRAP", defect.NcrCode);
                Assert.Equal("MOV-MAN421", defect.DispositionReferenceId);
            }
        }

        var winnerEventId = await ReadNcrRaceWinnerEventIdAsync(setupOptions);
        var losingEvent = winnerEventId == firstEvent.EventId ? secondEvent : firstEvent;
        await using (var replayContext = CreateContext(setupOptions))
        {
            await CreateNcrHandler(replayContext).HandleCapAsync(losingEvent, CancellationToken.None);
        }

        await using var replayAssertion = CreateContext(setupOptions);
        Assert.Single(await replayAssertion.ProcessedIntegrationEvents.AsNoTracking().Where(x =>
            x.ConsumerName == NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName &&
            x.IdempotencyKey == "ncr-man421-race").ToArrayAsync());
    }

    [MesRealPostgresFact]
    public async Task Telemetry_posted_business_divergence_is_dead_lettered_without_partial_report_or_retry_poison()
    {
        await using var database = await TemporaryDatabase.CreateAsync(ReadConnectionString());
        var options = CreateOptions(database.ConnectionString);
        await MigrateAsync(options);
        await SeedRunningTelemetryOperationAsync(options);

        var integrationEvent = CreateTelemetryPostedEvent();
        await using (var handlerContext = CreateContext(options))
        {
            await CreateTelemetryHandler(handlerContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertTelemetryDivergenceFactsAsync(options, integrationEvent);

        await using (var replayContext = CreateContext(options))
        {
            await CreateTelemetryHandler(replayContext).HandleCapAsync(integrationEvent, CancellationToken.None);
        }

        await AssertTelemetryDivergenceFactsAsync(options, integrationEvent);
    }

    private static async Task<string> ReadNcrRaceWinnerEventIdAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var assertion = CreateContext(options);
        return (await assertion.ProcessedIntegrationEvents.AsNoTracking().SingleAsync(x =>
            x.ConsumerName == NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName &&
            x.IdempotencyKey == "ncr-man421-race")).EventId;
    }

    private static AssetRestoredIntegrationEventHandlerForReschedule CreateAssetRestoredHandler(ApplicationDbContext dbContext)
    {
        var store = new PersistentMesPlanningStore(dbContext, new OperationTaskRepository(dbContext));
        return new AssetRestoredIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
            dbContext,
            new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext));
    }

    private static SchedulePlanReleasedIntegrationEventHandlerForDispatch CreateScheduleReleasedHandler(ApplicationDbContext dbContext) =>
        new(
            dbContext,
            new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext),
            new PostgreSqlMesScheduleReleaseScopeCoordinator(dbContext));

    private static NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect CreateNcrHandler(ApplicationDbContext dbContext) =>
        new(dbContext, new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext));

    private static ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders CreateProductionVersionHandler(
        ApplicationDbContext dbContext) =>
        new(dbContext, new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext));

    private static TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport CreateTelemetryHandler(
        ApplicationDbContext dbContext) =>
        new(
            dbContext,
            new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext),
            new ProductionReportSender(dbContext));

    private static async Task AssertAssetRestoredFactsAsync(
        DbContextOptions<ApplicationDbContext> options,
        int expectedInboxCount,
        int expectedScheduleCount)
    {
        await using var assertion = CreateContext(options);
        var window = await assertion.WorkCenterUnavailabilities.AsNoTracking().SingleAsync();
        Assert.Equal(OccurredAtUtc, window.ToUtc);
        Assert.Equal(expectedScheduleCount, await assertion.ScheduleResults.AsNoTracking().CountAsync());
        Assert.Equal(expectedInboxCount, await assertion.ProcessedIntegrationEvents.AsNoTracking().CountAsync(x =>
            x.ConsumerName == AssetRestoredIntegrationEventHandlerForReschedule.ConsumerName));
    }

    private static async Task AssertScheduleReleasedFactsAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var assertion = CreateContext(options);
        var operation = await assertion.OperationTasks.AsNoTracking().SingleAsync();
        Assert.Equal("PLAN-MAN421", operation.SchedulePlanId);
        Assert.Equal(3, operation.ScheduleReleaseRevision);
        Assert.Equal("WC-MAN421-TARGET", operation.WorkCenterId);
        Assert.Equal("ASSET-MAN421-TARGET", operation.DeviceAssetId);
        Assert.Equal(OccurredAtUtc, operation.ScheduledAtUtc);
        Assert.Equal(OccurredAtUtc.AddHours(1), operation.EarliestStartUtc);
        Assert.Equal(TimeSpan.FromHours(1), operation.Duration);
        Assert.Null(operation.ExistingStartUtc);
        Assert.Null(operation.ExistingEndUtc);
        Assert.Single(await assertion.ProcessedIntegrationEvents.AsNoTracking().Where(x =>
            x.ConsumerName == SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName).ToArrayAsync());
    }

    private static async Task AssertNcrFactsAsync(
        DbContextOptions<ApplicationDbContext> options,
        string eventId,
        string expectedStatus,
        string expectedNcrCode,
        string expectedReference)
    {
        await using var assertion = CreateContext(options);
        var defect = await assertion.DefectRecords.AsNoTracking().SingleAsync();
        Assert.Equal(expectedStatus, defect.Status);
        Assert.Equal(expectedNcrCode, defect.NcrCode);
        Assert.Equal(expectedReference, defect.DispositionReferenceId);
        Assert.Single(await assertion.ProcessedIntegrationEvents.AsNoTracking().Where(x =>
            x.ConsumerName == NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName &&
            x.EventId == eventId).ToArrayAsync());
    }

    private static async Task AssertProductionVersionFactsAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var assertion = CreateContext(options);
        Assert.Equal("PV-MAN421", (await assertion.WorkOrders.AsNoTracking().SingleAsync()).ProductionVersionId);
        Assert.Single(await assertion.ProcessedIntegrationEvents.AsNoTracking().Where(x =>
            x.ConsumerName == ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders.ConsumerName).ToArrayAsync());
    }

    private static async Task AssertTelemetryDivergenceFactsAsync(
        DbContextOptions<ApplicationDbContext> options,
        TelemetryProductionCountDeltaIntegrationEvent integrationEvent)
    {
        await using var assertion = CreateContext(options);
        var workOrder = await assertion.WorkOrders.AsNoTracking().SingleAsync();
        Assert.Equal(0m, workOrder.CompletedQuantity);
        Assert.Equal(0, workOrder.CostReportCount);
        Assert.Empty(await assertion.ProductionReports.AsNoTracking().ToArrayAsync());
        Assert.Single(await assertion.ProcessedIntegrationEvents.AsNoTracking().Where(x =>
            x.ConsumerName == TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport.ConsumerName &&
            x.EventId == integrationEvent.EventId).ToArrayAsync());
        var deadLetter = Assert.Single(await assertion.Set<IntegrationEventDeadLetter>().AsNoTracking().Where(x =>
            x.ConsumerName == TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport.ConsumerName &&
            x.EventId == integrationEvent.EventId).ToArrayAsync());
        Assert.Equal("telemetry-production-report-divergence", deadLetter.FailureCode);
    }

    private static DefectRecord CreateDefect(string defectNo) =>
        DefectRecord.Create(
            "org-001",
            "env-dev",
            defectNo,
            "WO-MAN421",
            "OP-MAN421-10",
            "SURFACE",
            1m,
            OccurredAtUtc.AddMinutes(-10));

    private static async Task SeedDefectAsync(
        DbContextOptions<ApplicationDbContext> options,
        string defectNo)
    {
        await using var seed = CreateContext(options);
        seed.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-MAN421",
            "SKU-MAN421",
            "PV-MAN421",
            10m,
            10,
            OccurredAtUtc.AddDays(1),
            "PCS"));
        await seed.SaveChangesAsync();

        seed.DefectRecords.Add(CreateDefect(defectNo));
        await seed.SaveChangesAsync();
    }

    private static async Task SeedRunningTelemetryOperationAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var seed = CreateContext(options);
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-MAN421-TELEMETRY",
            "SKU-MAN421",
            "PV-MAN421",
            10m,
            10,
            OccurredAtUtc.AddDays(1),
            "PCS");
        workOrder.MarkReleased();
        workOrder.Start(OccurredAtUtc.AddHours(-1));
        var operation = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-MAN421-TELEMETRY",
            "OP-MAN421-TELEMETRY",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-MAN421-TELEMETRY",
            [],
            OccurredAtUtc.AddHours(-1),
            TimeSpan.FromHours(1),
            OccurredAtUtc.AddHours(-1),
            null);
        operation.Assign(null, "ASSET-MAN421-TELEMETRY", null, OccurredAtUtc.AddHours(-1));
        seed.WorkOrders.Add(workOrder);
        seed.OperationTasks.Add(operation);
        seed.DeviceAssetWorkCenterMappings.Add(DeviceAssetWorkCenterMapping.Create(
            "org-001",
            "env-dev",
            "ASSET-MAN421-TELEMETRY",
            "WC-MAN421-TELEMETRY"));
        await seed.SaveChangesAsync();
    }

    private static AssetRestoredIntegrationEvent CreateAssetRestoredEvent(string eventId, string idempotencyKey) =>
        new(
            eventId,
            MaintenanceIntegrationEventTypes.AssetRestored,
            MaintenanceIntegrationEventVersions.V1,
            OccurredAtUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-man421-restored",
            "cause-man421-restored",
            "org-001",
            "env-dev",
            "maintenance",
            idempotencyKey,
            new AssetRestoredPayload("ASSET-MAN421", OccurredAtUtc));

    private static SchedulePlanReleasedIntegrationEvent CreateScheduleReleasedEvent() =>
        new(
            "evt-man421-schedule-release",
            SchedulingIntegrationEventTypes.SchedulePlanReleased,
            SchedulingIntegrationEventVersions.V1,
            OccurredAtUtc,
            SchedulingIntegrationEventSources.BusinessScheduling,
            "corr-man421-schedule",
            "cause-man421-schedule",
            "org-001",
            "env-dev",
            "scheduling",
            "schedule-release-man421",
            new SchedulePlanLifecyclePayload(
                "PLAN-MAN421",
                "PROBLEM-MAN421",
                3,
                "aps-lite-v1",
                "fingerprint-man421",
                "released",
                [
                    new SchedulePlanAffectedOperationPayload(
                        "WO-MAN421-SCHEDULE",
                        "OP-MAN421-10",
                        10,
                        "ASSET-MAN421-TARGET",
                        "WC-MAN421-TARGET",
                        OccurredAtUtc.AddHours(1),
                        OccurredAtUtc.AddHours(2))
                ],
                3));

    private static NcrDispositionDecidedIntegrationEvent CreateNcrDispositionEvent(
        string eventId,
        string idempotencyKey,
        string sourceDocumentId,
        string dispositionType,
        string ncrCode,
        string? reworkWorkOrderId = null,
        string? scrapMovementId = null) =>
        new(
            eventId,
            QualityIntegrationEventTypes.DispositionDecided,
            QualityIntegrationEventVersions.V1,
            OccurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            "corr-man421-ncr",
            "cause-man421-ncr",
            "org-001",
            "env-dev",
            "quality",
            idempotencyKey,
            new NcrDispositionDecidedPayload(
                "NCR-ID-MAN421",
                ncrCode,
                "SKU-MAN421",
                1m,
                dispositionType,
                "approval-man421",
                reworkWorkOrderId,
                scrapMovementId,
                null,
                OccurredAtUtc)
            {
                SourceDocumentId = sourceDocumentId,
            });

    private static ProductionVersionCreatedIntegrationEvent CreateProductionVersionCreatedEvent() =>
        new(
            "evt-man421-production-version",
            ProductEngineeringIntegrationEventTypes.ProductionVersionCreated,
            ProductEngineeringIntegrationEventVersions.V1,
            OccurredAtUtc,
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            "corr-man421-production-version",
            "cause-man421-production-version",
            "org-001",
            "env-dev",
            "product-engineering",
            "production-version-man421",
            new ProductionVersionCreatedPayload(
                "PV-MAN421",
                "SKU-MAN421-PV",
                "MBOM-MAN421:1",
                "ROUTING-MAN421:1",
                new DateOnly(2026, 7, 26),
                null));

    private static TelemetryProductionCountDeltaIntegrationEvent CreateTelemetryPostedEvent() =>
        new(
            "evt-man421-telemetry-divergence",
            IndustrialTelemetryIntegrationEventTypes.ProductionCountDeltaRecorded,
            IndustrialTelemetryIntegrationEventVersions.V1,
            OccurredAtUtc,
            IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            "corr-man421-telemetry",
            "cause-man421-telemetry",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            "telemetry-man421-divergence",
            new TelemetryProductionCountDeltaPayload(
                "ASSET-MAN421-TELEMETRY",
                "parts_count",
                TelemetryProductionReportCandidate.PostedReportingMode,
                20m,
                OccurredAtUtc.AddMinutes(-1),
                OccurredAtUtc,
                "seq-man421",
                false));

    private static PlanningSuggestionAcceptedIntegrationEvent CreatePlanningSuggestionEvent() =>
        new(
            "evt-man421-existing-suggestion",
            DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            DemandPlanningIntegrationEventVersions.V1,
            OccurredAtUtc,
            DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            "corr-man421-existing-suggestion",
            "cause-man421-existing-suggestion",
            "org-001",
            "env-dev",
            "planning",
            "planning-existing-man421",
            new PlanningSuggestionAcceptedPayload(
                "SUG-MAN421-EXISTING",
                "MRP-MAN421",
                DemandPlanningSuggestionTypes.PlannedWorkOrder,
                "SKU-MAN421-EXISTING",
                "PCS",
                "SITE-MAN421",
                3m,
                new DateOnly(2026, 7, 27),
                new DateOnly(2026, 7, 26),
                "DEMAND-MAN421",
                "PV-MAN421-EXISTING",
                DemandPlanningDownstreamReferences.BusinessMes,
                DemandPlanningDownstreamReferences.WorkOrder,
                "WO-MAN421-EXISTING"));

    private static StockMovementPostedIntegrationEvent CreateStockMovementPostedEvent() =>
        new(
            "evt-man421-stock-posted-unmatched",
            InventoryIntegrationEventTypes.StockMovementPosted,
            InventoryIntegrationEventVersions.V1,
            OccurredAtUtc,
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-man421-stock-posted",
            "cause-man421-stock-posted",
            "org-001",
            "env-dev",
            "inventory",
            "stock-posted-unmatched-man421",
            new StockMovementPostedPayload(
                "MOV-MAN421",
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                "FGR-MAN421-MISSING",
                "WO-MAN421",
                "mes:finished-goods-receipt:man421",
                "SKU-MAN421",
                "PCS",
                "finished-goods",
                "receiving",
                "LOT-MAN421",
                null,
                "Unrestricted",
                "production",
                null,
                1m,
                OccurredAtUtc,
                null,
                null));

    private static StockMovementPostingFailedIntegrationEvent CreateUnknownStockMovementPostingFailedEvent() =>
        new(
            "evt-man421-stock-failed-unknown",
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1,
            OccurredAtUtc,
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-man421-stock-failed",
            "cause-man421-stock-failed",
            "org-001",
            "env-dev",
            "inventory",
            "stock-failed-unknown-man421",
            new StockMovementPostingFailedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                "DOC-MAN421",
                "LINE-MAN421",
                "mes:unknown:man421",
                "SKU-MAN421",
                "PCS",
                "finished-goods",
                "receiving",
                "LOT-MAN421",
                null,
                "Unrestricted",
                "production",
                null,
                1m,
                "inventory.rejected",
                "Rejected for MAN-421 test.",
                OccurredAtUtc));

    private static DbContextOptions<ApplicationDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private static async Task MigrateAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var context = CreateContext(options);
        await context.Database.MigrateAsync();
    }

    private static string ReadConnectionString() =>
        Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
        ?? throw new InvalidOperationException("NERV_IIP_TEST_POSTGRES is required.");

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class ProductionReportSender(ApplicationDbContext dbContext) : ISender
    {
        private readonly MesCodingService codingService = new();

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is not RecordProductionReportCommand command)
            {
                throw new NotSupportedException($"Unsupported request: {request.GetType().Name}");
            }

            var response = await new RecordProductionReportCommandHandler(dbContext, codingService)
                .Handle(command, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return (TResponse)(object)response;
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SaveBarrierInterceptor(AsyncBarrier barrier) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await barrier.SignalAndWaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class AsyncBarrier(int participantCount)
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int participants;

        public Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref participants) == participantCount)
            {
                completion.TrySetResult();
            }

            return completion.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TemporaryDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"nerv_mes_man421_{Guid.CreateVersion7():N}";
            var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = "postgres",
            }.ConnectionString;
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryDatabase(
                adminConnectionString,
                databaseName,
                new NpgsqlConnectionStringBuilder(baseConnectionString)
                {
                    Database = databaseName,
                }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
