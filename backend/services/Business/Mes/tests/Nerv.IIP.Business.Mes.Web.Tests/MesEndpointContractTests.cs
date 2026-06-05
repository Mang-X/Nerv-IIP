using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
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
        Assert.Equal(38, MesEndpointContracts.All.Count);
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
        await new RecordProductionReportCommandHandler(dbContext).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-001", "OP-10", 8m, 1m, false, dueUtc),
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
    public async Task Work_order_list_query_returns_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 10, dueUtc));
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-002", "SKU-002", "PV-002", 1m, 10, dueUtc.AddMinutes(1)));
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-003", "SKU-003", "PV-003", 1m, 10, dueUtc.AddMinutes(2)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var page = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new ListMesWorkOrdersQuery("org-001", "env-dev", null, Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, page.Total);
        Assert.Equal("WO-002", Assert.Single(page.Items).WorkOrderId);
    }

    [Fact]
    public async Task Secondary_mes_list_queries_return_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.MaterialIssueRequests.AddRange(
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-001", "WO-MAT", "OP-MAT-10", "MAT-OIL", 1m, now.AddMinutes(1)),
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-002", "WO-MAT", "OP-MAT-20", "MAT-OIL", 1m, now.AddMinutes(2)),
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-003", "WO-MAT", "OP-MAT-30", "MAT-OIL", 1m, now.AddMinutes(3)));
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
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordProductionReportCommandHandler(dbContext).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-001", "OP-10", 9m, 1m, true, reportedAt),
            CancellationToken.None);
        await new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext).Handle(
            new CreateFinishedGoodsReceiptRequestCommand("org-001", "env-dev", "WO-001", "SKU-FG-1000", 9m, "PCS", reportedAt.AddMinutes(15)),
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
