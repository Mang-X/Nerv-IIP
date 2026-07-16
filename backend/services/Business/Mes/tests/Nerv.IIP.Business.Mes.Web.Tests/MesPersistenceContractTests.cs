using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

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
                new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = true },
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                new InMemoryIntegrationEventDeadLetterStore());

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
            new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = false },
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore());
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
            dbContext.ProductionReports.Add(ProductionReport.Record("org-001", "env-dev", "PRPT-001", "WO-001", "OP-10", 9m, 1m, true, now));
            dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-001", "WO-001", "SKU-001", 9m, "PCS", now.AddMinutes(10)));
            await dbContext.SaveChangesAsync();
        }

        using var recreatedScope = services.CreateScope();
        var recreatedDbContext = recreatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var report = await recreatedDbContext.ProductionReports.SingleAsync();
        var receiptRequest = await recreatedDbContext.FinishedGoodsReceiptRequests.SingleAsync();

        Assert.Equal(9m, report.GoodQuantity);
        Assert.Equal("PRPT-001", report.ReportNo);
        Assert.Equal("WO-001", receiptRequest.WorkOrderId);
        Assert.Equal("FGR-001", receiptRequest.RequestNo);
        Assert.Equal("PCS", receiptRequest.UomCode);
    }

    [Fact]
    public async Task Material_readiness_uses_persisted_requirement_issue_and_line_side_receipt_facts()
    {
        var services = CreateServices(nameof(Material_readiness_uses_persisted_requirement_issue_and_line_side_receipt_facts));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-MAT-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-MAT-001",
                "OP-MAT-10",
                OperationTaskLifecycleStatus.Queued,
                10,
                "WC-FILL",
                [],
                now,
                TimeSpan.FromMinutes(45),
                null,
                null));
            dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001",
                "env-dev",
                "WO-MAT-001",
                "OP-MAT-10",
                "MAT-OIL",
                "LOT-OIL-A",
                requiredQuantity: 10m,
                availableQuantity: 3m,
                stagedQuantity: 1m,
                sourceSystem: "Inventory",
                sourceSnapshotId: "inv-snap-001",
                capturedAtUtc: now));
            await dbContext.SaveChangesAsync();
        }

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var createHandler = new CreateMaterialIssueRequestCommandHandler(dbContext);
            var response = await createHandler.Handle(
                new CreateMaterialIssueRequestCommand("org-001", "env-dev", "WO-MAT-001", "OP-MAT-10", "MAT-OIL", "L", 4m, now.AddMinutes(5), "issue-001"),
                CancellationToken.None);

            await dbContext.SaveChangesAsync();

            var receiptHandler = new ConfirmLineSideMaterialReceiptCommandHandler(dbContext);
            await receiptHandler.Handle(
                new ConfirmLineSideMaterialReceiptCommand("org-001", "env-dev", response.ReferenceId, now.AddMinutes(15), 4m, "LOT-OIL-A"),
                CancellationToken.None);

            await dbContext.SaveChangesAsync();
        }

        using var recreatedScope = services.CreateScope();
        var recreatedDbContext = recreatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var readiness = await new GetMaterialReadinessQueryHandler(recreatedDbContext).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-MAT-001"),
            CancellationToken.None);

        Assert.Equal("Blocked", readiness.ReadinessStatus);
        Assert.Contains("MAT-OIL LOT-OIL-A shortage 2", readiness.BlockingReasons);
        var row = Assert.Single(readiness.Items);
        Assert.Equal("MAT-OIL", row.MaterialId);
        Assert.Equal("LOT-OIL-A", row.MaterialLotId);
        Assert.Equal(10m, row.RequiredQuantity);
        Assert.Equal(3m, row.AvailableQuantity);
        Assert.Equal(4m, row.RequestedQuantity);
        Assert.Equal(1m, row.StagedQuantity);
        Assert.Equal(4m, row.ReceivedQuantity);
        Assert.Equal(2m, row.ShortageQuantity);
        Assert.Equal("Shortage", row.Status);
    }

    [Fact]
    public async Task Material_readiness_does_not_use_received_quantity_from_a_different_lot()
    {
        var services = CreateServices(nameof(Material_readiness_does_not_use_received_quantity_from_a_different_lot));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-LOT-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-LOT-001",
            "OP-LOT-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-LOT-001",
            "OP-LOT-10",
            "MAT-OIL",
            "LOT-OIL-A",
            requiredQuantity: 10m,
            availableQuantity: 3m,
            stagedQuantity: 1m,
            sourceSystem: "Inventory",
            sourceSnapshotId: "inv-snap-lot-a",
            capturedAtUtc: now));
        var wrongLotRequest = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-WRONG-LOT",
            "WO-LOT-001",
            "OP-LOT-10",
            "MAT-OIL",
            "L",
            6m,
            now.AddMinutes(1));
        wrongLotRequest.ConfirmLineSideReceipt(now.AddMinutes(2), 6m, "LOT-OIL-B");
        dbContext.MaterialIssueRequests.Add(wrongLotRequest);
        await dbContext.SaveChangesAsync();

        var readiness = await new GetMaterialReadinessQueryHandler(dbContext).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-LOT-001"),
            CancellationToken.None);

        var row = Assert.Single(readiness.Items);
        Assert.Equal("LOT-OIL-A", row.MaterialLotId);
        Assert.Equal(0m, row.ReceivedQuantity);
        Assert.Equal(6m, row.ShortageQuantity);
        Assert.Contains("MAT-OIL LOT-OIL-A shortage 6", readiness.BlockingReasons);
    }

    [Fact]
    public void Material_issue_request_rejects_mixed_lot_partial_receipts()
    {
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-MIXED-LOT",
            "WO-MIXED-LOT",
            "OP-MIXED-10",
            "MAT-OIL",
            "L",
            10m,
            now);

        request.ConfirmLineSideReceipt(now.AddMinutes(5), 4m, "LOT-OIL-A");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            request.ConfirmLineSideReceipt(now.AddMinutes(10), 6m, "LOT-OIL-B"));
        Assert.Contains("同一领料申请不能混用多个物料批次", exception.Message);
    }

    [Fact]
    public async Task Release_and_start_are_blocked_when_material_readiness_has_shortage()
    {
        var services = CreateServices(nameof(Release_and_start_are_blocked_when_material_readiness_has_shortage));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-BLOCKED-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-BLOCKED-001",
            "OP-BLOCKED-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-BLOCKED-001",
            "OP-BLOCKED-10",
            "MAT-SEAL",
            null,
            requiredQuantity: 10m,
            availableQuantity: 2m,
            stagedQuantity: 0m,
            sourceSystem: "Inventory",
            sourceSnapshotId: "inv-snap-002",
            capturedAtUtc: now));
        await dbContext.SaveChangesAsync();

        var releaseException = await Assert.ThrowsAsync<KnownException>(() =>
            new ReleaseWorkOrderCommandHandler(dbContext).Handle(
                new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-BLOCKED-001", now.AddMinutes(30)),
                CancellationToken.None));
        Assert.Contains("物料齐套未满足", releaseException.Message);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-BLOCKED-10", "start", now.AddMinutes(35)),
                CancellationToken.None));
        Assert.Contains("物料齐套未满足", exception.Message);
    }

    [Fact]
    public async Task Release_work_order_captures_material_requirements_from_released_mbom_snapshot()
    {
        var services = CreateServices(nameof(Release_work_order_captures_material_requirements_from_released_mbom_snapshot));
        var now = DateTimeOffset.Parse("2026-06-19T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-CAPTURE-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-CAPTURE-001",
            "OP-CAPTURE-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        await dbContext.SaveChangesAsync();

        var snapshotProvider = new FakeMesMaterialRequirementSnapshotProvider(
            MesMaterialRequirementSnapshotResult.Captured(
                "product-engineering-http:PV-FSA-1:MBOM-FSA-1",
                [
                    new MesMaterialRequirementSnapshotLine(
                        "OP-CAPTURE-10",
                        "MAT-SEAL",
                        null,
                        20m,
                        "pcs",
                        20m,
                        0m,
                        "MBOM-FSA-1:MAT-SEAL"),
                    new MesMaterialRequirementSnapshotLine(
                        "OP-CAPTURE-10",
                        "MAT-OIL",
                        null,
                        5m,
                        "L",
                        5m,
                        0m,
                        "MBOM-FSA-1:MAT-OIL"),
                ]));

        var release = await new ReleaseWorkOrderCommandHandler(dbContext, snapshotProvider).Handle(
            new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-CAPTURE-001", now.AddMinutes(10)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal("Accepted", release.Status);
        Assert.Single(snapshotProvider.Requests);
        Assert.Equal("PV-FSA-1", snapshotProvider.Requests[0].ProductionVersionId);
        var requirements = await dbContext.MaterialRequirements
            .Where(x => x.WorkOrderId == "WO-CAPTURE-001")
            .OrderBy(x => x.MaterialId)
            .ToArrayAsync();
        Assert.Collection(
            requirements,
            oil =>
            {
                Assert.Equal("MAT-OIL", oil.MaterialId);
                Assert.Equal("OP-CAPTURE-10", oil.OperationTaskId);
                Assert.Equal(5m, oil.RequiredQuantity);
                Assert.Equal(5m, oil.AvailableQuantity);
                Assert.Equal("product-engineering-http:PV-FSA-1:MBOM-FSA-1", oil.SourceSystem);
            },
            seal =>
            {
                Assert.Equal("MAT-SEAL", seal.MaterialId);
                Assert.Equal("OP-CAPTURE-10", seal.OperationTaskId);
                Assert.Equal(20m, seal.RequiredQuantity);
                Assert.Equal(20m, seal.AvailableQuantity);
                Assert.Equal("product-engineering-http:PV-FSA-1:MBOM-FSA-1", seal.SourceSystem);
            });
    }

    [Fact]
    public async Task Convert_plan_to_work_order_captures_material_requirements_from_real_production_snapshot()
    {
        var services = CreateServices(nameof(Convert_plan_to_work_order_captures_material_requirements_from_real_production_snapshot));
        var now = DateTimeOffset.Parse("2026-06-24T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshotProvider = new FakeMesMaterialRequirementSnapshotProvider(
            MesMaterialRequirementSnapshotResult.Captured(
                "product-engineering-http:PV-FSA-1:MBOM-FSA-1",
                [
                    new MesMaterialRequirementSnapshotLine(
                        null,
                        "MAT-OIL",
                        null,
                        5m,
                        "L",
                        4m,
                        0m,
                        "MBOM-FSA-1:MAT-OIL"),
                ]));
        var handler = new ConvertPlanToWorkOrderCommandHandler(dbContext, new RuleScheduler(), null, snapshotProvider);

        var response = await handler.Handle(
            new ConvertPlanToWorkOrderCommand(
                "org-001",
                "env-dev",
                "PLAN-FSA-001",
                null,
                now,
                "FG-FSA",
                "PV-FSA-1",
                10m,
                "PCS",
                now.AddDays(2),
                "WC-FILL",
                "DemandPlanning",
                "PlanningSuggestion",
                "SUG-FSA-001",
                "DEMAND-FSA-001",
                "convert-plan-material-capture"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Single(snapshotProvider.Requests);
        Assert.Equal(response.ReferenceId, snapshotProvider.Requests[0].WorkOrderId);
        Assert.Equal("FG-FSA", snapshotProvider.Requests[0].SkuId);
        Assert.Equal("PV-FSA-1", snapshotProvider.Requests[0].ProductionVersionId);
        var requirement = Assert.Single(await dbContext.MaterialRequirements
            .Where(x => x.WorkOrderId == response.ReferenceId)
            .ToArrayAsync());
        Assert.Equal("MAT-OIL", requirement.MaterialId);
        Assert.Equal(5m, requirement.RequiredQuantity);
        Assert.Equal(4m, requirement.AvailableQuantity);
        Assert.Equal("product-engineering-http:PV-FSA-1:MBOM-FSA-1", requirement.SourceSystem);
    }

    [Fact]
    public async Task Release_and_start_are_blocked_when_material_requirement_snapshot_is_missing()
    {
        var services = CreateServices(nameof(Release_and_start_are_blocked_when_material_requirement_snapshot_is_missing));
        var now = DateTimeOffset.Parse("2026-06-19T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-MISSING-MAT-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-MISSING-MAT-001",
            "OP-MISSING-MAT-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        await dbContext.SaveChangesAsync();

        var releaseException = await Assert.ThrowsAsync<KnownException>(() =>
            new ReleaseWorkOrderCommandHandler(dbContext, FakeMesMaterialRequirementSnapshotProvider.Missing).Handle(
                new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-MISSING-MAT-001", now.AddMinutes(10)),
                CancellationToken.None));
        Assert.Contains("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING", releaseException.Message);

        var startException = await Assert.ThrowsAsync<KnownException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext, FakeMesMaterialRequirementSnapshotProvider.Missing).Handle(
                new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-MISSING-MAT-10", "start", now.AddMinutes(15)),
                CancellationToken.None));
        Assert.Contains("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING", startException.Message);
    }

    [Fact]
    public async Task Quality_inspection_result_hold_blocks_release_and_start_until_cleared()
    {
        var services = CreateServices(nameof(Quality_inspection_result_hold_blocks_release_and_start_until_cleared));
        var now = DateTimeOffset.Parse("2026-06-24T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QH-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-QH-001",
            "OP-QH-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-QH-001",
            "OP-QH-10",
            "MAT-OIL",
            null,
            requiredQuantity: 5m,
            availableQuantity: 5m,
            stagedQuantity: 0m,
            sourceSystem: "Inventory",
            sourceSnapshotId: "inv-ready-qh",
            capturedAtUtc: now));
        await dbContext.SaveChangesAsync();

        var qualityConsumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(dbContext, deadLetters);
        await qualityConsumer.HandleAsync(CreateInspectionResultEvent(
            "evt-qh-rejected-001",
            QualityIntegrationEventTypes.InspectionRejected,
            "QI-QH-001",
            "WO-QH-001",
            now.AddMinutes(1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var releaseHold = await Assert.ThrowsAsync<KnownException>(() =>
            new ReleaseWorkOrderCommandHandler(dbContext).Handle(
                new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-QH-001", now.AddMinutes(5)),
                CancellationToken.None));
        Assert.Contains(MesReadinessReasonCodes.QualityHoldActive, releaseHold.Message);

        await qualityConsumer.HandleAsync(CreateInspectionResultEvent(
            "evt-qh-passed-001",
            QualityIntegrationEventTypes.InspectionPassed,
            "QI-QH-002",
            "WO-QH-001",
            now.AddMinutes(6)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var release = await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
            new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-QH-001", now.AddMinutes(10)),
            CancellationToken.None);
        Assert.Equal("Accepted", release.Status);

        await qualityConsumer.HandleAsync(CreateInspectionResultEvent(
            "evt-qh-rejected-002",
            QualityIntegrationEventTypes.InspectionRejected,
            "QI-QH-003",
            "WO-QH-001",
            now.AddMinutes(11)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var startHold = await Assert.ThrowsAsync<KnownException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-QH-10", "start", now.AddMinutes(12)),
                CancellationToken.None));
        Assert.Contains(MesReadinessReasonCodes.QualityHoldActive, startHold.Message);
    }

    [Fact]
    public async Task Conditional_release_clears_only_matching_operation_hold_and_allows_dispatch_without_manual_intervention()
    {
        var services = CreateServices(nameof(Conditional_release_clears_only_matching_operation_hold_and_allows_dispatch_without_manual_intervention));
        var now = DateTimeOffset.Parse("2026-07-05T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QH-SCOPE", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.AddRange(
            OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-QH-SCOPE",
                "OP-QH-SCOPE-10",
                OperationTaskLifecycleStatus.Queued,
                10,
                "WC-FILL",
                [],
                now,
                TimeSpan.FromMinutes(45),
                null,
                null),
            OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-QH-SCOPE",
                "OP-QH-SCOPE-20",
                OperationTaskLifecycleStatus.Queued,
                20,
                "WC-PACK",
                [],
                now,
                TimeSpan.FromMinutes(45),
                null,
                null));
        await dbContext.SaveChangesAsync();

        var qualityConsumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(dbContext, deadLetters);
        await qualityConsumer.HandleAsync(CreateInspectionResultEvent(
            "evt-qh-scope-rejected-10",
            QualityIntegrationEventTypes.InspectionRejected,
            "QI-QH-SCOPE-REJECTED-10",
            "OP-QH-SCOPE-10",
            now.AddMinutes(1)),
            CancellationToken.None);
        await qualityConsumer.HandleAsync(CreateInspectionResultEvent(
            "evt-qh-scope-rejected-20",
            QualityIntegrationEventTypes.InspectionRejected,
            "QI-QH-SCOPE-REJECTED-20",
            "OP-QH-SCOPE-20",
            now.AddMinutes(2)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var blockedDispatch = await Assert.ThrowsAsync<KnownException>(() =>
            new AssignDispatchTaskCommandHandler(dbContext).Handle(
                new AssignDispatchTaskCommand("org-001", "env-dev", "OP-QH-SCOPE-10", "operator-a", null, "shift-a", now.AddMinutes(3)),
                CancellationToken.None));
        Assert.Contains(MesReadinessReasonCodes.QualityHoldActive, blockedDispatch.Message);

        await qualityConsumer.HandleAsync(CreateInspectionResultEvent(
            "evt-qh-scope-conditional-10",
            QualityIntegrationEventTypes.InspectionConditionalReleased,
            "QI-QH-SCOPE-RELEASED-10",
            "OP-QH-SCOPE-10",
            now.AddMinutes(4),
            dispositionReason: "conditional release for OP-QH-SCOPE-10 only"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var releasedDispatch = await new AssignDispatchTaskCommandHandler(dbContext).Handle(
            new AssignDispatchTaskCommand("org-001", "env-dev", "OP-QH-SCOPE-10", "operator-a", null, "shift-a", now.AddMinutes(5)),
            CancellationToken.None);
        Assert.Equal("Accepted", releasedDispatch.Status);

        var stillBlockedDispatch = await Assert.ThrowsAsync<KnownException>(() =>
            new AssignDispatchTaskCommandHandler(dbContext).Handle(
                new AssignDispatchTaskCommand("org-001", "env-dev", "OP-QH-SCOPE-20", "operator-b", null, "shift-b", now.AddMinutes(6)),
                CancellationToken.None));
        Assert.Contains(MesReadinessReasonCodes.QualityHoldActive, stillBlockedDispatch.Message);

        var releasedHold = await dbContext.QualityHoldContexts.SingleAsync(x => x.SourceDocumentId == "OP-QH-SCOPE-10");
        var activeHold = await dbContext.QualityHoldContexts.SingleAsync(x => x.SourceDocumentId == "OP-QH-SCOPE-20");
        Assert.False(releasedHold.Active);
        Assert.Equal("QI-QH-SCOPE-REJECTED-10", releasedHold.HeldInspectionRecordId);
        Assert.Equal("QI-QH-SCOPE-RELEASED-10", releasedHold.ReleaseInspectionRecordId);
        Assert.Equal(now.AddMinutes(4), releasedHold.ReleasedAtUtc);
        Assert.Equal("quality.InspectionConditionalReleased", releasedHold.ReleaseSource);
        Assert.True(activeHold.Active);
    }

    [Fact]
    public async Task Force_release_quality_hold_requires_reason_and_is_idempotent()
    {
        var services = CreateServices(nameof(Force_release_quality_hold_requires_reason_and_is_idempotent));
        var now = DateTimeOffset.Parse("2026-07-05T09:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QH-FORCE", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-QH-FORCE",
            "OP-QH-FORCE-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        await dbContext.SaveChangesAsync();

        var qualityConsumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(dbContext, deadLetters);
        await qualityConsumer.HandleAsync(CreateInspectionResultEvent(
            "evt-qh-force-rejected",
            QualityIntegrationEventTypes.InspectionRejected,
            "QI-QH-FORCE-REJECTED",
            "WO-QH-FORCE",
            now.AddMinutes(1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var validator = new ForceReleaseQualityHoldCommandValidator();
        var validation = validator.Validate(new ForceReleaseQualityHoldCommand(
            "org-001",
            "env-dev",
            QualityIntegrationEventSources.BusinessMes,
            "WO-QH-FORCE",
            "",
            "supervisor-001",
            "corr-validation",
            "idem-validation",
            now.AddMinutes(2)));
        Assert.False(validation.IsValid);

        var handler = new ForceReleaseQualityHoldCommandHandler(dbContext);
        var first = await handler.Handle(
            new ForceReleaseQualityHoldCommand(
                "org-001",
                "env-dev",
                QualityIntegrationEventSources.BusinessMes,
                "WO-QH-FORCE",
                "QA supervisor approved urgent release after recheck.",
                "supervisor-001",
                "corr-force-release-1",
                "idem-force-release-1",
                now.AddMinutes(3)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var second = await handler.Handle(
            new ForceReleaseQualityHoldCommand(
                "org-001",
                "env-dev",
                QualityIntegrationEventSources.BusinessMes,
                "WO-QH-FORCE",
                "second release should be idempotent",
                "supervisor-002",
                "corr-force-release-2",
                "idem-force-release-2",
                now.AddMinutes(4)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var hold = await dbContext.QualityHoldContexts.SingleAsync(x => x.SourceDocumentId == "WO-QH-FORCE");
        Assert.Equal("Accepted", first.Status);
        Assert.Equal("Accepted", second.Status);
        Assert.False(hold.Active);
        Assert.Equal("QI-QH-FORCE-REJECTED", hold.HeldInspectionRecordId);
        Assert.Equal("manual-force-release", hold.ReleaseSource);
        Assert.Equal("QA supervisor approved urgent release after recheck.", hold.ReleaseReason);
        Assert.Equal("supervisor-001", hold.ReleasedBy);
        Assert.Equal(now.AddMinutes(3), hold.ReleasedAtUtc);

        var transitions = await dbContext.QualityHoldTransitions
            .OrderBy(x => x.OccurredAtUtc)
            .ToListAsync();
        Assert.Collection(
            transitions,
            applied =>
            {
                Assert.Equal("hold-applied", applied.EventKind);
                Assert.Equal("automatic", applied.Origin);
                Assert.Equal("QI-QH-FORCE-REJECTED", applied.SourceInspectionRecordId);
                Assert.Equal("PLAN-QH-001", applied.SourceInspectionDocumentId);
                Assert.Equal("quality:inspection-result:org-001:env-dev:QI-QH-FORCE-REJECTED:quality.InspectionRejected", applied.IdempotencyKey);
                Assert.Equal("quality", applied.Actor);
            },
            released =>
            {
                Assert.Equal("manual-force-released", released.EventKind);
                Assert.Equal("manual", released.Origin);
                Assert.Equal("supervisor-001", released.Actor);
                Assert.Equal("QA supervisor approved urgent release after recheck.", released.Reason);
                Assert.Equal("idem-force-release-1", released.IdempotencyKey);
                Assert.Equal("QI-QH-FORCE-REJECTED", released.SourceInspectionRecordId);
                Assert.Equal("PLAN-QH-001", released.SourceInspectionDocumentId);
            },
            noOp =>
            {
                Assert.Equal("manual-force-release-noop", noOp.EventKind);
                Assert.Equal("manual", noOp.Origin);
                Assert.Equal("supervisor-002", noOp.Actor);
                Assert.Equal("second release should be idempotent", noOp.Reason);
                Assert.Equal("idem-force-release-2", noOp.IdempotencyKey);
            });
        Assert.Equal(transitions[0].HoldCycleId, transitions[1].HoldCycleId);
        Assert.Equal(transitions[0].HoldCycleId, transitions[2].HoldCycleId);

        await qualityConsumer.HandleAsync(CreateInspectionResultEvent(
            "evt-qh-force-rejected-cycle-2",
            QualityIntegrationEventTypes.InspectionRejected,
            "QI-QH-FORCE-REJECTED-CYCLE-2",
            "WO-QH-FORCE",
            now.AddMinutes(3).AddSeconds(30)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await handler.Handle(
            new ForceReleaseQualityHoldCommand(
                "org-001",
                "env-dev",
                QualityIntegrationEventSources.BusinessMes,
                "WO-QH-FORCE",
                "second release should be idempotent",
                "supervisor-002",
                "corr-force-release-2",
                "idem-force-release-2",
                now.AddMinutes(4)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(hold.Active);
        Assert.Equal("QI-QH-FORCE-REJECTED-CYCLE-2", hold.HeldInspectionRecordId);
        var noOp = await dbContext.QualityHoldTransitions.SingleAsync(x =>
            x.IdempotencyKey == "idem-force-release-2");
        Assert.Equal("manual-force-release-noop", noOp.EventKind);

        var conflict = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ForceReleaseQualityHoldCommand(
                "org-001",
                "env-dev",
                QualityIntegrationEventSources.BusinessMes,
                "WO-QH-FORCE",
                "different replay payload",
                "another-supervisor",
                "corr-force-release-conflict",
                "idem-force-release-2",
                now.AddMinutes(5)),
            CancellationToken.None));
        Assert.Contains("different payload", conflict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(hold.Active);

    }

    [Fact]
    public async Task Quality_hold_history_preserves_two_cycles_in_stable_order_and_ignores_event_replay()
    {
        var services = CreateServices(nameof(Quality_hold_history_preserves_two_cycles_in_stable_order_and_ignores_event_replay));
        var now = DateTimeOffset.Parse("2026-07-13T01:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QH-HISTORY", "FG-A", "PV-A", 10m, 20, now.AddHours(8)));
        await dbContext.SaveChangesAsync();

        var consumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        var events = new[]
        {
            CreateInspectionResultEvent("evt-history-1", QualityIntegrationEventTypes.InspectionRejected, "QI-HISTORY-1", "WO-QH-HISTORY", now.AddMinutes(1), "cycle one defect"),
            CreateInspectionResultEvent("evt-history-2", QualityIntegrationEventTypes.InspectionPassed, "QI-HISTORY-2", "WO-QH-HISTORY", now.AddMinutes(2), "cycle one cleared"),
            CreateInspectionResultEvent("evt-history-3", QualityIntegrationEventTypes.InspectionRejected, "QI-HISTORY-3", "WO-QH-HISTORY", now.AddMinutes(3), "cycle two defect"),
            CreateInspectionResultEvent("evt-history-4", QualityIntegrationEventTypes.InspectionConditionalReleased, "QI-HISTORY-4", "WO-QH-HISTORY", now.AddMinutes(4), "cycle two conditional release"),
        };

        foreach (var integrationEvent in events)
        {
            await consumer.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        await consumer.HandleAsync(events[0], CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var transitions = await dbContext.QualityHoldTransitions
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev" && x.SourceDocumentId == "WO-QH-HISTORY")
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync();

        Assert.Equal(4, transitions.Count);
        Assert.Equal(new[] { "hold-applied", "inspection-released", "hold-applied", "inspection-released" }, transitions.Select(x => x.EventKind));
        Assert.Equal(2, transitions.Select(x => x.HoldCycleId).Distinct().Count());
        Assert.All(transitions, x => Assert.False(string.IsNullOrWhiteSpace(x.Actor)));
        Assert.All(transitions, x => Assert.NotEqual(default, x.OccurredAtUtc));

        var timeline = await new GetQualityHoldTimelineQueryHandler(dbContext).Handle(
            new GetQualityHoldTimelineQuery("org-001", "env-dev", QualityIntegrationEventSources.BusinessMes, "WO-QH-HISTORY"),
            CancellationToken.None);
        Assert.Equal(4, timeline.Items.Count);
        Assert.Equal(transitions.Select(x => x.Id.ToString()), timeline.Items.Select(x => x.TransitionId));
        Assert.Equal(transitions.Select(x => x.IdempotencyKey), timeline.Items.Select(x => x.IdempotencyKey));
    }

    [Fact]
    public void FinishedGoodsReceipt_validator_treats_unit_cost_as_optional()
    {
        // Now that the command validators actually run in the request pipeline, this guards against the validator
        // over-constraining an optional field: FinishedGoodsReceiptRequest.Create accepts a null UnitCost, so the
        // validator must accept it too (and only require positivity when a value is supplied).
        var validator = new CreateFinishedGoodsReceiptRequestCommandValidator();
        var command = new CreateFinishedGoodsReceiptRequestCommand(
            "org-001",
            "env-dev",
            "WO-FG",
            "SKU-FG",
            5m,
            "PCS",
            DateTimeOffset.Parse("2026-05-27T08:00:00Z"),
            UnitCost: null,
            IdempotencyKey: "idem-fg",
            ProducedLotNo: "LOT-FG-1");

        Assert.True(validator.Validate(command).IsValid);
        Assert.True(validator.Validate(command with { UnitCost = 12.34m }).IsValid);

        // Every other field is valid, so a negative unit cost is the only thing that can fail here — exactly one error.
        var negative = validator.Validate(command with { UnitCost = -1m });
        Assert.False(negative.IsValid);
        Assert.Single(negative.Errors);
    }

    [Fact]
    public async Task Foundation_readiness_reports_quality_plan_and_equipment_blocking_reason_codes()
    {
        var services = CreateServices(nameof(Foundation_readiness_reports_quality_plan_and_equipment_blocking_reason_codes));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "DOWNTIME-MAINT-001",
            "WC-FILL",
            now.AddMinutes(-30),
            null,
            EquipmentRuntimeReasonCodes.MaintenanceWindow,
            "ASSET-FILL-01"));
        await dbContext.SaveChangesAsync();

        var handler = new GetMesFoundationReadinessAreaQueryHandler(new MesFoundationReadinessService(dbContext));
        var quality = await handler.Handle(
            new GetMesFoundationReadinessAreaQuery(
                "org-001",
                "env-dev",
                "quality",
                "SITE-01",
                "LINE-01",
                "WC-FILL",
                "FG-FSA",
                "PV-FSA-1",
                now,
                now.AddHours(1)),
            CancellationToken.None);
        var equipment = await handler.Handle(
            new GetMesFoundationReadinessAreaQuery(
                "org-001",
                "env-dev",
                "equipment",
                "SITE-01",
                "LINE-01",
                "WC-FILL",
                "FG-FSA",
                "PV-FSA-1",
                now,
                now.AddHours(1)),
            CancellationToken.None);

        Assert.Equal("Blocked", quality.Status);
        Assert.Contains(quality.Issues, x => x.Code == "QUALITY_PLAN_MISSING" && x.Severity == "Blocked");
        Assert.Equal("Blocked", equipment.Status);
        Assert.Contains(equipment.Issues, x =>
            x.Code == EquipmentRuntimeReasonCodes.MaintenanceWindow &&
            x.SourceSystem == "Maintenance" &&
            x.ReferenceId == "DOWNTIME-MAINT-001");
    }

    [Fact]
    public async Task Foundation_readiness_without_execution_context_does_not_block_contextual_quality_or_equipment_checks()
    {
        var services = CreateServices(nameof(Foundation_readiness_without_execution_context_does_not_block_contextual_quality_or_equipment_checks));

        using var scope = services.CreateScope();
        var handler = new GetMesFoundationReadinessAreaQueryHandler(
            new MesFoundationReadinessService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()));

        var quality = await handler.Handle(
            new GetMesFoundationReadinessAreaQuery(
                "org-001",
                "env-dev",
                "quality",
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);
        var equipment = await handler.Handle(
            new GetMesFoundationReadinessAreaQuery(
                "org-001",
                "env-dev",
                "equipment",
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);

        Assert.Equal("Ready", quality.Status);
        Assert.Empty(quality.Issues);
        Assert.Equal("Ready", equipment.Status);
        Assert.Empty(equipment.Issues);
    }

    [Fact]
    public async Task Equipment_readiness_returns_shared_active_alarm_reason_code()
    {
        var services = CreateServices(nameof(Equipment_readiness_returns_shared_active_alarm_reason_code));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "DOWNTIME-ALARM-001",
            "WC-OIL",
            now.AddMinutes(-10),
            null,
            EquipmentRuntimeReasonCodes.ActiveAlarm,
            "DEV-OIL-01"));
        await dbContext.SaveChangesAsync();

        var readiness = await new GetMesFoundationReadinessAreaQueryHandler(new MesFoundationReadinessService(dbContext)).Handle(
            new GetMesFoundationReadinessAreaQuery(
                "org-001",
                "env-dev",
                "equipment",
                "SITE-01",
                "LINE-OIL",
                "WC-OIL",
                "FG-OIL",
                "PV-OIL-1",
                now,
                now.AddHours(1)),
            CancellationToken.None);

        var issue = Assert.Single(readiness.Issues);
        Assert.Equal(EquipmentRuntimeReasonCodes.ActiveAlarm, issue.Code);
        Assert.Equal("IndustrialTelemetry", issue.SourceSystem);
        Assert.Equal("DEV-OIL-01", issue.ReferenceDisplayName);
    }

    [Fact]
    public async Task Release_and_start_reuse_quality_and_equipment_readiness_reason_codes()
    {
        var services = CreateServices(nameof(Release_and_start_reuse_quality_and_equipment_readiness_reason_codes));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QUALITY-001", "FG-FSA", null, 10m, 20, now.AddHours(8)));
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-EQUIP-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-EQUIP-001",
            "OP-EQUIP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "DOWNTIME-MAINT-002",
            "WC-FILL",
            now.AddMinutes(-10),
            null,
            EquipmentRuntimeReasonCodes.MaintenanceWindow,
            "ASSET-FILL-01"));
        await dbContext.SaveChangesAsync();

        var qualityException = await Assert.ThrowsAsync<KnownException>(() =>
            new ReleaseWorkOrderCommandHandler(dbContext).Handle(
                new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-QUALITY-001", now.AddMinutes(30)),
                CancellationToken.None));
        Assert.Contains("QUALITY_PLAN_MISSING", qualityException.Message);

        var releaseEquipmentException = await Assert.ThrowsAsync<KnownException>(() =>
            new ReleaseWorkOrderCommandHandler(dbContext).Handle(
                new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-EQUIP-001", now.AddMinutes(30)),
                CancellationToken.None));
        Assert.Contains(EquipmentRuntimeReasonCodes.MaintenanceWindow, releaseEquipmentException.Message);

        var startException = await Assert.ThrowsAsync<KnownException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-EQUIP-10", "start", now.AddMinutes(35)),
                CancellationToken.None));
        Assert.Contains(EquipmentRuntimeReasonCodes.MaintenanceWindow, startException.Message);
    }

    [Fact]
    public async Task Operation_start_rejects_same_equipment_reason_code_used_by_readiness()
    {
        var services = CreateServices(nameof(Operation_start_rejects_same_equipment_reason_code_used_by_readiness));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-EQUIP-RUNTIME-001", "FG-OIL", "PV-OIL-1", 10m, 20, now.AddHours(8));
        workOrder.MarkReleased();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-EQUIP-RUNTIME-001",
            "OP-EQUIP-RUNTIME-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-OIL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "DOWNTIME-MAINT-WINDOW-001",
            "WC-OIL",
            now.AddMinutes(-10),
            null,
            EquipmentRuntimeReasonCodes.MaintenanceWindow,
            "DEV-OIL-01"));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-EQUIP-RUNTIME-10", "start", now.AddMinutes(15)),
                CancellationToken.None));

        Assert.Contains(EquipmentRuntimeReasonCodes.MaintenanceWindow, exception.Message);
    }

    [Fact]
    public async Task Dispatch_rejects_same_equipment_reason_code_used_by_readiness()
    {
        var services = CreateServices(nameof(Dispatch_rejects_same_equipment_reason_code_used_by_readiness));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-DISPATCH-RUNTIME-001", "FG-OIL", "PV-OIL-1", 10m, 20, now.AddHours(8));
        workOrder.MarkReleased();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-DISPATCH-RUNTIME-001",
            "OP-DISPATCH-RUNTIME-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-OIL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "DOWNTIME-DISPATCH-ALARM-001",
            "WC-OIL",
            now.AddMinutes(-10),
            null,
            EquipmentRuntimeReasonCodes.ActiveAlarm,
            "DEV-OIL-01"));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new AssignDispatchTaskCommandHandler(dbContext).Handle(
                new AssignDispatchTaskCommand(
                    "org-001",
                    "env-dev",
                    "OP-DISPATCH-RUNTIME-10",
                    "operator-001",
                    "DEV-OIL-01",
                    "SHIFT-A",
                    now.AddMinutes(15)),
                CancellationToken.None));

        Assert.Contains(EquipmentRuntimeReasonCodes.ActiveAlarm, exception.Message);
    }

    [Fact]
    public async Task Dispatch_without_active_quality_hold_does_not_block_on_missing_quality_plan()
    {
        var services = CreateServices(nameof(Dispatch_without_active_quality_hold_does_not_block_on_missing_quality_plan));
        var now = DateTimeOffset.Parse("2026-07-05T10:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-DISPATCH-NO-PV", "FG-FSA", null, 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-DISPATCH-NO-PV",
            "OP-DISPATCH-NO-PV-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        await dbContext.SaveChangesAsync();

        var response = await new AssignDispatchTaskCommandHandler(dbContext).Handle(
            new AssignDispatchTaskCommand(
                "org-001",
                "env-dev",
                "OP-DISPATCH-NO-PV-10",
                "operator-001",
                null,
                "SHIFT-A",
                now.AddMinutes(15)),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
    }

    [Fact]
    public void Equipment_inspection_required_reason_classifies_as_maintenance()
    {
        var classification = MesReadinessReasonCodes.ClassifyEquipmentReason(EquipmentRuntimeReasonCodes.InspectionRequired);

        Assert.Equal(EquipmentRuntimeReasonCodes.InspectionRequired, classification.Code);
        Assert.Equal("Maintenance", classification.SourceSystem);
    }

    [Fact]
    public void Equipment_no_eligible_substitute_reason_keeps_shared_code()
    {
        var classification = MesReadinessReasonCodes.ClassifyEquipmentReason(EquipmentRuntimeReasonCodes.NoEligibleSubstitute);

        Assert.Equal(EquipmentRuntimeReasonCodes.NoEligibleSubstitute, classification.Code);
        Assert.NotEqual(EquipmentRuntimeReasonCodes.Downtime, classification.Code);
        Assert.Equal("BusinessScheduling", classification.SourceSystem);
    }

    [Fact]
    public async Task Material_readiness_uses_latest_requirement_snapshot_per_material()
    {
        var services = CreateServices(nameof(Material_readiness_uses_latest_requirement_snapshot_per_material));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-SNAPSHOT-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-SNAPSHOT-001",
            "OP-SNAPSHOT-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-SNAPSHOT-001",
            "OP-SNAPSHOT-10",
            "MAT-OIL",
            null,
            requiredQuantity: 10m,
            availableQuantity: 0m,
            stagedQuantity: 0m,
            sourceSystem: "Inventory",
            sourceSnapshotId: "inv-snap-old",
            capturedAtUtc: now));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-SNAPSHOT-001",
            "OP-SNAPSHOT-10",
            "MAT-OIL",
            null,
            requiredQuantity: 10m,
            availableQuantity: 10m,
            stagedQuantity: 0m,
            sourceSystem: "Inventory",
            sourceSnapshotId: "inv-snap-new",
            capturedAtUtc: now.AddMinutes(5)));
        await dbContext.SaveChangesAsync();

        var release = await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
            new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-SNAPSHOT-001", now.AddMinutes(10)),
            CancellationToken.None);
        var readiness = await new GetMaterialReadinessQueryHandler(dbContext).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-SNAPSHOT-001"),
            CancellationToken.None);

        Assert.Equal("Accepted", release.Status);
        Assert.Equal("Ready", readiness.ReadinessStatus);
        var row = Assert.Single(readiness.Items);
        Assert.Equal(10m, row.RequiredQuantity);
        Assert.Equal(10m, row.AvailableQuantity);
        Assert.Equal(0m, row.ShortageQuantity);
    }

    [Fact]
    public async Task Release_work_order_persistently_changes_state_when_readiness_is_ready()
    {
        var services = CreateServices(nameof(Release_work_order_persistently_changes_state_when_readiness_is_ready));
        var now = DateTimeOffset.Parse("2026-05-30T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-RELEASE-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-RELEASE-001",
                "OP-RELEASE-10",
                OperationTaskLifecycleStatus.Queued,
                10,
                "WC-FILL",
                [],
                now,
                TimeSpan.FromMinutes(45),
                null,
                null));
            dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001",
                "env-dev",
                "WO-RELEASE-001",
                "OP-RELEASE-10",
                "MAT-OIL",
                null,
                requiredQuantity: 10m,
                availableQuantity: 10m,
                stagedQuantity: 0m,
                sourceSystem: "Inventory",
                sourceSnapshotId: "inv-ready-001",
                capturedAtUtc: now));
            await dbContext.SaveChangesAsync();

            var response = await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
                new ReleaseWorkOrderCommand("org-001", "env-dev", "WO-RELEASE-001", now.AddMinutes(10)),
                CancellationToken.None);
            await dbContext.SaveChangesAsync();

            Assert.Equal("Accepted", response.Status);
        }

        using var recreatedScope = services.CreateScope();
        var recreatedDbContext = recreatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = await recreatedDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-RELEASE-001");

        Assert.Equal(WorkOrder.ReleasedStatus, workOrder.Status);
        Assert.Single(await recreatedDbContext.OperationTasks.Where(x => x.WorkOrderId == "WO-RELEASE-001").ToArrayAsync());
    }

    [Fact]
    public async Task Dispatch_assignment_persists_shift_operator_device_and_planned_time_facts()
    {
        var services = CreateServices(nameof(Dispatch_assignment_persists_shift_operator_device_and_planned_time_facts));
        var now = DateTimeOffset.Parse("2026-05-30T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-DISPATCH-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-DISPATCH-001",
                "OP-DISPATCH-10",
                OperationTaskLifecycleStatus.Queued,
                10,
                "WC-FILL",
                [],
                now,
                TimeSpan.FromMinutes(45),
                null,
                null));
            await dbContext.SaveChangesAsync();

            await new AssignDispatchTaskCommandHandler(dbContext).Handle(
                new AssignDispatchTaskCommand("org-001", "env-dev", "OP-DISPATCH-10", "person-001", "device-fill-01", "shift-a", now.AddMinutes(5), "user:dispatcher-001"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync();

            dbContext.ChangeTracker.Clear();
            var persistedAssignment = await dbContext.OperationTasks.SingleAsync(
                x => x.OperationTaskIdValue == "OP-DISPATCH-10");
            Assert.Equal(1, persistedAssignment.ManualDispatchRevision);
            Assert.True(persistedAssignment.HasActiveManualDispatch);

            var list = await new ListDispatchTasksQueryHandler(dbContext)
                .Handle(new ListDispatchTasksQuery("org-001", "env-dev", null), CancellationToken.None);

            var row = Assert.Single(list.Items);
            Assert.Equal("OP-DISPATCH-10", row.OperationTaskId);
            Assert.Equal("person-001", row.AssignedUserId);
            Assert.Equal("device-fill-01", row.DeviceAssetId);
            Assert.Equal("shift-a", row.ShiftId);
            Assert.Equal(now, row.PlannedStartUtc);

            await new AssignDispatchTaskCommandHandler(dbContext).Handle(
                new AssignDispatchTaskCommand("org-001", "env-dev", "OP-DISPATCH-10", "person-001", null, "shift-a", now.AddMinutes(10), "user:dispatcher-001"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync();

            dbContext.ChangeTracker.Clear();
            var persistedClear = await dbContext.OperationTasks.SingleAsync(
                x => x.OperationTaskIdValue == "OP-DISPATCH-10");
            Assert.Equal(2, persistedClear.ManualDispatchRevision);
            Assert.False(persistedClear.HasActiveManualDispatch);
        }
    }

    [Fact]
    public async Task Legacy_unknown_device_reload_emits_revocation_and_persists_monotonic_revision()
    {
        var services = CreateServices(nameof(Legacy_unknown_device_reload_emits_revocation_and_persists_monotonic_revision));
        var now = DateTimeOffset.Parse("2026-07-15T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = OperationTask.Create(
            "org-001", "env-dev", "WO-LEGACY-001", "OP-LEGACY-10",
            OperationTaskLifecycleStatus.Queued, 10, "WC-FILL", [], now,
            TimeSpan.FromMinutes(45), null, null);
        task.ApplyScheduleAssignment("WC-FILL", "device-legacy-01", now, now.AddMinutes(45), now);
        dbContext.OperationTasks.Add(task);
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var upgraded = await dbContext.OperationTasks.SingleAsync(
            x => x.OperationTaskIdValue == "OP-LEGACY-10");
        Assert.Equal(0, upgraded.ManualDispatchRevision);
        Assert.False(upgraded.HasActiveManualDispatch);
        Assert.Equal("device-legacy-01", upgraded.DeviceAssetId);

        upgraded.Assign(null, null, null, now.AddMinutes(1), "user:dispatcher-001");
        var cleared = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(upgraded.GetDomainEvents()));
        Assert.Equal(1, cleared.Dispatch.DispatchRevision);
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var persistedClear = await dbContext.OperationTasks.SingleAsync(
            x => x.OperationTaskIdValue == "OP-LEGACY-10");
        Assert.Equal(1, persistedClear.ManualDispatchRevision);
        Assert.False(persistedClear.HasActiveManualDispatch);

        persistedClear.Assign(null, "device-new-02", null, now.AddMinutes(2), "user:dispatcher-001");
        Assert.Equal(2, persistedClear.ManualDispatchRevision);
        Assert.True(persistedClear.HasActiveManualDispatch);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Operation_lifecycle_rejects_complete_before_start_and_pause_after_completion()
    {
        var services = CreateServices(nameof(Operation_lifecycle_rejects_complete_before_start_and_pause_after_completion));
        var now = DateTimeOffset.Parse("2026-05-30T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-LIFE-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-LIFE-001",
            "OP-LIFE-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-LIFE-001",
            "OP-LIFE-10",
            "MAT-LIFE",
            null,
            requiredQuantity: 1m,
            availableQuantity: 1m,
            stagedQuantity: 0m,
            sourceSystem: "Inventory",
            sourceSnapshotId: "inv-life-ready",
            capturedAtUtc: now));
        await dbContext.SaveChangesAsync();

        var handler = new ChangeOperationTaskStateCommandHandler(dbContext);

        // Invalid lifecycle transitions are domain-rule violations and must surface as a clean KnownException
        // business error, not an unhandled InvalidOperationException (which would become an HTTP 500).
        await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-LIFE-10", "complete", now.AddMinutes(1)), CancellationToken.None));

        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-LIFE-10", "start", now.AddMinutes(2)), CancellationToken.None);
        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-LIFE-10", "complete", now.AddMinutes(45)), CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-LIFE-10", "pause", now.AddMinutes(46)), CancellationToken.None));
    }

    [Fact]
    public async Task Production_report_that_completes_operation_persists_operation_completion()
    {
        var services = CreateServices(nameof(Production_report_that_completes_operation_persists_operation_completion));
        var now = DateTimeOffset.Parse("2026-05-30T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-REPORT-COMPLETE-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-REPORT-COMPLETE-001",
                "OP-REPORT-COMPLETE-10",
                OperationTaskLifecycleStatus.InProgress,
                10,
                "WC-FILL",
                [],
                now,
                TimeSpan.FromMinutes(45),
                now,
                null));
            var materialIssue = MaterialIssueRequest.Create(
                "org-001",
                "env-dev",
                "MIR-REPORT-COMPLETE-001",
                "WO-REPORT-COMPLETE-001",
                "OP-REPORT-COMPLETE-10",
                "MAT-SCRAP",
                "PCS",
                10m,
                now.AddMinutes(-10));
            materialIssue.ConfirmLineSideReceipt(now.AddMinutes(-9), 10m, "LOT-SCRAP");
            materialIssue.ClearDomainEvents();
            dbContext.MaterialIssueRequests.Add(materialIssue);
            await dbContext.SaveChangesAsync();

            await new RecordProductionReportCommandHandler(dbContext).Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-REPORT-COMPLETE-001",
                    "OP-REPORT-COMPLETE-10",
                    9m,
                    1m,
                    true,
                    now.AddMinutes(40),
                    "report-complete-001",
                    [new ConsumedMaterialLotInput("MAT-SCRAP", "LOT-SCRAP", 1m, "MIR-REPORT-COMPLETE-001")]),
                CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        using var recreatedScope = services.CreateScope();
        var task = await recreatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .OperationTasks
            .SingleAsync(x => x.OperationTaskIdValue == "OP-REPORT-COMPLETE-10");

        Assert.Equal(OperationTaskLifecycleStatus.Completed, task.Status);
        Assert.Equal(now.AddMinutes(40), task.ExistingEndUtc);
    }

    [Fact]
    public async Task Batch_traceability_reads_durable_material_consumption_facts()
    {
        var services = CreateServices(nameof(Batch_traceability_reads_durable_material_consumption_facts));
        var now = DateTimeOffset.Parse("2026-05-30T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-BATCH-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-BATCH-001",
                "OP-BATCH-10",
                OperationTaskLifecycleStatus.InProgress,
                10,
                "WC-FILL",
                [],
                now,
                TimeSpan.FromMinutes(45),
                now,
                null));
            dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
                "org-001",
                "env-dev",
                "MIR-BATCH-001",
                "WO-BATCH-001",
                "OP-BATCH-10",
                "MAT-OIL",
                "L",
                4m,
                now.AddMinutes(5)));
            await dbContext.SaveChangesAsync();
            var request = await dbContext.MaterialIssueRequests.SingleAsync();
            request.ConfirmLineSideReceipt(now.AddMinutes(10), materialLotId: "LOT-BATCH-A");
            await dbContext.SaveChangesAsync();

            await new RecordProductionReportCommandHandler(dbContext).Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-BATCH-001",
                    "OP-BATCH-10",
                    9m,
                    1m,
                    true,
                    now.AddMinutes(30),
                    "report-batch-001",
                    [new ConsumedMaterialLotInput("MAT-OIL", "LOT-BATCH-A", 4m, "MIR-BATCH-001")]),
                CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        using var recreatedScope = services.CreateScope();
        var traceability = await new GetBatchTraceabilityQueryHandler(
            recreatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
            .Handle(new GetBatchTraceabilityQuery("org-001", "env-dev", "LOT-BATCH-A"), CancellationToken.None);

        Assert.Contains(traceability.Nodes, x => x.NodeId == "LOT-BATCH-A" && x.NodeType == "MaterialLot");
        Assert.Contains(traceability.Nodes, x => x.NodeId == "WO-BATCH-001" && x.NodeType == "WorkOrder");
        Assert.Contains(traceability.Edges, x => x.RelationType == "consumed-by-report");
    }

    [Fact]
    public async Task Work_order_traceability_returns_unknown_node_when_work_order_is_missing()
    {
        var services = CreateServices(nameof(Work_order_traceability_returns_unknown_node_when_work_order_is_missing));

        using var scope = services.CreateScope();
        var traceability = await new GetWorkOrderTraceabilityQueryHandler(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
            .Handle(new GetWorkOrderTraceabilityQuery("org-001", "env-dev", "WO-MISSING"), CancellationToken.None);

        var node = Assert.Single(traceability.Nodes);
        Assert.Equal("WO-MISSING", node.NodeId);
        Assert.Equal("WorkOrder", node.NodeType);
        Assert.Equal("Unknown", node.Status);
        Assert.Empty(traceability.Edges);
    }

    [Fact]
    public async Task Partial_line_side_receipt_keeps_remaining_shortage_until_full_quantity_is_received()
    {
        var services = CreateServices(nameof(Partial_line_side_receipt_keeps_remaining_shortage_until_full_quantity_is_received));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-PARTIAL-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-PARTIAL-001",
            "OP-PARTIAL-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-PARTIAL-001",
            "OP-PARTIAL-10",
            "MAT-OIL",
            null,
            requiredQuantity: 10m,
            availableQuantity: 0m,
            stagedQuantity: 0m,
            sourceSystem: "Inventory",
            sourceSnapshotId: "inv-snap-001",
            capturedAtUtc: now));
        dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-PARTIAL-001",
            "WO-PARTIAL-001",
            "OP-PARTIAL-10",
            "MAT-OIL",
            "L",
            10m,
            now.AddMinutes(1)));
        await dbContext.SaveChangesAsync();

        await new ConfirmLineSideMaterialReceiptCommandHandler(dbContext).Handle(
            new ConfirmLineSideMaterialReceiptCommand("org-001", "env-dev", "MIR-PARTIAL-001", now.AddMinutes(5), 4m, "LOT-OIL-A"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var readiness = await new GetMaterialReadinessQueryHandler(dbContext).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-PARTIAL-001"),
            CancellationToken.None);

        var row = Assert.Single(readiness.Items);
        Assert.Equal("Blocked", readiness.ReadinessStatus);
        Assert.Equal(4m, row.ReceivedQuantity);
        Assert.Equal(6m, row.ShortageQuantity);
        Assert.Equal(MaterialIssueRequest.PartiallyReceivedStatus, await dbContext.MaterialIssueRequests.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Production_report_can_reference_consumed_material_lots_for_traceability()
    {
        var services = CreateServices(nameof(Production_report_can_reference_consumed_material_lots_for_traceability));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-TRACE-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-TRACE-001",
                "OP-TRACE-10",
                OperationTaskLifecycleStatus.InProgress,
                10,
                "WC-FILL",
                [],
                now,
                TimeSpan.FromMinutes(45),
                now,
                now.AddMinutes(45)));
            dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
                "org-001",
                "env-dev",
                "MIR-TRACE-001",
                "WO-TRACE-001",
                "OP-TRACE-10",
                "MAT-OIL",
                "L",
                4m,
                now.AddMinutes(5)));
            await dbContext.SaveChangesAsync();
        }

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var request = await dbContext.MaterialIssueRequests.SingleAsync();
            request.ConfirmLineSideReceipt(now.AddMinutes(10), materialLotId: "LOT-OIL-A");
            await dbContext.SaveChangesAsync();

            var handler = new RecordProductionReportCommandHandler(dbContext);
            await handler.Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-TRACE-001",
                    "OP-TRACE-10",
                    9m,
                    1m,
                    true,
                    now.AddMinutes(30),
                    "report-001",
                    [new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 4m, request.RequestNo)]),
                CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        using var recreatedScope = services.CreateScope();
        var traceability = await new GetMaterialLotTraceabilityQueryHandler(
            recreatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
            .Handle(new GetMaterialLotTraceabilityQuery("org-001", "env-dev", "LOT-OIL-A"), CancellationToken.None);

        Assert.Contains(traceability.Nodes, x => x.NodeId == "LOT-OIL-A" && x.NodeType == "MaterialLot");
        Assert.Contains(traceability.Nodes, x => x.NodeType == "ProductionReport");
        Assert.Contains(traceability.Nodes, x => x.NodeId == "WO-TRACE-001" && x.NodeType == "WorkOrder");
        Assert.Contains(traceability.Edges, x => x.RelationType == "consumed-by-report");
    }

    [Fact]
    public async Task Production_report_material_consumption_requires_received_line_side_request_reference()
    {
        var services = CreateServices(nameof(Production_report_material_consumption_requires_received_line_side_request_reference));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-CONSUME-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-CONSUME-001",
            "OP-CONSUME-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            now.AddMinutes(45)));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new RecordProductionReportCommandHandler(dbContext).Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-CONSUME-001",
                    "OP-CONSUME-10",
                    1m,
                    0m,
                    false,
                    now.AddMinutes(30),
                    "report-without-material-issue",
                    [new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 1m, "")]),
                CancellationToken.None));

        Assert.Contains("线边领料申请", exception.Message);
    }

    [Fact]
    public async Task Production_report_rejects_cumulative_consumption_that_exceeds_received_line_side_quantity()
    {
        var services = CreateServices(nameof(Production_report_rejects_cumulative_consumption_that_exceeds_received_line_side_quantity));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-CUM-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-CUM-001",
            "OP-CUM-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            now.AddMinutes(45)));
        dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-CUM-001",
            "WO-CUM-001",
            "OP-CUM-10",
            "MAT-OIL",
            "L",
            10m,
            now.AddMinutes(1)));
        await dbContext.SaveChangesAsync();
        var request = await dbContext.MaterialIssueRequests.SingleAsync();
        request.ConfirmLineSideReceipt(now.AddMinutes(5), 10m, "LOT-OIL-A");
        await dbContext.SaveChangesAsync();

        var handler = new RecordProductionReportCommandHandler(dbContext);
        await handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-CUM-001",
                "OP-CUM-10",
                6m,
                0m,
                false,
                now.AddMinutes(30),
                "report-cumulative-first",
                [new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 6m, "MIR-CUM-001")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-CUM-001",
                    "OP-CUM-10",
                    5m,
                    0m,
                    false,
                    now.AddMinutes(40),
                    "report-cumulative-second",
                    [new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 5m, "MIR-CUM-001")]),
                CancellationToken.None));

        Assert.Contains("累计耗料", exception.Message);
    }

    [Fact]
    public async Task Production_report_rejects_line_side_request_from_another_work_order_or_operation()
    {
        var services = CreateServices(nameof(Production_report_rejects_line_side_request_from_another_work_order_or_operation));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-REPORT-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-REPORT-001",
            "OP-REPORT-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            now.AddMinutes(45)));
        var otherRequest = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-OTHER-WO",
            "WO-OTHER-001",
            "OP-OTHER-10",
            "MAT-OIL",
            "L",
            10m,
            now.AddMinutes(1));
        otherRequest.ConfirmLineSideReceipt(now.AddMinutes(5), 10m, "LOT-OIL-A");
        dbContext.MaterialIssueRequests.Add(otherRequest);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new RecordProductionReportCommandHandler(dbContext).Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-REPORT-001",
                    "OP-REPORT-10",
                    1m,
                    0m,
                    false,
                    now.AddMinutes(30),
                    "report-cross-work-order",
                    [new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 1m, "MIR-OTHER-WO")]),
                CancellationToken.None));

        Assert.Contains("当前工单或工序", exception.Message);
    }

    [Fact]
    public async Task Production_report_rejects_operation_task_from_another_work_order()
    {
        var services = CreateServices(nameof(Production_report_rejects_operation_task_from_another_work_order));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-REPORT-A", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-REPORT-B", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-REPORT-B",
            "OP-REPORT-B10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            now.AddMinutes(45)));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new RecordProductionReportCommandHandler(dbContext).Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-REPORT-A",
                    "OP-REPORT-B10",
                    1m,
                    0m,
                    true,
                    now.AddMinutes(30),
                    "report-wrong-operation"),
                CancellationToken.None));

        Assert.Contains("工序任务不存在或不属于当前工单", exception.Message);
    }

    [Fact]
    public async Task Production_report_idempotency_fingerprint_includes_consumed_material_lots()
    {
        var services = CreateServices(nameof(Production_report_idempotency_fingerprint_includes_consumed_material_lots));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-IDEMP-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-IDEMP-001",
            "OP-IDEMP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            now.AddMinutes(45)));
        var lotARequest = MaterialIssueRequest.Create("org-001", "env-dev", "MIR-IDEMP-A", "WO-IDEMP-001", "OP-IDEMP-10", "MAT-OIL", "L", 10m, now.AddMinutes(1));
        lotARequest.ConfirmLineSideReceipt(now.AddMinutes(5), 10m, "LOT-OIL-A");
        var lotBRequest = MaterialIssueRequest.Create("org-001", "env-dev", "MIR-IDEMP-B", "WO-IDEMP-001", "OP-IDEMP-10", "MAT-OIL", "L", 10m, now.AddMinutes(2));
        lotBRequest.ConfirmLineSideReceipt(now.AddMinutes(6), 10m, "LOT-OIL-B");
        dbContext.MaterialIssueRequests.AddRange(lotARequest, lotBRequest);
        await dbContext.SaveChangesAsync();

        var handler = new RecordProductionReportCommandHandler(dbContext);
        await handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-IDEMP-001",
                "OP-IDEMP-10",
                1m,
                0m,
                false,
                now.AddMinutes(30),
                "report-idempotent-lot",
                [new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 1m, "MIR-IDEMP-A")]),
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            handler.Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-IDEMP-001",
                    "OP-IDEMP-10",
                    1m,
                    0m,
                    false,
                    now.AddMinutes(30),
                    "report-idempotent-lot",
                    [new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-B", 1m, "MIR-IDEMP-B")]),
                CancellationToken.None));
    }

    [Fact]
    public async Task Production_report_rejects_duplicate_consumed_material_lot_for_same_report()
    {
        var services = CreateServices(nameof(Production_report_rejects_duplicate_consumed_material_lot_for_same_report));
        var now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-DUP-001", "FG-FSA", "PV-FSA-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-DUP-001",
            "OP-DUP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            now.AddMinutes(45)));
        dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-DUP-001",
            "WO-DUP-001",
            "OP-DUP-10",
            "MAT-OIL",
            "L",
            10m,
            now.AddMinutes(1)));
        await dbContext.SaveChangesAsync();
        var request = await dbContext.MaterialIssueRequests.SingleAsync();
        request.ConfirmLineSideReceipt(now.AddMinutes(5), 10m, "LOT-OIL-A");
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new RecordProductionReportCommandHandler(dbContext).Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-DUP-001",
                    "OP-DUP-10",
                    1m,
                    0m,
                    false,
                    now.AddMinutes(30),
                    "report-duplicate-lot",
                    [
                        new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 1m, "MIR-DUP-001"),
                        new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 1m, "MIR-DUP-001"),
                    ]),
                CancellationToken.None));

        Assert.Contains("重复", exception.Message);
    }

    [Fact]
    public async Task Reverse_production_report_restores_progress_consumption_and_traceability_to_initial_state()
    {
        var services = CreateServices(nameof(Reverse_production_report_restores_progress_consumption_and_traceability_to_initial_state));
        var now = DateTimeOffset.Parse("2026-07-03T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var codingService = new MesCodingService();
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-REV-001", "SKU-FG-REV", "PV-REV-1", 10m, 20, now.AddHours(8), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(now);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-REV-001",
            "OP-REV-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            null));
        var materialIssue = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-REV-001",
            "WO-REV-001",
            "OP-REV-10",
            "MAT-OIL",
            "L",
            6m,
            now.AddMinutes(1));
        materialIssue.ConfirmLineSideReceipt(now.AddMinutes(5), 6m, "LOT-OIL-REV");
        materialIssue.ClearDomainEvents();
        dbContext.MaterialIssueRequests.Add(materialIssue);
        await dbContext.SaveChangesAsync();

        var reportResult = await new RecordProductionReportCommandHandler(dbContext, codingService).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-REV-001",
                "OP-REV-10",
                4m,
                1m,
                true,
                now.AddMinutes(30),
                "report-rev-001",
                [new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-REV", 3m, "MIR-REV-001")],
                ReworkQuantity: 2m,
                ProducedLotNo: "LOT-FG-REV"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var receiptResult = await new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext, codingService).Handle(
            new CreateFinishedGoodsReceiptRequestCommand(
                "org-001",
                "env-dev",
                "WO-REV-001",
                "SKU-FG-REV",
                4m,
                "PCS",
                now.AddMinutes(40),
                UnitCost: 12.34m,
                IdempotencyKey: "receipt-rev-001",
                ProducedLotNo: "LOT-FG-REV"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var reversal = await new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
            new ReverseProductionReportCommand(
                "org-001",
                "env-dev",
                reportResult.ReportNo,
                "wrong quantity",
                now.AddMinutes(50),
                "operator-1",
                "reverse-rev-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.NotEqual(reportResult.ReportNo, reversal.ReportNo);
        var persistedWorkOrder = await dbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-REV-001");
        Assert.Equal(0m, persistedWorkOrder.CompletedQuantity);
        Assert.Equal(0m, persistedWorkOrder.ScrapQuantity);
        Assert.Equal(WorkOrder.StartedStatus, persistedWorkOrder.Status);
        var operationTask = await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-REV-10");
        Assert.Equal(OperationTaskLifecycleStatus.InProgress, operationTask.Status);
        Assert.Null(operationTask.ExistingEndUtc);

        var reversalReport = await dbContext.ProductionReports.SingleAsync(x => x.ReportNo == reversal.ReportNo);
        Assert.Equal(reportResult.ReportNo, reversalReport.ReversedReportNo);
        Assert.Equal("operator-1", reversalReport.ReversedBy);
        Assert.Equal(-4m, reversalReport.GoodQuantity);
        Assert.Equal(-1m, reversalReport.ScrapQuantity);
        Assert.Equal(-2m, reversalReport.ReworkQuantity);
        Assert.Equal(reportResult.ReportNo, reversal.OriginalReportNo);

        var netConsumed = await dbContext.ProductionReportMaterialConsumptions
            .Where(x => x.MaterialLotId == "LOT-OIL-REV")
            .SumAsync(x => x.ConsumedQuantity);
        Assert.Equal(0m, netConsumed);
        Assert.Empty(await dbContext.OutputLotGenealogies.Where(x => x.ProducedLotNo == "LOT-FG-REV").ToArrayAsync());
        Assert.Equal(
            FinishedGoodsReceiptRequest.CancelledStatus,
            (await dbContext.FinishedGoodsReceiptRequests.SingleAsync(x => x.RequestNo == receiptResult.RequestNo)).Status);

        var traceability = await new GetMaterialLotTraceabilityQueryHandler(dbContext)
            .Handle(new GetMaterialLotTraceabilityQuery("org-001", "env-dev", "LOT-OIL-REV"), CancellationToken.None);
        Assert.Single(traceability.Nodes);
        Assert.Equal("Unknown", traceability.Nodes.Single().Status);
        Assert.Empty(traceability.Edges);
    }

    // 跨边界事实(MAN-444/#798 review):冲销 handler fingerprint = ReportNo + Reason + ReversedAtUtc,
    // ReverseProductionReportEndpoint 对缺失 reversedAtUtc 每次补新的 TimeProvider.GetUtcNow()。故幂等重放
    // 要求前端**同时冻结** idempotencyKey 与 reversedAtUtc:同 key + 同 reversedAtUtc 才走 IsIdempotentReplay;
    // 同 key + 不同 reversedAtUtc 会因 fingerprint 变被 CodeAllocator 判为冲突(KnownException),而非重放。
    [Fact]
    public async Task Reverse_production_report_idempotent_replay_requires_stable_reversed_at()
    {
        var services = CreateServices(nameof(Reverse_production_report_idempotent_replay_requires_stable_reversed_at));
        var now = DateTimeOffset.Parse("2026-07-03T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var codingService = new MesCodingService();
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-REV-IDEM", "SKU-IDEM", "PV-IDEM", 10m, 20, now.AddHours(8), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(now);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-REV-IDEM",
            "OP-IDEM-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            null));
        await dbContext.SaveChangesAsync();

        var reportResult = await new RecordProductionReportCommandHandler(dbContext, codingService).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-REV-IDEM",
                "OP-IDEM-10",
                4m,
                0m,
                true,
                now.AddMinutes(30),
                "report-rev-idem"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var reversedAt = now.AddMinutes(50);
        var first = await new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
            new ReverseProductionReportCommand("org-001", "env-dev", reportResult.ReportNo, "mis-report", reversedAt, "  operator-1  ", "reverse-idem-key"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        // 同 key + 同 reversedAtUtc → 幂等重放:返回同一冲销单,不产生第二条冲销记录,工单不二次回退
        var replay = await new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
            new ReverseProductionReportCommand("org-001", "env-dev", reportResult.ReportNo, "mis-report", reversedAt, "operator-1", "reverse-idem-key"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(first.ReportNo, replay.ReportNo);
        Assert.Equal(1, await dbContext.ProductionReports.CountAsync(x => x.ReversedReportNo == reportResult.ReportNo));
        var workOrderAfterReplay = await dbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-REV-IDEM");
        Assert.Equal(0m, workOrderAfterReplay.CompletedQuantity);

        // 同 key + **不同** reversedAtUtc → fingerprint 变 → CodeAllocator 判冲突(KnownException),不会误重放
        await Assert.ThrowsAsync<KnownException>(() =>
            new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
                new ReverseProductionReportCommand("org-001", "env-dev", reportResult.ReportNo, "mis-report", reversedAt.AddSeconds(1), "operator-1", "reverse-idem-key"),
                CancellationToken.None));

        // 同 key + 同 reversedAtUtc + **不同 reason** → fingerprint 同样变 → 冲突。故前端必须把 reason 也冻结进意图。
        await Assert.ThrowsAsync<KnownException>(() =>
            new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
                new ReverseProductionReportCommand("org-001", "env-dev", reportResult.ReportNo, "wrong-quantity", reversedAt, "operator-1", "reverse-idem-key"),
                CancellationToken.None));

        await Assert.ThrowsAsync<KnownException>(() =>
            new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
                new ReverseProductionReportCommand("org-001", "env-dev", reportResult.ReportNo, "mis-report", reversedAt, "operator-2", "reverse-idem-key"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Reverse_production_report_rejects_closed_work_order_and_duplicate_reversal()
    {
        var services = CreateServices(nameof(Reverse_production_report_rejects_closed_work_order_and_duplicate_reversal));
        var now = DateTimeOffset.Parse("2026-07-03T08:00:00Z");

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var codingService = new MesCodingService();
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-REV-CLOSED", "SKU-FG-REV", "PV-REV-1", 5m, 20, now.AddHours(8), "PCS"));
        var workOrder = dbContext.WorkOrders.Local.Single();
        workOrder.MarkReleased();
        workOrder.Start(now);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-REV-CLOSED",
            "OP-REV-CLOSED",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            null));
        await dbContext.SaveChangesAsync();

        var report = await new RecordProductionReportCommandHandler(dbContext, codingService).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-REV-CLOSED",
                "OP-REV-CLOSED",
                5m,
                0m,
                true,
                now.AddMinutes(30),
                "report-rev-closed",
                ProducedLotNo: "LOT-FG-CLOSED"),
            CancellationToken.None);
        workOrder.Close(now.AddMinutes(40));
        await dbContext.SaveChangesAsync();

        var closedException = await Assert.ThrowsAsync<KnownException>(() =>
            new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
                new ReverseProductionReportCommand("org-001", "env-dev", report.ReportNo, "closed", now.AddMinutes(50), "operator-1", "reverse-closed"),
                CancellationToken.None));
        Assert.Contains("已关闭", closedException.Message);

        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-REV-DUP", "SKU-FG-REV", "PV-REV-1", 5m, 20, now.AddHours(9), "PCS"));
        var duplicateWorkOrder = dbContext.WorkOrders.Local.Single(x => x.WorkOrderIdValue == "WO-REV-DUP");
        duplicateWorkOrder.MarkReleased();
        duplicateWorkOrder.Start(now);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-REV-DUP",
            "OP-REV-DUP",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-FILL",
            [],
            now,
            TimeSpan.FromMinutes(45),
            now,
            null));
        await dbContext.SaveChangesAsync();
        var duplicateReport = await new RecordProductionReportCommandHandler(dbContext, codingService).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-REV-DUP",
                "OP-REV-DUP",
                5m,
                0m,
                true,
                now.AddMinutes(45),
                "report-rev-dup",
                ProducedLotNo: "LOT-FG-DUP"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
            new ReverseProductionReportCommand("org-001", "env-dev", duplicateReport.ReportNo, "first reversal", now.AddMinutes(60), "operator-1", "reverse-first"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var duplicateException = await Assert.ThrowsAsync<KnownException>(() =>
            new ReverseProductionReportCommandHandler(dbContext, codingService).Handle(
                new ReverseProductionReportCommand("org-001", "env-dev", duplicateReport.ReportNo, "duplicate reversal", now.AddMinutes(70), "operator-2", "reverse-second"),
                CancellationToken.None));
        Assert.Contains("已冲销", duplicateException.Message);
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
            new ListMesWorkOrdersQuery("org-a", "env-dev", null, Take: 100),
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

    private sealed class FakeMesMaterialRequirementSnapshotProvider(MesMaterialRequirementSnapshotResult result)
        : IMesMaterialRequirementSnapshotProvider
    {
        public static readonly FakeMesMaterialRequirementSnapshotProvider Missing = new(MesMaterialRequirementSnapshotResult.Missing("fake-missing"));

        public List<MesMaterialRequirementSnapshotRequest> Requests { get; } = [];

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private static InspectionResultIntegrationEvent CreateInspectionResultEvent(
        string eventId,
        string eventType,
        string inspectionRecordId,
        string workOrderId,
        DateTimeOffset occurredAtUtc,
        string? dispositionReason = null)
    {
        var result = eventType == QualityIntegrationEventTypes.InspectionPassed
            ? "passed"
            : eventType == QualityIntegrationEventTypes.InspectionConditionalReleased
                ? "conditional-release"
                : "rejected";
        return new InspectionResultIntegrationEvent(
            eventId,
            eventType,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            $"corr-{eventId}",
            $"cause-{eventId}",
            "org-001",
            "env-dev",
            "quality",
            $"quality:inspection-result:org-001:env-dev:{inspectionRecordId}:{eventType}",
            new InspectionResultPayload(
                inspectionRecordId,
                "PLAN-QH-001",
                "in-process",
                QualityIntegrationEventSources.BusinessMes,
                workOrderId,
                "FG-FSA",
                10m,
                result,
                dispositionReason ?? (eventType == QualityIntegrationEventTypes.InspectionRejected ? "critical-defect" : null),
                [],
                occurredAtUtc));
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
