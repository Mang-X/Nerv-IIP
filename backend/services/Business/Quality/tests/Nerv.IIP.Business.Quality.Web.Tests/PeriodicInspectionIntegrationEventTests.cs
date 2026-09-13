using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class PeriodicInspectionIntegrationEventTests
{
    [Fact]
    public async Task Legacy_mes_v1_json_without_dimension_snapshot_keeps_periodic_inspection_semantics()
    {
        const string json = """
            {
              "eventId": "evt-report-RPT-LEGACY-001",
              "eventType": "mes.ProductionReportRecorded",
              "eventVersion": 1,
              "occurredAtUtc": "2026-08-24T01:30:00Z",
              "sourceService": "business-mes",
              "correlationId": "corr-report-RPT-LEGACY-001",
              "causationId": "WO-001",
              "organizationId": "org-001",
              "environmentId": "env-dev",
              "actor": "system:mes",
              "idempotencyKey": "mes:production-report-recorded:org-001:env-dev:RPT-LEGACY-001",
              "payload": {
                "reportNo": "RPT-LEGACY-001",
                "workOrderId": "WO-001",
                "operationTaskId": "OP-001",
                "workCenterId": "WC-001",
                "deviceAssetId": null,
                "goodQuantity": 25,
                "scrapQuantity": 0,
                "reworkQuantity": 0,
                "uomCode": "EA",
                "theoreticalRatePerHour": null,
                "reportedAtUtc": "2026-08-24T01:30:00Z",
                "isReversal": false,
                "reversedReportNo": null,
                "materialMovementCount": 0
              }
            }
            """;
        var integrationEvent = JsonSerializer.Deserialize<ProductionReportRecordedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(integrationEvent);

        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            coordinator,
            deadLetters).HandleAsync(integrationEvent, CancellationToken.None);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .SingleAsync();
        var productionReport = Assert.Single(operation.ProductionReports);
        Assert.Equal("RPT-LEGACY-001", productionReport.ReportNo);
        Assert.Equal(25m, productionReport.GoodQuantity);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Extended_mes_v1_json_with_dimension_snapshot_keeps_periodic_inspection_semantics_and_avoids_dlq()
    {
        const string json = """
            {
              "eventId": "evt-report-RPT-DIMENSION-001",
              "eventType": "mes.ProductionReportRecorded",
              "eventVersion": 1,
              "occurredAtUtc": "2026-08-24T01:30:00Z",
              "sourceService": "business-mes",
              "correlationId": "corr-report-RPT-DIMENSION-001",
              "causationId": "WO-001",
              "organizationId": "org-001",
              "environmentId": "env-dev",
              "actor": "system:mes",
              "idempotencyKey": "mes:production-report-recorded:org-001:env-dev:RPT-DIMENSION-001",
              "payload": {
                "reportNo": "RPT-DIMENSION-001",
                "workOrderId": "WO-001",
                "operationTaskId": "OP-001",
                "workCenterId": "WC-001",
                "deviceAssetId": "DEV-001",
                "goodQuantity": 25,
                "scrapQuantity": 0,
                "reworkQuantity": 0,
                "uomCode": "EA",
                "theoreticalRatePerHour": 120,
                "reportedAtUtc": "2026-08-24T01:30:00Z",
                "isReversal": false,
                "reversedReportNo": null,
                "materialMovementCount": 0,
                "siteCode": "SITE-SH",
                "workshopCode": "WS-MACH",
                "lineCode": "LINE-CNC",
                "shiftCode": "NIGHT",
                "siteTimezone": "Asia/Shanghai",
                "shiftStartsAt": "22:30:00",
                "shiftEndsAt": "06:15:00",
                "shiftCrossesMidnight": true,
                "shiftPaidMinutes": 435,
                "shiftBreakMinutes": 30
              }
            }
            """;
        var integrationEvent = JsonSerializer.Deserialize<ProductionReportRecordedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(integrationEvent);

        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            coordinator,
            deadLetters).HandleAsync(integrationEvent, CancellationToken.None);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .SingleAsync();
        var productionReport = Assert.Single(operation.ProductionReports);
        Assert.Equal("RPT-DIMENSION-001", productionReport.ReportNo);
        Assert.Equal(25m, productionReport.GoodQuantity);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task One_report_crossing_multiple_quantity_intervals_generates_each_stable_assigned_task_once()
    {
        await using var dbContext = CreateDbContext();
        var plan = NewPeriodicPlan();
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        var releaseHandler = new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters);
        var reportHandler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters);
        var report = ProductionReport() with
        {
            Payload = ProductionReport().Payload with { GoodQuantity = 250m },
        };

        await releaseHandler.HandleAsync(WorkOrderReleased(), CancellationToken.None);
        await reportHandler.HandleAsync(report, CancellationToken.None);
        await reportHandler.HandleAsync(report, CancellationToken.None);

        var context = await dbContext.PeriodicInspectionRuntimeContexts.SingleAsync();
        var tasks = await dbContext.InspectionTasks.OrderBy(x => x.Quantity).ToArrayAsync();
        Assert.Collection(
            tasks,
            task => AssertQuantityTask(task, context.Id.Id, 1, 100m, plan.Id),
            task => AssertQuantityTask(task, context.Id.Id, 2, 200m, plan.Id));
        Assert.All(tasks, task => Assert.Equal("team-quality-001", task.AssignedTeamId));
        Assert.Equal(2, context.LastGeneratedQuantityWindowSequence);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Same_report_event_id_with_a_different_valid_payload_is_an_inbox_noop()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);
        var handler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters);
        var first = ProductionReport() with
        {
            Payload = ProductionReport().Payload with { GoodQuantity = 100m },
        };
        var conflictingPayload = ProductionReport("RPT-CHANGED") with
        {
            EventId = first.EventId,
            Payload = ProductionReport("RPT-CHANGED").Payload with { GoodQuantity = 100m },
        };

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(conflictingPayload, CancellationToken.None);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        Assert.Single(operation.ProductionReports);
        Assert.Equal(100m, Assert.Single(operation.RuntimeContexts).QuantityHighWater);
        Assert.Single(await dbContext.InspectionTasks.ToListAsync());
    }

    [Fact]
    public async Task Same_release_event_id_with_a_different_valid_payload_is_an_inbox_noop()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        var handler = new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters);
        var first = WorkOrderReleased();
        var conflictingPayload = WorkOrderReleased("WO-CHANGED", "OP-CHANGED") with
        {
            EventId = first.EventId,
        };

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(conflictingPayload, CancellationToken.None);

        Assert.Single(await dbContext.PeriodicInspectionOperations.ToListAsync());
        Assert.Single(await dbContext.PeriodicInspectionRuntimeContexts.ToListAsync());
    }

    [Fact]
    public async Task Report_before_release_backfills_quantity_windows_from_the_frozen_context()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        var report = ProductionReport() with
        {
            Payload = ProductionReport().Payload with { GoodQuantity = 200m },
        };

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(report, CancellationToken.None);
        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());

        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);

        var tasks = await dbContext.InspectionTasks.OrderBy(x => x.Quantity).ToArrayAsync();
        Assert.Equal([100m, 200m], tasks.Select(x => x.Quantity));
        Assert.Equal(2, (await dbContext.PeriodicInspectionRuntimeContexts.SingleAsync()).LastGeneratedQuantityWindowSequence);
    }

    [Fact]
    public async Task Quantity_remainder_reversal_and_late_report_do_not_duplicate_or_reclaim_windows()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        var handler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);

        await handler.HandleAsync(ProductionReport() with
        {
            Payload = ProductionReport().Payload with { GoodQuantity = 150m },
        }, CancellationToken.None);
        await handler.HandleAsync(ProductionReport("RPT-REV", reportedAtUtc: "2026-08-24T01:20:00Z") with
        {
            Payload = ProductionReport("RPT-REV", reportedAtUtc: "2026-08-24T01:20:00Z").Payload with
            {
                GoodQuantity = -100m,
                IsReversal = true,
                ReversedReportNo = "RPT-001",
            },
        }, CancellationToken.None);
        await handler.HandleAsync(ProductionReport("RPT-LATE", reportedAtUtc: "2026-08-24T01:10:00Z") with
        {
            Payload = ProductionReport("RPT-LATE", reportedAtUtc: "2026-08-24T01:10:00Z").Payload with { GoodQuantity = 150m },
        }, CancellationToken.None);

        var tasks = await dbContext.InspectionTasks.OrderBy(x => x.Quantity).ToArrayAsync();
        Assert.Equal([100m, 200m, 300m], tasks.Select(x => x.Quantity));
        var context = await dbContext.PeriodicInspectionRuntimeContexts.SingleAsync();
        Assert.Equal(200m, context.CumulativeGoodQuantity);
        Assert.Equal(300m, context.QuantityHighWater);
        Assert.Equal(3, context.LastGeneratedQuantityWindowSequence);
    }

    [Fact]
    public async Task Report_after_completion_does_not_generate_new_quantity_windows()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(OperationCompleted(), CancellationToken.None);

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(ProductionReport() with
            {
                Payload = ProductionReport().Payload with { GoodQuantity = 250m },
            }, CancellationToken.None);

        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
        var context = await dbContext.PeriodicInspectionRuntimeContexts.SingleAsync();
        Assert.Equal("closed", context.Status);
        Assert.Equal(0, context.LastGeneratedQuantityWindowSequence);
    }

    [Fact]
    public async Task Fake_time_at_the_first_due_window_generates_one_assigned_operation_task_and_advances_watermark()
    {
        await using var dbContext = CreateDbContext();
        var plan = NewPeriodicPlan();
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(ProductionReport(), CancellationToken.None);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-24T03:30:00Z"));
        var generated = await GenerateDueAsync(dbContext, coordinator, clock.GetUtcNow().UtcDateTime, 100);
        var replayed = await GenerateDueAsync(dbContext, coordinator, clock.GetUtcNow().UtcDateTime, 100);

        Assert.Equal(1, generated);
        Assert.Equal(0, replayed);
        var task = await dbContext.InspectionTasks.SingleAsync();
        Assert.Equal(plan.Id, task.InspectionPlanId);
        Assert.Equal("operation", task.SourceType);
        Assert.Equal("mes", task.SourceService);
        Assert.Equal("WO-001", task.SourceDocumentId);
        Assert.Equal("SKU-FG-1000", task.SkuCode);
        Assert.Equal(25m, task.Quantity);
        Assert.Equal("EA", task.UomCode);
        Assert.Equal("team-quality-001", task.AssignedTeamId);
        var operation = await dbContext.PeriodicInspectionOperations.Include(x => x.RuntimeContexts).SingleAsync();
        var runtimeContext = Assert.Single(operation.RuntimeContexts);
        Assert.Equal($"OP-001:periodic-time:{runtimeContext.Id.Id:D}:1", task.SourceDocumentLineId);
        Assert.Equal($"quality:periodic-time:{runtimeContext.Id.Id:D}:1", task.TriggerIdempotencyKey);
        Assert.Equal(1, runtimeContext.LastGeneratedTimeWindowSequence);
        Assert.Equal(DateTime.Parse("2026-08-24T05:30:00Z").ToUniversalTime(), runtimeContext.NextTimeWindowAtUtc);

        var claimed = await new ClaimInspectionTaskCommandHandler(dbContext).Handle(
            new ClaimInspectionTaskCommand(
                task.Id,
                "org-001",
                "env-dev",
                "inspector-001",
                ["team-quality-001"],
                "claim-periodic-time-task",
                task.Version),
            CancellationToken.None);
        Assert.Equal(InspectionTaskStatuses.InProgress, claimed.Status);
        Assert.Equal("inspector-001", claimed.AssignedInspectorUserId);
    }

    [Fact]
    public async Task Out_of_order_mes_facts_reconcile_a_closed_periodic_context_without_generating_a_task()
    {
        await using var dbContext = CreateDbContext();
        var plan = NewPeriodicPlan();
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var scopeCoordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, scopeCoordinator, deadLetters).HandleAsync(ProductionReport(), CancellationToken.None);
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            dbContext, scopeCoordinator, deadLetters).HandleAsync(OperationCompleted(), CancellationToken.None);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, scopeCoordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);

        var generated = await GenerateDueAsync(
            dbContext,
            scopeCoordinator,
            DateTime.Parse("2026-08-25T01:00:00Z").ToUniversalTime(),
            100);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(plan.Id, context.InspectionPlanId);
        Assert.Equal(1, context.InspectionPlanVersion);
        Assert.Equal(25m, context.CumulativeGoodQuantity);
        Assert.Equal(25m, context.QuantityHighWater);
        Assert.Equal("EA", context.UomCode);
        Assert.Equal("closed", context.Status);
        Assert.Equal(0, generated);
        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Released_periodic_context_without_a_production_report_does_not_generate_a_time_task()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            coordinator,
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(WorkOrderReleased(), CancellationToken.None);

        var generated = await GenerateDueAsync(
            dbContext,
            coordinator,
            DateTime.Parse("2026-08-25T01:00:00Z").ToUniversalTime(),
            100);

        Assert.Equal(0, generated);
        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
    }

    [Fact]
    public async Task Due_context_query_filters_orders_and_applies_the_batch_limit()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        var releaseHandler = new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters);
        var reportHandler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters);

        await releaseHandler.HandleAsync(WorkOrderReleased("WO-001", "OP-001"), CancellationToken.None);
        await reportHandler.HandleAsync(
            ProductionReport("RPT-001", "WO-001", "OP-001", "2026-08-24T04:00:00Z"),
            CancellationToken.None);
        await releaseHandler.HandleAsync(WorkOrderReleased("WO-002", "OP-002"), CancellationToken.None);
        await reportHandler.HandleAsync(
            ProductionReport("RPT-002", "WO-002", "OP-002", "2026-08-24T02:30:00Z"),
            CancellationToken.None);
        await releaseHandler.HandleAsync(WorkOrderReleased("WO-003", "OP-003"), CancellationToken.None);
        await reportHandler.HandleAsync(
            ProductionReport("RPT-003", "WO-003", "OP-003", "2026-08-24T01:00:00Z"),
            CancellationToken.None);

        var beforeAnyWindowIsDue = await new ListDuePeriodicInspectionTimeContextsQueryHandler(dbContext).Handle(
            new ListDuePeriodicInspectionTimeContextsQuery(
                "org-001",
                "env-dev",
                DateTimeOffset.Parse("2026-08-24T02:00:00Z").UtcDateTime,
                1),
            CancellationToken.None);

        var generated = await GenerateDueAsync(
            dbContext,
            coordinator,
            DateTimeOffset.Parse("2026-08-24T04:45:00Z").UtcDateTime,
            1);

        Assert.Empty(beforeAnyWindowIsDue);
        Assert.Equal(1, generated);
        Assert.Equal("WO-003", (await dbContext.InspectionTasks.SingleAsync()).SourceDocumentId);
    }

    [Fact]
    public async Task Two_matching_plans_generate_two_tasks_with_distinct_context_scoped_source_lines()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.AddRange(
            NewPeriodicPlan("IQP-PERIODIC-001"),
            NewPeriodicPlan("IQP-PERIODIC-002"));
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(ProductionReport(), CancellationToken.None);

        var generated = await GenerateDueAsync(
            dbContext,
            coordinator,
            DateTimeOffset.Parse("2026-08-24T03:30:00Z").UtcDateTime,
            100);

        Assert.Equal(2, generated);
        var contexts = await dbContext.PeriodicInspectionRuntimeContexts.ToArrayAsync();
        var tasks = await dbContext.InspectionTasks.ToArrayAsync();
        Assert.Equal(2, contexts.Length);
        Assert.Equal(2, tasks.Length);
        Assert.Equal(
            contexts.Select(x => $"OP-001:periodic-time:{x.Id.Id:D}:1").Order().ToArray(),
            tasks.Select(x => x.SourceDocumentLineId).Order().ToArray());
    }

    [Fact]
    public async Task Zero_quantity_context_is_not_selected_for_time_generation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(
                ProductionReport() with
                {
                    Payload = ProductionReport().Payload with { GoodQuantity = 0m },
                },
                CancellationToken.None);

        var candidates = await new ListDuePeriodicInspectionTimeContextsQueryHandler(dbContext).Handle(
            new ListDuePeriodicInspectionTimeContextsQuery(
                "org-001",
                "env-dev",
                DateTimeOffset.Parse("2026-08-25T03:30:00Z").UtcDateTime,
                100),
            CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task Release_without_a_matching_periodic_plan_preserves_source_facts_without_creating_a_context()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        Assert.Equal("SKU-FG-1000", operation.SkuCode);
        Assert.Empty(operation.RuntimeContexts);
        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_mes_business_facts_are_dead_lettered_without_creating_a_context()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var malformed = ProductionReport() with
        {
            Payload = ProductionReport().Payload with { WorkCenterId = " " },
        };

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters).HandleAsync(malformed, CancellationToken.None);

        Assert.Empty(await dbContext.PeriodicInspectionOperations.ToListAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("invalid-business-facts", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Same_event_id_changed_report_payload_is_an_inbox_noop_before_domain_identity_checks()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters);

        await handler.HandleAsync(ProductionReport(), CancellationToken.None);
        var conflicting = ProductionReport() with
        {
            Payload = ProductionReport().Payload with { GoodQuantity = 30m },
        };
        await handler.HandleAsync(conflicting, CancellationToken.None);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .SingleAsync();
        Assert.Equal(25m, Assert.Single(operation.ProductionReports).GoodQuantity);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    // ===== #3129 行为探针（常驻）=====
    // 两条探针钉的是同一个根因的两个投影：owner 裁定「下达之前已产出的数量不补开巡检任务」，
    // 而做这个判断所需的事实——**哪些产量在下达动作之前就已存在、且按工序分辨**——只存在于 MES 侧。
    // 载荷补上该事实之前，两条探针在 main 上分别读到 4 与 2（本票基线读数），目标都是 0。
    // 探针①是「空间」面（多工序被压成一个工单级标量），探针②是「时间」面（发布先于报工到达）。

    [Fact]
    public async Task Release_opens_no_quantity_window_for_any_operation_that_had_already_produced_before_the_release_action()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        var reportHandler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters);

        await reportHandler.HandleAsync(
            ReportOf("RPT-OP10", "OP-10", "2026-08-02T00:00:00Z", 250m),
            CancellationToken.None);
        await reportHandler.HandleAsync(
            ReportOf("RPT-OP20", "OP-20", "2026-08-03T00:00:00Z", 250m),
            CancellationToken.None);

        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(
                ReleasedAt(
                    "2026-08-02T00:00:00Z",
                    // 载荷里这两个 250 就是 MES 在下达那一刻按工序算出来的既有净良品量。
                    // 它们不是本用例编出来的数：MES 侧同场景的产出由
                    // `WorkOrderReleaseFactTimePostgresTests
                    //  .Release_carries_each_operations_pre_release_good_quantity_on_postgres`
                    // 独立钉住——那条用例在真实 PostgreSQL 上跑 ReleaseWorkOrderCommandHandler，
                    // 用同样「两道工序各 250」的报工，断言载荷里落出的就是 [250, 250]。
                    new ReleasedOperationPayload("OP-10", 10, "WC-001", 250m),
                    new ReleasedOperationPayload("OP-20", 20, "WC-001", 250m)),
                CancellationToken.None);

        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
        var contexts = await dbContext.PeriodicInspectionRuntimeContexts
            .OrderBy(x => x.OperationId)
            .ToArrayAsync();
        Assert.Equal([2L, 2L], contexts.Select(x => x.LastGeneratedQuantityWindowSequence));
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Release_arriving_before_the_report_opens_no_quantity_window_for_production_that_preceded_the_release_action()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);

        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(
                ReleasedAt("2026-08-02T00:00:00Z", new ReleasedOperationPayload("OP-001", 10, "WC-001", 250m)),
                CancellationToken.None);
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(
                ReportOf("RPT-001", "OP-001", "2026-08-02T00:00:00Z", 250m),
                CancellationToken.None);

        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
        var context = await dbContext.PeriodicInspectionRuntimeContexts.SingleAsync();
        Assert.Equal(250m, context.QuantityHighWater);
        Assert.Equal(2, context.LastGeneratedQuantityWindowSequence);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    /// <summary>
    /// 阳性对照的「带值」形态：MES 查过、答案是 0（下达动作发生时这道工序一条报工都没有），
    /// 窗口照常开。它与 <c>Report_before_release_backfills_quantity_windows_from_the_frozen_context</c>
    /// 是同一个场景的两种载荷——后者不带该字段（旧消息），本条带 <c>0</c>（上线后的新消息）。
    /// 少了这一条，「跳过」那一支被整块删掉时只有探针会红，**判别式退化成无条件跳过不会红**。
    /// </summary>
    [Fact]
    public async Task Release_carrying_an_explicit_zero_pre_release_quantity_still_opens_the_accrued_windows()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(
                ReportOf("RPT-001", "OP-001", "2026-08-24T01:30:00Z", 200m),
                CancellationToken.None);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(
                ReleasedAt("2026-08-24T01:00:00Z", new ReleasedOperationPayload("OP-001", 10, "WC-001", 0m)),
                CancellationToken.None);

        var tasks = await dbContext.InspectionTasks.OrderBy(x => x.Quantity).ToArrayAsync();
        Assert.Equal([100m, 200m], tasks.Select(x => x.Quantity));
        Assert.Equal(2, (await dbContext.PeriodicInspectionRuntimeContexts.SingleAsync()).LastGeneratedQuantityWindowSequence);
    }

    /// <summary>
    /// <b>把「null 时的行为」当断言写，而不是只写在注释里。</b>
    /// 本次发布之前入队、此刻仍在在途队列或 DLQ 里的旧 <c>mes.WorkOrderReleased</c> 不带该字段；
    /// 它们照**老行为**处理——不跳过，下达前的产量仍被补开成巡检任务，
    /// 与本次改动之前的 main 逐字相同（探针①在 main 上的读数就是这个 4）。
    /// 这是一个**已知的、明确的不生效面**，不是安全默认值；
    /// 它随那批旧消息被消费干净而消失，完整论证见 <c>ReleasedOperationPayload.PreReleaseGoodQuantity</c>。
    ///
    /// <para>这条用例同时是承重格的「另一半」：若有人把消费侧的 <c>is { }</c> 判空改成
    /// <c>?? 0</c> 之类的静默回落，本条不会变——但把 null 当成「跳过全部」处理的写法会打红它。</para>
    /// </summary>
    [Fact]
    public async Task Release_without_the_pre_release_quantity_field_keeps_the_pre_3129_behaviour()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        var reportHandler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters);

        await reportHandler.HandleAsync(
            ReportOf("RPT-OP10", "OP-10", "2026-08-02T00:00:00Z", 250m),
            CancellationToken.None);
        await reportHandler.HandleAsync(
            ReportOf("RPT-OP20", "OP-20", "2026-08-03T00:00:00Z", 250m),
            CancellationToken.None);

        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(
                ReleasedAt(
                    "2026-08-02T00:00:00Z",
                    new ReleasedOperationPayload("OP-10", 10, "WC-001"),
                    new ReleasedOperationPayload("OP-20", 20, "WC-001")),
                CancellationToken.None);

        Assert.Equal(4, await dbContext.InspectionTasks.CountAsync());
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    private static ProductionReportRecordedIntegrationEvent ReportOf(
        string reportNo,
        string operationId,
        string reportedAtUtc,
        decimal goodQuantity)
    {
        var template = ProductionReport(reportNo, operationId: operationId, reportedAtUtc: reportedAtUtc);
        return template with { Payload = template.Payload with { GoodQuantity = goodQuantity } };
    }

    private static WorkOrderReleasedIntegrationEvent ReleasedAt(
        string releasedAtUtc,
        params ReleasedOperationPayload[] operations) => new(
        "evt-release-WO-001",
        MesIntegrationEventTypes.WorkOrderReleased,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse(releasedAtUtc),
        MesIntegrationEventSources.BusinessMes,
        "corr-release-WO-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:work-order-released:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001",
            "SKU-FG-1000",
            1000m,
            DateTimeOffset.Parse(releasedAtUtc),
            operations));

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"periodic-inspection-{Guid.CreateVersion7()}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static void AssertQuantityTask(
        InspectionTask task,
        Guid runtimeContextId,
        long sequence,
        decimal thresholdQuantity,
        InspectionPlanId inspectionPlanId)
    {
        Assert.Equal(inspectionPlanId, task.InspectionPlanId);
        Assert.Equal("operation", task.SourceType);
        Assert.Equal("mes", task.SourceService);
        Assert.Equal("WO-001", task.SourceDocumentId);
        Assert.Equal($"OP-001:periodic-quantity:{runtimeContextId:D}:{sequence}", task.SourceDocumentLineId);
        Assert.Equal($"quality:periodic-quantity:{runtimeContextId:D}:{sequence}", task.TriggerIdempotencyKey);
        Assert.Equal("SKU-FG-1000", task.SkuCode);
        Assert.Equal(thresholdQuantity, task.Quantity);
        Assert.Equal("EA", task.UomCode);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T01:30:00Z"), task.CreatedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-25T01:30:00Z"), task.DueAtUtc);
    }

    private static async Task<int> GenerateDueAsync(
        ApplicationDbContext dbContext,
        IPeriodicInspectionOperationScopeCoordinator coordinator,
        DateTime nowUtc,
        int batchSize)
    {
        var candidates = await new ListDuePeriodicInspectionTimeContextsQueryHandler(dbContext).Handle(
            new ListDuePeriodicInspectionTimeContextsQuery("org-001", "env-dev", nowUtc, batchSize),
            CancellationToken.None);
        var generated = 0;
        foreach (var candidate in candidates)
        {
            generated += await new GeneratePeriodicInspectionTimeTaskForContextCommandHandler(
                dbContext,
                coordinator).Handle(
                    new GeneratePeriodicInspectionTimeTaskForContextCommand(
                        "org-001",
                        "env-dev",
                        candidate.WorkOrderId,
                        candidate.OperationId,
                        candidate.RuntimeContextId,
                        nowUtc,
                        24),
                    CancellationToken.None);
        }

        return generated;
    }

    private static InspectionPlan NewPeriodicPlan(string planCode = "IQP-PERIODIC-001")
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", planCode, "operation", "SKU-FG-1000", null, "WC-001", null, "mes-operation",
            timeIntervalHours: 2m,
            quantityInterval: 100m,
            assignedTeamId: "team-quality-001");
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();
        return plan;
    }

    private static WorkOrderReleasedIntegrationEvent WorkOrderReleased(
        string workOrderId = "WO-001",
        string operationId = "OP-001") => new(
        $"evt-release-{workOrderId}",
        MesIntegrationEventTypes.WorkOrderReleased,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        $"corr-release-{workOrderId}",
        workOrderId,
        "org-001",
        "env-dev",
        "system:mes",
        $"mes:work-order-released:org-001:env-dev:{workOrderId}",
        new WorkOrderReleasedPayload(
            workOrderId,
            "SKU-FG-1000",
            1000m,
            DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
            [new ReleasedOperationPayload(operationId, 10, "WC-001")]));

    private static ProductionReportRecordedIntegrationEvent ProductionReport(
        string reportNo = "RPT-001",
        string workOrderId = "WO-001",
        string operationId = "OP-001",
        string reportedAtUtc = "2026-08-24T01:30:00Z") => new(
        $"evt-report-{reportNo}",
        MesIntegrationEventTypes.ProductionReportRecorded,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse(reportedAtUtc),
        MesIntegrationEventSources.BusinessMes,
        $"corr-report-{reportNo}",
        workOrderId,
        "org-001",
        "env-dev",
        "system:mes",
        $"mes:production-report-recorded:org-001:env-dev:{reportNo}",
        new ProductionReportRecordedPayload(
            reportNo, workOrderId, operationId, "WC-001", null, 25m, 0m, 0m, "EA", null,
            DateTimeOffset.Parse(reportedAtUtc), false));

    private static MesOperationTaskCompletedIntegrationEvent OperationCompleted() => new(
        "evt-complete-001",
        MesIntegrationEventTypes.OperationTaskCompleted,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T04:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "corr-complete-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:operation-completed:org-001:env-dev:WO-001:OP-001",
        new OperationTaskCompletedPayload(
            "WO-001", "OP-001", "SKU-FG-1000", 10, "WC-001", 1000m, "EA", false,
            DateTimeOffset.Parse("2026-08-24T04:00:00Z")));

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
