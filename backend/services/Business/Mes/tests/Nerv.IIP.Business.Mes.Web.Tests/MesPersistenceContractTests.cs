using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesPersistenceContractTests
{
    [Fact]
    public async Task Rush_work_order_survives_service_scope_recreation_when_persistence_is_enabled()
    {
        var services = CreateServices(nameof(Rush_work_order_survives_service_scope_recreation_when_persistence_is_enabled));
        var now = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var handler = new CreateRushWorkOrderCommandHandler(
                scope.ServiceProvider.GetRequiredService<IMesPlanningStore>(),
                scope.ServiceProvider.GetRequiredService<RuleScheduler>());

            await handler.Handle(
                new CreateRushWorkOrderCommand(
                    "org-001",
                    "env-dev",
                    "WO-PERSISTED",
                    "SKU-P",
                    "PV-P",
                    1m,
                    now.AddHours(2),
                    "WC-A",
                    "OP-10",
                    10,
                    TimeSpan.FromMinutes(30),
                    now),
                CancellationToken.None);

            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        using var recreatedScope = services.CreateScope();
        var store = recreatedScope.ServiceProvider.GetRequiredService<IMesPlanningStore>();

        Assert.Contains(await store.GetWorkOrdersAsync(), x => x.WorkOrderId == "WO-PERSISTED");
        Assert.Contains(await store.GetOperationTasksAsync(), x => x.OperationTaskId == "OP-10");
        Assert.Contains(await store.GetScheduleResultsAsync(), x => x.Trigger == RescheduleTrigger.RushOrder);
    }

    [Fact]
    public async Task Reschedule_uses_persisted_work_order_and_schedule_facts()
    {
        var services = CreateServices(nameof(Reschedule_uses_persisted_work_order_and_schedule_facts));
        var now = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IMesPlanningStore>();
            store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddHours(12)));
            store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));

            var handler = new RescheduleCommandHandler(store, scope.ServiceProvider.GetRequiredService<RuleScheduler>());
            await handler.Handle(new RescheduleCommand("org-001", "env-dev", RescheduleTrigger.Manual, now), CancellationToken.None);

            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IMesPlanningStore>();
            store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, now.AddHours(4), "breakdown"));

            var handler = new RescheduleCommandHandler(store, scope.ServiceProvider.GetRequiredService<RuleScheduler>());
            var second = await handler.Handle(new RescheduleCommand("org-001", "env-dev", RescheduleTrigger.AssetUnavailable, now.AddMinutes(5)), CancellationToken.None);

            Assert.Equal(2, second.ScheduleVersion);
            Assert.Contains("WO-001", second.AffectedWorkOrderIds);
        }
    }

    [Fact]
    public async Task Maintenance_asset_unavailable_event_updates_persisted_scheduling_constraints()
    {
        var services = CreateServices(nameof(Maintenance_asset_unavailable_event_updates_persisted_scheduling_constraints));
        var now = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IMesPlanningStore>();
            store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
            store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
            store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        using (var scope = services.CreateScope())
        {
            var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
                scope.ServiceProvider.GetRequiredService<IMesPlanningStore>(),
                scope.ServiceProvider.GetRequiredService<RuleScheduler>(),
                new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = true });

            await handler.HandleAsync(CreateUnavailableEvent(now), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        using var recreatedScope = services.CreateScope();
        var recreatedStore = recreatedScope.ServiceProvider.GetRequiredService<IMesPlanningStore>();
        var window = Assert.Single(await recreatedStore.GetUnavailabilitiesAsync());
        Assert.Equal("WC-A", window.WorkCenterId);
        Assert.Equal("ASSET-CNC-01", window.DeviceAssetId);
        Assert.Null(window.ToUtc);
        Assert.Equal(RescheduleTrigger.AssetUnavailable, Assert.Single(await recreatedStore.GetScheduleResultsAsync()).Trigger);
    }

    [Fact]
    public async Task Scheduling_reads_operation_tasks_only_for_requested_organization_and_environment()
    {
        var services = CreateServices(nameof(Scheduling_reads_operation_tasks_only_for_requested_organization_and_environment));
        var now = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IMesPlanningStore>();
        store.AddWorkOrder(new PlannedWorkOrder("org-a", "env-dev", "WO-SHARED", "SKU-A", null, 1m, 10, now.AddHours(4)));
        store.AddOperationTask(new PlannedOperationTask("WO-SHARED", "OP-A", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromMinutes(30), OrganizationId: "org-a", EnvironmentId: "env-dev"));
        store.AddWorkOrder(new PlannedWorkOrder("org-b", "env-dev", "WO-SHARED", "SKU-B", null, 1m, 10, now.AddHours(4)));
        store.AddOperationTask(new PlannedOperationTask("WO-SHARED", "OP-B", OperationTaskStatus.Queued, 10, "WC-B", [], now, TimeSpan.FromMinutes(30), OrganizationId: "org-b", EnvironmentId: "env-dev"));
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();

        var orgAOperations = await store.GetScheduleOperationsAsync("org-a", "env-dev");

        var operation = Assert.Single(orgAOperations);
        Assert.Equal("OP-A", operation.OperationTaskId);
        Assert.Equal("WC-A", operation.WorkCenterId);
    }

    [Fact]
    public async Task Maintenance_unavailability_constraints_are_scoped_to_event_organization_and_environment()
    {
        var services = CreateServices(nameof(Maintenance_unavailability_constraints_are_scoped_to_event_organization_and_environment));
        var now = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IMesPlanningStore>();
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-a", "env-dev", "WO-A", "SKU-A", null, 1m, 10, now.AddHours(4)));
        store.AddOperationTask(new PlannedOperationTask("WO-A", "OP-A", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(1), OrganizationId: "org-a", EnvironmentId: "env-dev"));
        store.AddWorkOrder(new PlannedWorkOrder("org-b", "env-dev", "WO-B", "SKU-B", null, 1m, 10, now.AddHours(4)));
        store.AddOperationTask(new PlannedOperationTask("WO-B", "OP-B", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(1), OrganizationId: "org-b", EnvironmentId: "env-dev"));
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();

        var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
            store,
            scope.ServiceProvider.GetRequiredService<RuleScheduler>(),
            new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = false });
        await handler.HandleAsync(CreateUnavailableEvent(now, organizationId: "org-b"), CancellationToken.None);

        var orgAPlan = new RuleScheduler().Schedule(
            await store.GetScheduleOperationsAsync("org-a", "env-dev"),
            await store.GetUnavailabilitiesAsync("org-a", "env-dev"));
        var orgBPlan = new RuleScheduler().Schedule(
            await store.GetScheduleOperationsAsync("org-b", "env-dev"),
            await store.GetUnavailabilitiesAsync("org-b", "env-dev"));

        Assert.Equal(now, Assert.Single(orgAPlan.Assignments).StartUtc);
        Assert.True(Assert.Single(orgBPlan.Assignments).StartUtc > now);
    }

    [Fact]
    public async Task Production_report_and_finished_goods_receipt_request_round_trip_through_persistence()
    {
        var services = CreateServices(nameof(Production_report_and_finished_goods_receipt_request_round_trip_through_persistence));
        var now = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ProductionReports.Add(ProductionReport.Record("org-001", "env-dev", "WO-001", "OP-10", 9m, 1m, true, now));
            dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "WO-001", "SKU-001", 9m, "PCS", now.AddMinutes(10)));
            await dbContext.SaveChangesAsync();
        }

        using var recreatedScope = services.CreateScope();
        var recreatedDbContext = recreatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var report = await recreatedDbContext.ProductionReports.SingleAsync();
        var receiptRequest = await recreatedDbContext.FinishedGoodsReceiptRequests.SingleAsync();

        Assert.Equal(9m, report.GoodQuantity);
        Assert.Equal("WO-001", receiptRequest.WorkOrderId);
        Assert.Equal("PCS", receiptRequest.UomCode);
    }

    [Fact]
    public async Task List_work_orders_query_returns_scoped_persisted_work_orders_with_tasks()
    {
        var services = CreateServices(nameof(List_work_orders_query_returns_scoped_persisted_work_orders_with_tasks));
        var now = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-a", "env-dev", "WO-A", "SKU-A", "PV-A", 10m, 20, now.AddDays(1)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-a",
            "env-dev",
            "WO-A",
            "OP-A-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-A",
            ["WC-B"],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.WorkOrders.Add(WorkOrder.Create("org-b", "env-dev", "WO-B", "SKU-B", null, 1m, 5, now.AddDays(2)));
        await dbContext.SaveChangesAsync();

        var handler = new ListMesWorkOrdersQueryHandler(dbContext);
        var response = await handler.Handle(
            new ListMesWorkOrdersQuery("org-a", "env-dev", null, 100),
            CancellationToken.None);

        var workOrder = Assert.Single(response.Items);
        Assert.Equal("WO-A", workOrder.WorkOrderId);
        Assert.Equal("SKU-A", workOrder.SkuId);
        Assert.Equal("PV-A", workOrder.ProductionVersionId);
        Assert.Equal("created", workOrder.Status);
        var task = Assert.Single(workOrder.OperationTasks);
        Assert.Equal("OP-A-10", task.OperationTaskId);
        Assert.Equal("Queued", task.Status);
        Assert.Equal("WC-A", task.WorkCenterId);
    }

    private static ServiceProvider CreateServices(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IOperationTaskRepository, OperationTaskRepository>();
        services.AddScoped<IMesPlanningStore, PersistentMesPlanningStore>();
        services.AddSingleton<RuleScheduler>();
        return services.BuildServiceProvider();
    }

    private static AssetUnavailableIntegrationEvent CreateUnavailableEvent(
        DateTimeOffset fromUtc,
        string organizationId = "org-001",
        string environmentId = "env-dev")
    {
        return new AssetUnavailableIntegrationEvent(
            "evt-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            fromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-001",
            "cause-001",
            organizationId,
            environmentId,
            "maintenance",
            "maintenance.AssetUnavailable:ASSET-CNC-01:20260523080000",
            new AssetUnavailablePayload("ASSET-CNC-01", "breakdown", fromUtc));
    }
}
