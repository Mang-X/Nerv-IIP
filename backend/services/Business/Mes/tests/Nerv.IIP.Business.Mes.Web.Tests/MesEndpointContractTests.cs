using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Auth;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesEndpointContractTests
{
    [Fact]
    public void MesEndpointContracts_ExposeRescheduleAndRushOrderRoutes()
    {
        Assert.Equal(42, MesEndpointContracts.All.Count);
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/foundation-readiness/{areaCode}"
            && x.PermissionCode == MesPermissionCodes.FoundationRead
            && x.OperationId == "getBusinessMesFoundationReadinessArea");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/overview"
            && x.PermissionCode == MesPermissionCodes.OverviewRead
            && x.OperationId == "getBusinessMesOverview");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/production-plans"
            && x.PermissionCode == MesPermissionCodes.PlansRead
            && x.OperationId == "listBusinessMesProductionPlans");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/production-plans/{productionPlanId}/readiness"
            && x.PermissionCode == MesPermissionCodes.PlansRead
            && x.OperationId == "getBusinessMesProductionPlanReadiness");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/production-plans/{productionPlanId}/work-orders"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "convertBusinessMesPlanToWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/schedules/run"
            && x.PermissionCode == MesPermissionCodes.SchedulesManage
            && x.OperationId == "runBusinessMesSchedule");

        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/rush"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "createBusinessMesRushWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/work-orders"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersRead
            && x.OperationId == "listBusinessMesWorkOrders");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersRead
            && x.OperationId == "getBusinessMesWorkOrderDetail");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/release"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "releaseBusinessMesWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/close"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "closeBusinessMesWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/hold"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "holdBusinessMesWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/cancel"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "cancelBusinessMesWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/material-readiness"
            && x.PermissionCode == MesPermissionCodes.MaterialsRead
            && x.OperationId == "getBusinessMesMaterialReadiness");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/material-issue-requests"
            && x.PermissionCode == MesPermissionCodes.MaterialsManage
            && x.OperationId == "createBusinessMesMaterialIssueRequest");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/material-issue-requests"
            && x.PermissionCode == MesPermissionCodes.MaterialsRead
            && x.OperationId == "listBusinessMesMaterialIssueRequests");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/material-issue-requests/{requestId}/line-side-receipts"
            && x.PermissionCode == MesPermissionCodes.MaterialsManage
            && x.OperationId == "confirmBusinessMesLineSideMaterialReceipt");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/material-issue-requests/{requestId}/line-side-returns"
            && x.PermissionCode == MesPermissionCodes.MaterialsManage
            && x.OperationId == "returnBusinessMesLineSideMaterial");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/dispatch-tasks"
            && x.PermissionCode == MesPermissionCodes.DispatchRead
            && x.OperationId == "listBusinessMesDispatchTasks");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign"
            && x.PermissionCode == MesPermissionCodes.DispatchManage
            && x.OperationId == "assignBusinessMesDispatchTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/operation-tasks"
            && x.PermissionCode == MesPermissionCodes.OperationsRead
            && x.OperationId == "listBusinessMesOperationTasks");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/start"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "startBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/pause"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "pauseBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/resume"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "resumeBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/complete"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "completeBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/wip"
            && x.PermissionCode == MesPermissionCodes.OperationsRead
            && x.OperationId == "getBusinessMesWipSummary");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/production-reports"
            && x.PermissionCode == MesPermissionCodes.ReportingWrite
            && x.OperationId == "recordBusinessMesProductionReport");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/production-reports"
            && x.PermissionCode == MesPermissionCodes.ReportingRead
            && x.OperationId == "listBusinessMesProductionReports");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/defects"
            && x.PermissionCode == MesPermissionCodes.QualityWrite
            && x.OperationId == "recordBusinessMesDefect");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/related-quality-items"
            && x.PermissionCode == MesPermissionCodes.QualityRead
            && x.OperationId == "listBusinessMesRelatedQualityItems");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/finished-goods-receipt-requests"
            && x.PermissionCode == MesPermissionCodes.ReceiptsManage
            && x.OperationId == "createBusinessMesFinishedGoodsReceiptRequest");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/finished-goods-receipt-requests"
            && x.PermissionCode == MesPermissionCodes.ReceiptsRead
            && x.OperationId == "listBusinessMesFinishedGoodsReceiptRequests");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/capacity-impacts"
            && x.PermissionCode == MesPermissionCodes.CapacityRead
            && x.OperationId == "listBusinessMesCapacityImpacts");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/downtime-events"
            && x.PermissionCode == MesPermissionCodes.DowntimeRead
            && x.OperationId == "listBusinessMesDowntimeEvents");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/downtime-events"
            && x.PermissionCode == MesPermissionCodes.DowntimeManage
            && x.OperationId == "recordBusinessMesDowntimeEvent");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/downtime-events/{downtimeEventId}/recover"
            && x.PermissionCode == MesPermissionCodes.DowntimeManage
            && x.OperationId == "confirmBusinessMesDowntimeRecovery");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/shift-handovers"
            && x.PermissionCode == MesPermissionCodes.HandoversRead
            && x.OperationId == "listBusinessMesShiftHandovers");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/shift-handovers"
            && x.PermissionCode == MesPermissionCodes.HandoversManage
            && x.OperationId == "createBusinessMesShiftHandover");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/shift-handovers/{handoverId}/accept"
            && x.PermissionCode == MesPermissionCodes.HandoversManage
            && x.OperationId == "acceptBusinessMesShiftHandover");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/traceability/work-orders/{workOrderId}"
            && x.PermissionCode == MesPermissionCodes.TraceabilityRead
            && x.OperationId == "getBusinessMesWorkOrderTraceability");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/traceability/batches/{batchOrSerial}"
            && x.PermissionCode == MesPermissionCodes.TraceabilityRead
            && x.OperationId == "getBusinessMesBatchTraceability");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/traceability/material-lots/{materialLotId}"
            && x.PermissionCode == MesPermissionCodes.TraceabilityRead
            && x.OperationId == "getBusinessMesMaterialLotTraceability");

        Assert.All(MesEndpointContracts.All, contract =>
            Assert.Contains(contract.PermissionCode, MesPermissionCodes.All));
    }

    [Fact]
    public async Task Work_order_lifecycle_commands_update_status_and_reject_illegal_close()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-05T08:00:00Z");
        var completed = WorkOrder.Create("org-001", "env-dev", "WO-CLOSE", "SKU-001", "PV-001", 2m, 10, now.AddDays(1));
        completed.MarkReleased();
        completed.Start(now);
        completed.RecordProductionProgress(2m, 0m, now.AddMinutes(30));
        var active = WorkOrder.Create("org-001", "env-dev", "WO-ACTIVE", "SKU-001", "PV-001", 2m, 10, now.AddDays(1));
        active.MarkReleased();
        dbContext.WorkOrders.AddRange(completed, active);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var closeHandler = new CloseWorkOrderCommandHandler(dbContext);
        var closeResponse = await closeHandler.Handle(
            new CloseWorkOrderCommand("org-001", "env-dev", "WO-CLOSE", now.AddHours(1)),
            CancellationToken.None);
        var holdResponse = await new HoldWorkOrderCommandHandler(dbContext).Handle(
            new HoldWorkOrderCommand("org-001", "env-dev", "WO-ACTIVE", "material shortage", now.AddMinutes(10)),
            CancellationToken.None);
        var cancelResponse = await new CancelWorkOrderCommandHandler(dbContext).Handle(
            new CancelWorkOrderCommand("org-001", "env-dev", "WO-ACTIVE", "plan cancelled", now.AddMinutes(20)),
            CancellationToken.None);
        var invalidClose = await Assert.ThrowsAsync<KnownException>(() => closeHandler.Handle(
            new CloseWorkOrderCommand("org-001", "env-dev", "WO-ACTIVE", now.AddHours(2)),
            CancellationToken.None));
        var invalidCancel = await Assert.ThrowsAsync<KnownException>(() => new CancelWorkOrderCommandHandler(dbContext).Handle(
            new CancelWorkOrderCommand("org-001", "env-dev", "WO-ACTIVE", "duplicate cancellation", now.AddMinutes(30)),
            CancellationToken.None));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("Accepted", closeResponse.Status);
        Assert.Equal("Accepted", holdResponse.Status);
        Assert.Equal("Accepted", cancelResponse.Status);
        Assert.Equal(WorkOrder.ClosedStatus, completed.Status);
        Assert.Equal(now.AddHours(1), completed.ClosedAtUtc);
        Assert.Equal(WorkOrder.CancelledStatus, active.Status);
        Assert.Equal("material shortage", active.HoldReason);
        Assert.Equal("plan cancelled", active.CancelReason);
        Assert.NotEqual("duplicate cancellation", active.CancelReason);
        Assert.Contains("completed", invalidClose.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(invalidClose.InnerException);
        Assert.Contains("cancelled or scrapped", invalidCancel.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(invalidCancel.InnerException);
    }

    [Fact]
    public async Task Starting_operation_task_starts_owning_work_order()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-05T08:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-START", "SKU-001", "PV-001", 2m, 10, now.AddDays(1));
        var tasks = workOrder.Release(
            now,
            [
                new RoutingStepSnapshot("OP-10", 10, "WC-001", [], TimeSpan.FromMinutes(30)),
            ]);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ChangeOperationTaskStateCommandHandler(dbContext, NoRequirementSnapshotProvider.Instance).Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "start", now.AddMinutes(5)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("OP-10", response.OperationTaskId);
        Assert.Equal(WorkOrder.StartedStatus, workOrder.Status);
    }

    [Fact]
    public async Task Mes_workbench_queries_return_detail_operations_wip_and_empty_material_context()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-05-24T08:00:00Z");
        var workOrder = Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "SKU-FG-1000",
            "PV-001",
            10m,
            1,
            dueUtc);
        var tasks = workOrder.Release(
            dueUtc.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        var scrapLots = SeedReceivedMaterialIssue(dbContext, "WO-001", "OP-10", "MIR-WIP-SCRAP", dueUtc.AddMinutes(-20), 1m);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordProductionReportCommandHandler(dbContext).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-001", "OP-10", 8m, 1m, false, dueUtc, ConsumedMaterialLots: scrapLots),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetMesWorkOrderDetailQueryHandler(dbContext).Handle(
            new GetMesWorkOrderDetailQuery("org-001", "env-dev", "WO-001"),
            CancellationToken.None);
        var operations = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery("org-001", "env-dev", null, Take: 100),
            CancellationToken.None);
        var wip = await new GetWipSummaryQueryHandler(dbContext).Handle(
            new GetWipSummaryQuery("org-001", "env-dev", null, Take: 100),
            CancellationToken.None);
        var material = await new GetMaterialReadinessQueryHandler(dbContext).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-001"),
            CancellationToken.None);

        Assert.Equal("WO-001", detail.WorkOrderId);
        Assert.Equal("Ready", detail.ReadinessStatus);
        Assert.Empty(detail.BlockingReasons);
        Assert.Equal("OP-10", Assert.Single(detail.OperationTasks).OperationTaskId);
        Assert.Equal("OP-10", Assert.Single(operations.Items).OperationTaskId);
        var wipRow = Assert.Single(wip.Items);
        Assert.Equal(10m, wipRow.PlannedQuantity);
        Assert.Equal(8m, wipRow.GoodQuantity);
        Assert.Equal(1m, wipRow.ScrapQuantity);
        Assert.Equal("Ready", material.ReadinessStatus);
        Assert.Empty(material.Items);
    }

    [Fact]
    public async Task Production_report_only_rolls_work_order_progress_from_output_operation()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var reportedAt = DateTimeOffset.Parse("2026-05-24T09:00:00Z");
        var workOrder = Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-OUTPUT",
            "SKU-FG-1000",
            "PV-001",
            100m,
            1,
            reportedAt.AddHours(8));
        var tasks = workOrder.Release(
            reportedAt.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30)),
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-20",
                    20,
                    "WC-INSPECT-01",
                    [],
                    TimeSpan.FromMinutes(20)),
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-30",
                    30,
                    "WC-PACK-01",
                    [],
                    TimeSpan.FromMinutes(25)),
            ]);
        workOrder.Start(reportedAt.AddMinutes(-20));
        tasks.Single(x => x.OperationTaskId == "OP-10").Start(reportedAt.AddMinutes(-15));
        tasks.Single(x => x.OperationTaskId == "OP-20").Start(reportedAt.AddMinutes(10));
        tasks.Single(x => x.OperationTaskId == "OP-30").Start(reportedAt.AddMinutes(25));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        var scrapLots = SeedReceivedMaterialIssue(dbContext, "WO-OUTPUT", "OP-30", "MIR-OUTPUT-SCRAP", reportedAt.AddMinutes(20), 1m);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProductionReportCommandHandler(dbContext);
        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-10", 100m, 0m, true, reportedAt),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0m, workOrder.CompletedQuantity);
        Assert.Equal(WorkOrder.StartedStatus, workOrder.Status);
        Assert.Equal(
            Domain.AggregatesModel.OperationTaskAggregate.OperationTaskLifecycleStatus.Completed,
            tasks.Single(x => x.OperationTaskId == "OP-10").Status);

        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-20", 0m, 0m, true, reportedAt.AddMinutes(20), ReworkQuantity: 1m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-30", 40m, 0m, false, reportedAt.AddMinutes(30)),
            CancellationToken.None);
        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-30", 59m, 1m, true, reportedAt.AddMinutes(45), ConsumedMaterialLots: scrapLots),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(99m, workOrder.CompletedQuantity);
        Assert.Equal(1m, workOrder.ScrapQuantity);
        Assert.Equal(WorkOrder.CompletedStatus, workOrder.Status);
    }

    [Fact]
    public async Task Production_report_rejects_non_completion_report_for_operation_outside_work_order()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var reportedAt = DateTimeOffset.Parse("2026-05-24T10:00:00Z");
        var workOrder = Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-OUTPUT",
            "SKU-FG-1000",
            "PV-001",
            100m,
            1,
            reportedAt.AddHours(8));
        var tasks = workOrder.Release(
            reportedAt.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        workOrder.Start(reportedAt.AddMinutes(-20));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => new RecordProductionReportCommandHandler(dbContext).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-404", 1m, 0m, false, reportedAt),
            CancellationToken.None));

        Assert.Contains("报工工序任务不存在或不属于当前工单", exception.Message, StringComparison.Ordinal);
        Assert.Empty(dbContext.ProductionReports);
        Assert.Equal(0m, workOrder.CompletedQuantity);
        Assert.Equal(0m, workOrder.ScrapQuantity);
    }

    [Fact]
    public void Operation_task_status_filters_do_not_depend_on_enum_ToString_provider_translation()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=nerv_iip_query_translation;Username=nerv;Password=nerv")
            .Options;
        using var dbContext = new Infrastructure.ApplicationDbContext(options, new NoopMediator());

        var query = InvokeOperationTaskEntityQuery(
            dbContext,
            "org-001",
            "env-dev",
            null,
            "inProgress",
            "progress",
            null,
            null,
            null);

        Assert.DoesNotContain("ToString", query.Expression.ToString(), StringComparison.Ordinal);

        var sql = query.ToQueryString();
        Assert.Contains("operation_tasks", sql, StringComparison.Ordinal);
        Assert.Contains("status", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_plan_to_work_order_persists_demand_planning_source_reference_for_queries_and_traceability()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var requestedAtUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");

        var response = await new ConvertPlanToWorkOrderCommandHandler(dbContext).Handle(
            new ConvertPlanToWorkOrderCommand(
                "org-001",
                "env-dev",
                "SUG-001",
                "WO-DP-001",
                requestedAtUtc,
                "SKU-FG-1000",
                "PV-001",
                12m,
                "PCS",
                requestedAtUtc.AddDays(2),
                "WC-MIX-01",
                "DemandPlanning",
                "PlanningSuggestion",
                "SUG-001",
                "DEMAND-001",
                "convert-dp-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var workOrder = await dbContext.WorkOrders.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal("WO-DP-001", response.ReferenceId);
        Assert.NotNull(workOrder.SourcePlanReference);
        Assert.Equal("DemandPlanning", workOrder.SourcePlanReference.SourceSystem);
        Assert.Equal("PlanningSuggestion", workOrder.SourcePlanReference.SourceDocumentType);
        Assert.Equal("SUG-001", workOrder.SourcePlanReference.SourceDocumentId);
        Assert.Equal("DEMAND-001", workOrder.SourcePlanReference.SourceDemandReference);

        var plans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Take: 100),
            CancellationToken.None);
        var plan = Assert.Single(plans.Items);
        Assert.Equal("SUG-001", plan.ProductionPlanId);
        Assert.Equal("DemandPlanning", plan.SourceSystem);
        Assert.Equal("PlanningSuggestion", plan.SourceDocumentType);
        Assert.Equal("SUG-001", plan.SourceDocumentId);
        Assert.Equal("DEMAND-001", plan.SourceDemandReference);
        Assert.Equal("created", plan.Status);

        var detail = await new GetMesWorkOrderDetailQueryHandler(dbContext).Handle(
            new GetMesWorkOrderDetailQuery("org-001", "env-dev", "WO-DP-001"),
            CancellationToken.None);
        Assert.Equal("DemandPlanning", detail.SourcePlanReference?.SourceSystem);
        Assert.Equal("SUG-001", detail.SourcePlanReference?.SourceDocumentId);
        Assert.Equal("DEMAND-001", detail.SourcePlanReference?.SourceDemandReference);

        var traceability = await new GetWorkOrderTraceabilityQueryHandler(dbContext).Handle(
            new GetWorkOrderTraceabilityQuery("org-001", "env-dev", "WO-DP-001"),
            CancellationToken.None);
        Assert.Contains(traceability.Nodes, x => x.NodeId == "SUG-001" && x.NodeType == "PlanningSuggestion");
        Assert.Contains(traceability.Edges, x => x.FromNodeId == "SUG-001" && x.ToNodeId == "WO-DP-001" && x.RelationType == "converted-to-work-order");
    }

    [Fact]
    public async Task Production_plan_query_filters_status_before_take_and_uses_work_order_status()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-CREATED-001",
            "SKU-001",
            "PV-001",
            1m,
            10,
            dueUtc,
            "PCS",
            new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-CREATED-001", null)));
        var released = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-RELEASED-001",
            "SKU-002",
            "PV-002",
            1m,
            10,
            dueUtc.AddMinutes(1),
            "PCS",
            new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-RELEASED-001", null));
        released.Release(
            dueUtc,
            [
                new RoutingStepSnapshot("OP-10", 10, "WC-01", [], TimeSpan.FromMinutes(30)),
            ]);
        dbContext.WorkOrders.Add(released);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var plans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", "released", Take: 1),
            CancellationToken.None);

        var plan = Assert.Single(plans.Items);
        Assert.Equal("SUG-RELEASED-001", plan.ProductionPlanId);
        Assert.Equal("released", plan.Status);
    }

    [Fact]
    public async Task Production_plan_query_filters_source_and_readiness_before_count_and_page()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-SALES-001",
            "SKU-SALES",
            "PV-001",
            1m,
            10,
            dueUtc,
            "PCS",
            new SourcePlanReference("SalesOrder", "PlanningSuggestion", "SO-001", "DEMAND-SALES")));
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-STOCK-001",
            "SKU-STOCK",
            "PV-001",
            1m,
            10,
            dueUtc.AddMinutes(1),
            "PCS",
            new SourcePlanReference("StockPlan", "PlanningSuggestion", "STOCK-001", "DEMAND-STOCK")));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var salesPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "SalesOrder", Source: "sales", ReadinessStatus: "Ready"),
            CancellationToken.None);
        var blockedPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Source: "sales", ReadinessStatus: "Blocked"),
            CancellationToken.None);

        Assert.Equal(1, salesPlans.Total);
        Assert.Equal("SO-001", Assert.Single(salesPlans.Items).ProductionPlanId);
        Assert.Equal(0, blockedPlans.Total);
        Assert.Empty(blockedPlans.Items);
    }

    [Fact]
    public async Task Production_plan_keyword_does_not_bypass_filters_with_readiness_text()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-ALPHA-001",
            "SKU-ALPHA",
            "PV-ALPHA",
            1m,
            10,
            dueUtc,
            "PCS",
            new SourcePlanReference("Alpha", "Beta", "GAMMA-001", "DELTA-001")));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var substringPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "y"),
            CancellationToken.None);
        var readyPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "ready"),
            CancellationToken.None);

        Assert.Equal(0, substringPlans.Total);
        Assert.Empty(substringPlans.Items);
        Assert.Equal(0, readyPlans.Total);
        Assert.Empty(readyPlans.Items);
    }

    [Fact]
    public async Task Work_order_list_query_returns_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 10, dueUtc));
        var partiallyCompleted = WorkOrder.Create("org-001", "env-dev", "WO-002", "SKU-002", "PV-002", 3m, 10, dueUtc.AddMinutes(1), "PCS");
        partiallyCompleted.RecordProductionProgress(1m, 0m, dueUtc.AddMinutes(2));
        dbContext.WorkOrders.Add(partiallyCompleted);
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-003", "SKU-003", "PV-003", 1m, 10, dueUtc.AddMinutes(2)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var page = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new ListMesWorkOrdersQuery("org-001", "env-dev", null, Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, page.Total);
        var workOrder = Assert.Single(page.Items);
        Assert.Equal("WO-002", workOrder.WorkOrderId);
        Assert.Equal("PCS", workOrder.UomCode);
        Assert.Equal(1m, workOrder.CompletedQuantity);
    }

    [Fact]
    public async Task Secondary_mes_list_queries_return_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.MaterialIssueRequests.AddRange(
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-001", "WO-MAT", "OP-MAT-10", "MAT-OIL", "L", 1m, now.AddMinutes(1)),
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-002", "WO-MAT", "OP-MAT-20", "MAT-OIL", "L", 1m, now.AddMinutes(2)),
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-003", "WO-MAT", "OP-MAT-30", "MAT-OIL", "L", 1m, now.AddMinutes(3)));
        dbContext.WorkCenterUnavailabilities.AddRange(
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-001", "WC-MIX", now.AddMinutes(1), null, "breakdown", "ASSET-001"),
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-002", "WC-MIX", now.AddMinutes(2), null, "breakdown", "ASSET-001"),
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-003", "WC-MIX", now.AddMinutes(3), null, "breakdown", "ASSET-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var materialIssues = await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(
            new ListMaterialIssueRequestsQuery("org-001", "env-dev", "WO-MAT", Skip: 1, Take: 1),
            CancellationToken.None);
        var downtimeEvents = await new ListDowntimeEventsQueryHandler(dbContext).Handle(
            new ListDowntimeEventsQuery("org-001", "env-dev", "WC-MIX", "ASSET-001", Skip: 1, Take: 1),
            CancellationToken.None);
        var capacityImpacts = await new ListCapacityImpactsQueryHandler(dbContext).Handle(
            new ListCapacityImpactsQuery("org-001", "env-dev", "ASSET-001", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, materialIssues.Total);
        Assert.Equal("MIR-002", Assert.Single(materialIssues.Items).RequestId);
        Assert.Equal(3, downtimeEvents.Total);
        Assert.Equal("DOWNTIME-002", Assert.Single(downtimeEvents.Items).DowntimeEventId);
        Assert.Equal(3, capacityImpacts.Total);
        Assert.Equal("DOWNTIME-002", Assert.Single(capacityImpacts.Items).ImpactId);
    }

    [Fact]
    public async Task Mes_list_queries_apply_server_filters_before_count_and_page()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");

        var targetOrder = WorkOrder.Create("org-001", "env-dev", "WO-FILTER-001", "SKU-FILTER", "PV-001", 1m, 10, now);
        var targetTasks = targetOrder.Release(
            now.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-FILTER-10",
                    10,
                    "WC-FILTER",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        targetTasks.Single().Assign("operator-001", "DEV-FILTER", "SHIFT-FILTER", now);
        var otherOrder = WorkOrder.Create("org-001", "env-dev", "WO-OTHER-001", "SKU-OTHER", "PV-001", 1m, 10, now.AddMinutes(1));
        var otherTasks = otherOrder.Release(
            now.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-OTHER-10",
                    10,
                    "WC-OTHER",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        otherTasks.Single().Assign("operator-002", "DEV-OTHER", "SHIFT-OTHER", now);
        dbContext.WorkOrders.AddRange(targetOrder, otherOrder);
        dbContext.OperationTasks.AddRange(targetTasks);
        dbContext.OperationTasks.AddRange(otherTasks);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var workOrders = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new ListMesWorkOrdersQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "filter", WorkCenterId: "WC-FILTER"),
            CancellationToken.None);
        var operationTasks = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "DEV-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var dispatchTasks = await new ListDispatchTasksQueryHandler(dbContext).Handle(
            new ListDispatchTasksQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "OP-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var wip = await new GetWipSummaryQueryHandler(dbContext).Handle(
            new GetWipSummaryQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "WO-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);

        Assert.Equal(1, workOrders.Total);
        Assert.Equal("WO-FILTER-001", Assert.Single(workOrders.Items).WorkOrderId);
        Assert.Equal(1, operationTasks.Total);
        var operationTask = Assert.Single(operationTasks.Items);
        Assert.Equal("OP-FILTER-10", operationTask.OperationTaskId);
        Assert.Equal("WO-FILTER-001", operationTask.WorkOrderNo);
        Assert.Equal("OP-FILTER-10", operationTask.OperationTaskNo);
        Assert.Equal("WC-FILTER", operationTask.WorkCenterCode);
        Assert.Null(operationTask.WorkCenterName);
        Assert.Equal("DEV-FILTER", operationTask.DeviceAssetCode);
        Assert.Null(operationTask.DeviceAssetName);
        Assert.Equal(1, dispatchTasks.Total);
        var dispatchTask = Assert.Single(dispatchTasks.Items);
        Assert.Equal("OP-FILTER-10", dispatchTask.OperationTaskId);
        Assert.Equal("WO-FILTER-001", dispatchTask.WorkOrderNo);
        Assert.Equal("OP-FILTER-10", dispatchTask.OperationTaskNo);
        Assert.Equal("WC-FILTER", dispatchTask.WorkCenterCode);
        Assert.Null(dispatchTask.WorkCenterName);
        Assert.Equal("DEV-FILTER", dispatchTask.DeviceAssetCode);
        Assert.Null(dispatchTask.DeviceAssetName);
        Assert.Equal(1, wip.Total);
        var wipItem = Assert.Single(wip.Items);
        Assert.Equal("OP-FILTER-10", wipItem.OperationTaskId);
        Assert.Equal("WO-FILTER-001", wipItem.WorkOrderNo);
        Assert.Equal("OP-FILTER-10", wipItem.OperationTaskNo);
        Assert.Equal("WC-FILTER", wipItem.WorkCenterCode);
        Assert.Null(wipItem.WorkCenterName);
    }

    [Fact]
    public async Task Mes_secondary_production_lists_apply_keyword_and_structured_filters_before_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");

        var targetOrder = WorkOrder.Create("org-001", "env-dev", "WO-FILTER", "SKU-FILTER", "PV-001", 1m, 10, now);
        var targetTasks = targetOrder.Release(
            now.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-FILTER",
                    10,
                    "WC-FILTER",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        targetTasks.Single().Assign("operator-001", "DEV-FILTER", "SHIFT-FILTER", now);
        var otherOrder = WorkOrder.Create("org-001", "env-dev", "WO-OTHER", "SKU-OTHER", "PV-001", 1m, 10, now.AddMinutes(1));
        var otherTasks = otherOrder.Release(
            now.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-OTHER",
                    10,
                    "WC-OTHER",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        otherTasks.Single().Assign("operator-002", "DEV-OTHER", "SHIFT-OTHER", now);
        dbContext.WorkOrders.AddRange(targetOrder, otherOrder);
        dbContext.OperationTasks.AddRange(targetTasks);
        dbContext.OperationTasks.AddRange(otherTasks);
        dbContext.ProductionReports.AddRange(
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-FILTER", "WO-FILTER", "OP-FILTER", 1m, 0m, false, now),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-OTHER", "WO-OTHER", "OP-OTHER", 1m, 0m, false, now.AddMinutes(1)));
        dbContext.FinishedGoodsReceiptRequests.AddRange(
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-FILTER", "WO-FILTER", "SKU-FILTER", 1m, "PCS", now),
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-OTHER", "WO-OTHER", "SKU-OTHER", 1m, "PCS", now.AddMinutes(1)));
        dbContext.MaterialIssueRequests.AddRange(
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-FILTER", "WO-FILTER", "OP-FILTER", "MAT-FILTER", "PCS", 1m, now),
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-OTHER", "WO-OTHER", "OP-OTHER", "MAT-OTHER", "PCS", 1m, now.AddMinutes(1)));
        dbContext.WorkCenterUnavailabilities.AddRange(
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-FILTER", "WC-FILTER", now, null, "filter-reason", "DEV-FILTER"),
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-OTHER", "WC-OTHER", now.AddMinutes(1), null, "other-reason", "DEV-OTHER"));
        await new CreateShiftHandoverCommandHandler(dbContext).Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-FILTER", "TEAM-FILTER", now, "handover-filter"),
            CancellationToken.None);
        await new CreateShiftHandoverCommandHandler(dbContext).Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-OTHER", "TEAM-OTHER", now.AddMinutes(1), "handover-other"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-FILTER", "OP-FILTER", "DEF-FILTER", 1m, now.AddMinutes(2), "defect-filter"),
            CancellationToken.None);
        await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-OTHER", "OP-OTHER", "DEF-OTHER", 1m, now.AddMinutes(3), "defect-other"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var reports = await new ListProductionReportsQueryHandler(dbContext).Handle(
            new ListProductionReportsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "PRPT-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var receipts = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "SKU-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var materialIssues = await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(
            new ListMaterialIssueRequestsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "MAT-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var qualityItems = await new ListRelatedQualityItemsQueryHandler(dbContext).Handle(
            new ListRelatedQualityItemsQuery("org-001", "env-dev", null, null, Skip: 0, Take: 10, Keyword: "DEF-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var downtimeEvents = await new ListDowntimeEventsQueryHandler(dbContext).Handle(
            new ListDowntimeEventsQuery("org-001", "env-dev", "WC-FILTER", "DEV-FILTER", Skip: 0, Take: 10, Keyword: "DOWNTIME-FILTER", ShiftId: "SHIFT-FILTER"),
            CancellationToken.None);
        var capacityImpacts = await new ListCapacityImpactsQueryHandler(dbContext).Handle(
            new ListCapacityImpactsQuery("org-001", "env-dev", "DEV-FILTER", Skip: 0, Take: 10, WorkCenterId: "WC-FILTER", Keyword: "filter-reason", ShiftId: "SHIFT-FILTER"),
            CancellationToken.None);
        var handovers = await new ListShiftHandoversQueryHandler(dbContext).Handle(
            new ListShiftHandoversQuery("org-001", "env-dev", "SHIFT-FILTER", Skip: 0, Take: 10, Keyword: "TEAM-FILTER", WorkCenterId: "WC-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var nonMatchingReceipts = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Status: "posted"),
            CancellationToken.None);
        var nonMatchingMaterialIssues = await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(
            new ListMaterialIssueRequestsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Status: "received"),
            CancellationToken.None);
        var nonMatchingQualityItems = await new ListRelatedQualityItemsQueryHandler(dbContext).Handle(
            new ListRelatedQualityItemsQuery("org-001", "env-dev", null, null, Skip: 0, Take: 10, Status: "reworkPending"),
            CancellationToken.None);
        var nonMatchingDowntimeEvents = await new ListDowntimeEventsQueryHandler(dbContext).Handle(
            new ListDowntimeEventsQuery("org-001", "env-dev", null, null, Skip: 0, Take: 10, Status: "recovered"),
            CancellationToken.None);
        var nonMatchingCapacityImpacts = await new ListCapacityImpactsQueryHandler(dbContext).Handle(
            new ListCapacityImpactsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Status: "recovered"),
            CancellationToken.None);
        var nonMatchingHandovers = await new ListShiftHandoversQueryHandler(dbContext).Handle(
            new ListShiftHandoversQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Status: "accepted"),
            CancellationToken.None);

        Assert.Equal("PRPT-FILTER", Assert.Single(reports.Items).ReportNo);
        Assert.Equal("WO-FILTER", Assert.Single(reports.Items).WorkOrderNo);
        Assert.Equal("OP-FILTER", Assert.Single(reports.Items).OperationTaskNo);
        Assert.Equal(1, reports.Total);
        var receipt = Assert.Single(receipts.Items);
        Assert.Equal("FGR-FILTER", receipt.RequestNo);
        Assert.Equal("WO-FILTER", receipt.WorkOrderNo);
        Assert.Equal("SKU-FILTER", receipt.SkuCode);
        Assert.Equal(1, receipts.Total);
        var materialIssue = Assert.Single(materialIssues.Items);
        Assert.Equal("MIR-FILTER", materialIssue.RequestId);
        Assert.Equal("WO-FILTER", materialIssue.WorkOrderNo);
        Assert.Equal("OP-FILTER", materialIssue.OperationTaskNo);
        Assert.Equal("MAT-FILTER", materialIssue.MaterialCode);
        Assert.Equal(1, materialIssues.Total);
        Assert.Equal("DEF-FILTER", Assert.Single(qualityItems.Items).DefectCode);
        Assert.Equal(1, qualityItems.Total);
        var downtime = Assert.Single(downtimeEvents.Items);
        Assert.Equal("DOWNTIME-FILTER", downtime.DowntimeEventId);
        Assert.Null(downtime.WorkOrderNo);
        Assert.Null(downtime.OperationTaskNo);
        Assert.Equal("DEV-FILTER", downtime.DeviceAssetCode);
        Assert.Null(downtime.DeviceAssetName);
        Assert.Equal(1, downtimeEvents.Total);
        var capacityImpact = Assert.Single(capacityImpacts.Items);
        Assert.Equal("DOWNTIME-FILTER", capacityImpact.ImpactId);
        Assert.Equal("WC-FILTER", capacityImpact.WorkCenterCode);
        Assert.Null(capacityImpact.WorkCenterName);
        Assert.Equal("DEV-FILTER", capacityImpact.DeviceAssetCode);
        Assert.Null(capacityImpact.DeviceAssetName);
        Assert.Equal(1, capacityImpacts.Total);
        Assert.Equal("SHIFT-FILTER", Assert.Single(handovers.Items).ShiftId);
        Assert.Equal(1, handovers.Total);
        Assert.Equal(0, nonMatchingReceipts.Total);
        Assert.Equal(0, nonMatchingMaterialIssues.Total);
        Assert.Equal(0, nonMatchingQualityItems.Total);
        Assert.Equal(0, nonMatchingDowntimeEvents.Total);
        Assert.Equal(0, nonMatchingCapacityImpacts.Total);
        Assert.Equal(0, nonMatchingHandovers.Total);
    }

    [Fact]
    public async Task Related_quality_items_and_shift_handovers_return_persisted_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QUALITY", "SKU-001", "PV-001", 1m, 10, now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-QUALITY", "OP-10", "DEF-SURFACE", 1m, now.AddMinutes(1), "defect-001"),
            CancellationToken.None);
        var expectedQualityItem = await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-QUALITY", "OP-20", "DEF-MIX", 2m, now.AddMinutes(2), "defect-002"),
            CancellationToken.None);
        await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-QUALITY", "OP-30", "DEF-PACK", 3m, now.AddMinutes(3), "defect-003"),
            CancellationToken.None);
        var shiftHandoverCommandHandler = new CreateShiftHandoverCommandHandler(dbContext);
        await shiftHandoverCommandHandler.Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-A", "TEAM-A", now.AddMinutes(1), "handover-001"),
            CancellationToken.None);
        var acceptedHandover = await shiftHandoverCommandHandler.Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-A", "TEAM-B", now.AddMinutes(2), "handover-002"),
            CancellationToken.None);
        await shiftHandoverCommandHandler.Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-A", "TEAM-C", now.AddMinutes(3), "handover-003"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new AcceptShiftHandoverCommandHandler(dbContext).Handle(
            new AcceptShiftHandoverCommand("org-001", "env-dev", acceptedHandover.ReferenceId, now.AddMinutes(4)),
            CancellationToken.None);
        var repeatedAccept = await new AcceptShiftHandoverCommandHandler(dbContext).Handle(
            new AcceptShiftHandoverCommand("org-001", "env-dev", acceptedHandover.ReferenceId, now.AddMinutes(5)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var qualityItems = await new ListRelatedQualityItemsQueryHandler(dbContext).Handle(
            new ListRelatedQualityItemsQuery("org-001", "env-dev", "WO-QUALITY", null, Skip: 1, Take: 1),
            CancellationToken.None);
        var handovers = await new ListShiftHandoversQueryHandler(dbContext).Handle(
            new ListShiftHandoversQuery("org-001", "env-dev", "SHIFT-A", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, qualityItems.Total);
        var qualityItem = Assert.Single(qualityItems.Items);
        Assert.Equal(acceptedHandover.ReferenceId, Assert.Single(handovers.Items).HandoverId);
        Assert.Equal(expectedQualityItem.ReferenceId, qualityItem.QualityItemId);
        Assert.Equal("Defect", qualityItem.SourceType);
        Assert.Equal("OP-20", qualityItem.SourceDocumentId);
        Assert.Equal("Open", qualityItem.Status);
        Assert.Equal("DEF-MIX", qualityItem.DefectCode);
        Assert.Null(qualityItem.NcrId);
        Assert.Equal(3, handovers.Total);
        Assert.Equal("Accepted", Assert.Single(handovers.Items).HandoverStatus);
        Assert.Equal(now.AddMinutes(4), repeatedAccept.AcceptedAtUtc);
    }

    [Fact]
    public async Task Record_defect_is_idempotent_for_same_payload_and_idempotency_key()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QUALITY", "SKU-001", "PV-001", 1m, 10, now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordDefectCommandHandler(dbContext);
        var command = new RecordDefectCommand(
            "org-001",
            "env-dev",
            "WO-QUALITY",
            "OP-10",
            "DEF-SURFACE",
            1m,
            now.AddMinutes(1),
            "defect-idem-001");

        var firstResult = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var secondResult = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(firstResult, secondResult);
        Assert.Equal(1, await dbContext.DefectRecords.CountAsync(
            x => x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.WorkOrderId == "WO-QUALITY",
            CancellationToken.None));
    }

    [Fact]
    public async Task Convert_plan_endpoint_rejects_missing_due_utc_instead_of_defaulting_to_now()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/mes/production-plans/SUG-001/work-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            skuId = "SKU-FG-1000",
            productionVersionId = "PV-001",
            plannedQuantity = 12m,
            uomCode = "PCS",
            workCenterId = "WC-MIX-01",
            requestedAtUtc = "2026-06-01T08:00:00Z",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(EndpointTypes))]
    public void Mes_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
    }

    [Fact]
    public async Task Mes_public_production_queries_return_reports_receipt_requests_and_capacity_impacts()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var reportedAt = DateTimeOffset.Parse("2026-05-24T08:00:00Z");
        var workOrder = Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "SKU-FG-1000",
            "PV-001",
            10m,
            1,
            reportedAt.AddHours(8));
        var tasks = workOrder.Release(
            reportedAt.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        tasks.Single().Start(reportedAt.AddMinutes(-10));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        var scrapLots = SeedReceivedMaterialIssue(dbContext, "WO-001", "OP-10", "MIR-PUBLIC-SCRAP", reportedAt.AddMinutes(-5), 1m);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var reportResult = await new RecordProductionReportCommandHandler(dbContext).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-001", "OP-10", 9m, 1m, true, reportedAt, ConsumedMaterialLots: scrapLots),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var producedLotNo = (await dbContext.ProductionReports.SingleAsync(x => x.ReportNo == reportResult.ReportNo, CancellationToken.None)).ProducedLotNo;
        await new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext).Handle(
            new CreateFinishedGoodsReceiptRequestCommand("org-001", "env-dev", "WO-001", "SKU-FG-1000", 9m, "PCS", reportedAt.AddMinutes(15), 12.34m, ProducedLotNo: producedLotNo),
            CancellationToken.None);
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "DOWNTIME-001",
            "WC-MIX-01",
            reportedAt,
            null,
            "maintenance",
            "ASSET-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var reports = await new ListProductionReportsQueryHandler(dbContext).Handle(
            new ListProductionReportsQuery("org-001", "env-dev", "WO-001"),
            CancellationToken.None);
        var receipts = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", "WO-001"),
            CancellationToken.None);
        var capacity = await new ListCapacityImpactsQueryHandler(dbContext).Handle(
            new ListCapacityImpactsQuery("org-001", "env-dev", "ASSET-001"),
            CancellationToken.None);

        var report = Assert.Single(reports.Items);
        Assert.StartsWith("PRPT-", report.ReportNo, StringComparison.Ordinal);
        Assert.Equal("WO-001", report.WorkOrderId);
        Assert.Equal("OP-10", report.OperationTaskId);
        Assert.Equal(9m, report.GoodQuantity);
        var receipt = Assert.Single(receipts.Items);
        Assert.StartsWith("FGR-", receipt.RequestNo, StringComparison.Ordinal);
        Assert.Equal("SKU-FG-1000", receipt.SkuId);
        Assert.Equal(9m, receipt.Quantity);
        var impact = Assert.Single(capacity.Items);
        Assert.Equal("DOWNTIME-001", impact.ImpactId);
        Assert.Equal("ASSET-001", impact.DeviceAssetId);
        Assert.Equal("WC-MIX-01", impact.WorkCenterId);
        Assert.Null(impact.EffectiveToUtc);
    }

    [Fact]
    public async Task Secondary_mes_production_queries_return_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.ProductionReports.AddRange(
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-001", "WO-001", "OP-10", 1m, 0m, false, now.AddMinutes(1)),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-002", "WO-001", "OP-20", 1m, 0m, false, now.AddMinutes(2)),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-003", "WO-001", "OP-30", 1m, 0m, false, now.AddMinutes(3)));
        dbContext.FinishedGoodsReceiptRequests.AddRange(
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-001", "WO-001", "SKU-001", 1m, "PCS", now.AddMinutes(1)),
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-002", "WO-001", "SKU-001", 1m, "PCS", now.AddMinutes(2)),
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-003", "WO-001", "SKU-001", 1m, "PCS", now.AddMinutes(3)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var reports = await new ListProductionReportsQueryHandler(dbContext).Handle(
            new ListProductionReportsQuery("org-001", "env-dev", "WO-001", Skip: 1, Take: 1),
            CancellationToken.None);
        var receipts = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", "WO-001", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, reports.Total);
        Assert.Equal("PRPT-002", Assert.Single(reports.Items).ReportNo);
        Assert.Equal(3, receipts.Total);
        Assert.Equal("FGR-002", Assert.Single(receipts.Items).RequestNo);
    }

    [Theory]
    [InlineData("/api/business/v1/mes/schedules/run")]
    [InlineData("/api/business/v1/mes/work-orders/rush")]
    [InlineData("/api/business/v1/mes/work-orders/WO-001/close")]
    [InlineData("/api/business/v1/mes/work-orders/WO-001/hold")]
    [InlineData("/api/business/v1/mes/work-orders/WO-001/cancel")]
    public async Task Mes_write_endpoints_require_internal_service_authentication(string route)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(route, new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            trigger = "Manual",
            workOrderId = "WO-RUSH",
            skuId = "SKU-R",
            productionVersionId = "PV-001",
            quantity = 1,
            dueUtc = DateTimeOffset.Parse("2026-05-22T12:00:00Z"),
            workCenterId = "WC-A",
            durationMinutes = 60
        });

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected auth failure but received {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Mes_work_order_query_endpoint_requires_internal_service_authentication()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/business/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected auth failure but received {(int)response.StatusCode}.");
    }

    public static IEnumerable<object[]> EndpointTypes()
    {
        return MesEndpointContracts.All.Select(x => new object[] { x.EndpointType });
    }

    private static IReadOnlyCollection<ConsumedMaterialLotInput> SeedReceivedMaterialIssue(
        Infrastructure.ApplicationDbContext dbContext,
        string workOrderId,
        string operationTaskId,
        string requestNo,
        DateTimeOffset requestedAtUtc,
        decimal consumedQuantity)
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            requestNo,
            workOrderId,
            operationTaskId,
            "MAT-SCRAP",
            "PCS",
            10m,
            requestedAtUtc);
        request.ConfirmLineSideReceipt(requestedAtUtc.AddMinutes(1), 10m, "LOT-SCRAP");
        request.ClearDomainEvents();
        dbContext.MaterialIssueRequests.Add(request);
        return [new ConsumedMaterialLotInput("MAT-SCRAP", "LOT-SCRAP", consumedQuantity, requestNo)];
    }

    private static IQueryable<Domain.AggregatesModel.OperationTaskAggregate.OperationTask> InvokeOperationTaskEntityQuery(
        Infrastructure.ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? workOrderId,
        string? status,
        string? keyword,
        string? workCenterId,
        string? shiftId,
        string? deviceAssetId)
    {
        var method = typeof(GetMesWorkOrderDetailQueryHandler).GetMethod(
            "QueryOperationTaskEntities",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable<Domain.AggregatesModel.OperationTaskAggregate.OperationTask>>(
            method.Invoke(null, [
                dbContext,
                organizationId,
                environmentId,
                workOrderId,
                status,
                keyword,
                workCenterId,
                shiftId,
                deviceAssetId,
            ]));
    }
}

internal static class MesTestProvider
{
    public static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"mes-production-contract-{Guid.NewGuid():N}";
        services.AddSingleton<IMediator, NoopMediator>();
        services.AddDbContext<Infrastructure.ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }
}

internal sealed class NoopMediator : IMediator
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("No-op mediator cannot send requests.");
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        throw new NotSupportedException("No-op mediator cannot send requests.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("No-op mediator cannot send requests.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("No-op mediator cannot stream requests.");
    }

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("No-op mediator cannot stream requests.");
    }
}

internal sealed class NoRequirementSnapshotProvider : IMesMaterialRequirementSnapshotProvider
{
    public static readonly NoRequirementSnapshotProvider Instance = new();

    public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
        MesMaterialRequirementSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}
