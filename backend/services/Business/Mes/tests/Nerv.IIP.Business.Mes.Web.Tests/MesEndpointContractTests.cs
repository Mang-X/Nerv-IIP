using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Business.Mes.Web.Application.Auth;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesEndpointContractTests
{
    [Fact]
    public void MesEndpointContracts_ExposeRescheduleAndRushOrderRoutes()
    {
        Assert.Equal(14, MesEndpointContracts.All.Count);
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
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/material-readiness"
            && x.PermissionCode == MesPermissionCodes.MaterialsRead
            && x.OperationId == "getBusinessMesMaterialReadiness");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/operation-tasks"
            && x.PermissionCode == MesPermissionCodes.OperationsRead
            && x.OperationId == "listBusinessMesOperationTasks");
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
            new ListOperationTasksQuery("org-001", "env-dev", null, 100),
            CancellationToken.None);
        var wip = await new GetWipSummaryQueryHandler(dbContext).Handle(
            new GetWipSummaryQuery("org-001", "env-dev", null, 100),
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
        await new RecordProductionReportCommandHandler(dbContext).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-001", "OP-10", 9m, 1m, true, reportedAt),
            CancellationToken.None);
        await new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext).Handle(
            new CreateFinishedGoodsReceiptRequestCommand("org-001", "env-dev", "WO-001", "SKU-FG-1000", 9m, "PCS", reportedAt.AddMinutes(15)),
            CancellationToken.None);
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
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
        Assert.Equal("WO-001", report.WorkOrderId);
        Assert.Equal("OP-10", report.OperationTaskId);
        Assert.Equal(9m, report.GoodQuantity);
        var receipt = Assert.Single(receipts.Items);
        Assert.Equal("SKU-FG-1000", receipt.SkuId);
        Assert.Equal(9m, receipt.Quantity);
        var impact = Assert.Single(capacity.Items);
        Assert.Equal("ASSET-001", impact.DeviceAssetId);
        Assert.Equal("WC-MIX-01", impact.WorkCenterId);
        Assert.Null(impact.ToUtc);
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
        services.AddSingleton<IMediator, NoopMediator>();
        services.AddDbContext<Infrastructure.ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"mes-production-contract-{Guid.NewGuid():N}"));
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
