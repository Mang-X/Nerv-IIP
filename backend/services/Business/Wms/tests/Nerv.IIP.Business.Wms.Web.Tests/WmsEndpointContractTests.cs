using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions;
using InboundOrder = Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder;
using InboundOrderLineDraft = Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrderLineDraft;
using OutboundOrder = Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder;
using OutboundOrderLineDraft = Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrderLineDraft;
using CountExecution = Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate.CountExecution;
using WarehouseTask = Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate.WarehouseTask;
using WarehouseTaskId = Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate.WarehouseTaskId;
using WarehouseTaskType = Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate.WarehouseTaskType;
using WcsTask = Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate.WcsTask;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsEndpointContractTests
{
    [Fact]
    public void Wms_endpoints_expose_issue_136_routes_permissions_policies_and_operation_ids()
    {
        var contracts = WmsEndpointContracts.All.ToArray();

        Assert.Equal(17, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsInboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/inbound-orders" && x.PermissionCode == WmsPermissionCodes.ReceiptsRead && x.OperationId == "listWmsInboundOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsPutawayTask");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/putaway-tasks" && x.PermissionCode == WmsPermissionCodes.ReceiptsRead && x.OperationId == "listWmsPutawayTasks");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "completeWmsInboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "createWmsOutboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/outbound-orders" && x.PermissionCode == WmsPermissionCodes.ShipmentsRead && x.OperationId == "listWmsOutboundOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "createWmsPickingTask");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/picking-tasks" && x.PermissionCode == WmsPermissionCodes.ShipmentsRead && x.OperationId == "listWmsPickingTasks");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "completeWmsOutboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/count-executions" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsCountExecution");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/count-executions" && x.PermissionCode == WmsPermissionCodes.ReceiptsRead && x.OperationId == "listWmsCountExecutions");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/count-executions/{countExecutionId}/complete" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "completeWmsCountExecution");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch" && x.PermissionCode == WmsPermissionCodes.AutomationManage && x.OperationId == "dispatchWmsWcsTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/wcs-tasks/{externalTaskId}/complete" && x.PermissionCode == WmsPermissionCodes.AutomationManage && x.OperationId == "completeWmsWcsTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail" && x.PermissionCode == WmsPermissionCodes.AutomationManage && x.OperationId == "failWmsWcsTask");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/wcs-tasks" && x.PermissionCode == WmsPermissionCodes.AutomationManage && x.OperationId == "listWmsWcsTasks");
        Assert.All(contracts, x => Assert.Equal(InternalServiceAuthorizationPolicy.Name, x.AuthorizationPolicy));
    }

    [Theory]
    [MemberData(nameof(EndpointTypes))]
    public void Wms_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType.GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Wms_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.UseSetting("Inventory:BaseUrl", "http://inventory.local");
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/business/v1/wms/inbound-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            inboundOrderNo = "IN-001",
            sourceDocumentType = "purchase-receipt",
            sourceDocumentId = "PO-001",
            siteCode = "SITE-01",
            lines = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wms_registers_persistent_integration_event_dead_letter_store()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();

        Assert.Equal(
            "Nerv.IIP.Messaging.CAP.PersistentIntegrationEventDeadLetterStore`1[[Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext, Nerv.IIP.Business.Wms.Infrastructure, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]",
            store.GetType().FullName);
    }

    [Fact]
    public async Task Wms_live_http_acceptance_host_dispatches_fails_retries_and_completes_wcs_task()
    {
        await using var factory = CreateAuthorizedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var externalTaskId = $"WCS-{suffix}-1";
        var retryExternalTaskId = $"WCS-{suffix}-2";
        WarehouseTaskId warehouseTaskId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var warehouseTask = WarehouseTask.CreatePutaway("org-acceptance", "env-acceptance", $"WT-{suffix}", $"IN-WCS-{suffix}", "10", $"SKU-WCS-{suffix}", "pcs", "SITE-01", "RECV-01", "STAGE-01", 3m);
            dbContext.WarehouseTasks.Add(warehouseTask);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            warehouseTaskId = warehouseTask.Id;
        }

        await PostJsonAndAssertOkAsync(client, $"/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", new
        {
            warehouseTaskId = warehouseTaskId.ToString(),
            adapterType = "agv",
            externalTaskId,
            payloadJson = """{"step":1}""",
        });
        await PostJsonAndAssertOkAsync(client, $"/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail", new
        {
            externalTaskId,
            failureCode = "PLC_TIMEOUT",
            failureMessage = "PLC timeout",
        });
        await PostJsonAndAssertOkAsync(client, $"/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", new
        {
            warehouseTaskId = warehouseTaskId.ToString(),
            adapterType = "agv",
            externalTaskId = retryExternalTaskId,
            payloadJson = """{"step":2}""",
        });
        await PostJsonAndAssertOkAsync(client, $"/api/business/v1/wms/wcs-tasks/{retryExternalTaskId}/complete", new
        {
            externalTaskId = retryExternalTaskId,
            completionPayloadJson = """{"ok":true}""",
        });

        var diagnostics = await client.GetStringAsync($"/api/business/v1/wms/wcs-tasks?OrganizationId=org-acceptance&EnvironmentId=env-acceptance&ExternalTaskId={retryExternalTaskId}&WarehouseTaskId={warehouseTaskId}");

        Assert.Contains("Completed", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PLC_TIMEOUT", diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wcs_task_query_exposes_failure_retry_and_completion_diagnostics()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = WarehouseTask.CreatePutaway("org-001", "env-dev", "WT-001", "IN-001", "10", "SKU-FG-1000", "kg", "SITE-01", "RECV-01", "STAGE-01", 10m);
        var otherTenantTask = WarehouseTask.CreatePutaway("org-002", "env-dev", "WT-001", "IN-002", "10", "SKU-FG-1000", "kg", "SITE-01", "RECV-01", "STAGE-01", 10m);
        dbContext.WarehouseTasks.AddRange(warehouseTask, otherTenantTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var commandHandler = new Application.Commands.DispatchWcsTaskCommandHandler(dbContext);
        await commandHandler.Handle(new Application.Commands.DispatchWcsTaskCommand(warehouseTask.Id, "agv", "EXT-001", """{"step":1}"""), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new Application.Commands.FailWcsTaskCommandHandler(dbContext).Handle(new Application.Commands.FailWcsTaskCommand("EXT-001", "PLC_TIMEOUT", "PLC timeout"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await commandHandler.Handle(new Application.Commands.DispatchWcsTaskCommand(warehouseTask.Id, "agv", "EXT-002", """{"step":2}"""), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new Application.Commands.CompleteWcsTaskCommandHandler(dbContext).Handle(new Application.Commands.CompleteWcsTaskCommand("EXT-002", """{"ok":true}"""), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await commandHandler.Handle(new Application.Commands.DispatchWcsTaskCommand(otherTenantTask.Id, "agv", "EXT-003", """{"step":3}"""), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListWcsTasksQueryHandler(dbContext).Handle(
            new ListWcsTasksQuery("org-001", "env-dev", null),
            CancellationToken.None);

        var fact = Assert.Single(result.Items);
        Assert.Equal("EXT-002", fact.ExternalTaskId);
        Assert.Equal("Completed", fact.Status);
        Assert.Equal(2, fact.AttemptCount);
        Assert.Equal("PLC_TIMEOUT", fact.FailureCode);
        Assert.Equal("PLC timeout", fact.FailureMessage);
        Assert.Equal("org-001", fact.OrganizationId);
        Assert.Equal("env-dev", fact.EnvironmentId);
        Assert.NotNull(fact.CompletedAtUtc);
    }

    [Fact]
    public async Task Inbound_order_query_filters_status_keyword_before_offset_page_and_total_count()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.InboundOrders.AddRange(
            CreateInboundOrder("IN-PAGE-001"),
            CreateInboundOrder("IN-PAGE-002"),
            CreateInboundOrder("IN-OTHER-001"),
            CreateInboundOrder("IN-PAGE-CLOSED"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListInboundOrdersQueryHandler(dbContext).Handle(
            new ListInboundOrdersQuery("org-001", "env-dev", 1, 1, "Open", "page"),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("IN-PAGE-001", item.InboundOrderNo);
        Assert.Equal("Open", item.Status);
    }

    [Fact]
    public async Task Outbound_order_query_filters_status_keyword_before_offset_page_and_total_count()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.OutboundOrders.AddRange(
            CreateOutboundOrder("OUT-PAGE-001"),
            CreateOutboundOrder("OUT-PAGE-002"),
            CreateOutboundOrder("OUT-OTHER-001"),
            CreateOutboundOrder("OUT-PAGE-CLOSED"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListOutboundOrdersQueryHandler(dbContext).Handle(
            new ListOutboundOrdersQuery("org-001", "env-dev", 1, 1, "Open", "page"),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("OUT-PAGE-001", item.OutboundOrderNo);
        Assert.Equal("Open", item.Status);
    }

    [Fact]
    public async Task Wcs_task_query_filters_status_failed_keyword_before_offset_page_and_total_count()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = WarehouseTask.CreatePutaway("org-001", "env-dev", "WT-PAGE-001", "IN-PAGE-001", "10", "SKU-001", "pcs", "SITE-01", "RECV-01", "STAGE-01", 3m);
        dbContext.WarehouseTasks.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var failedA = WcsTask.Dispatch("org-001", "env-dev", warehouseTask.Id, "agv", "EXT-PAGE-001", """{"step":1}""");
        failedA.Fail("PLC_TIMEOUT", "PLC timeout");
        var failedB = WcsTask.Dispatch("org-001", "env-dev", warehouseTask.Id, "agv", "EXT-PAGE-002", """{"step":2}""");
        failedB.Fail("PLC_TIMEOUT", "PLC timeout");
        var completed = WcsTask.Dispatch("org-001", "env-dev", warehouseTask.Id, "agv", "EXT-PAGE-COMPLETE", """{"step":3}""");
        completed.Complete("""{"ok":true}""");
        dbContext.WcsTasks.AddRange(failedA, failedB, completed);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListWcsTasksQueryHandler(dbContext).Handle(
            new ListWcsTasksQuery("org-001", "env-dev", null, null, 1, 1, "Failed", true, "page"),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("EXT-PAGE-001", item.ExternalTaskId);
        Assert.Equal("Failed", item.Status);
        Assert.NotNull(item.FailedAtUtc);
    }

    [Fact]
    public async Task Warehouse_task_query_filters_type_status_location_keyword_before_offset_page_and_total_count()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WarehouseTasks.AddRange(
            WarehouseTask.CreatePutaway("org-001", "env-dev", "PUT-PAGE-001", "IN-001", "10", "SKU-001", "pcs", "SITE-01", "RECV-01", "BIN-A", 3m),
            WarehouseTask.CreatePutaway("org-001", "env-dev", "PUT-PAGE-002", "IN-002", "10", "SKU-001", "pcs", "SITE-01", "RECV-02", "BIN-A", 3m),
            WarehouseTask.CreatePutaway("org-001", "env-dev", "PUT-OTHER-001", "IN-003", "10", "SKU-001", "pcs", "SITE-01", "RECV-01", "BIN-B", 3m),
            WarehouseTask.CreatePicking("org-001", "env-dev", "PICK-PAGE-001", "OUT-001", "10", "SKU-001", "pcs", "SITE-01", "BIN-A", "SHIP-01", 3m),
            WarehouseTask.CreatePutaway("org-002", "env-dev", "PUT-PAGE-003", "IN-004", "10", "SKU-001", "pcs", "SITE-01", "RECV-01", "BIN-A", 3m));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListWarehouseTasksQueryHandler(dbContext).Handle(
            new ListWarehouseTasksQuery("org-001", "env-dev", WarehouseTaskType.Putaway, 1, 1, "Open", "BIN-A", null, "page"),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("PUT-PAGE-001", item.TaskNo);
        Assert.Equal("Putaway", item.TaskType);
        Assert.Equal("Open", item.Status);
        Assert.Equal("BIN-A", item.ToLocationCode);
    }

    [Fact]
    public async Task Warehouse_task_query_returns_empty_when_operator_filter_is_supplied_until_assignment_field_exists()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WarehouseTasks.Add(
            WarehouseTask.CreatePutaway("org-001", "env-dev", "PUT-OPERATOR-001", "IN-001", "10", "SKU-001", "pcs", "SITE-01", "RECV-01", "BIN-A", 3m));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListWarehouseTasksQueryHandler(dbContext).Handle(
            new ListWarehouseTasksQuery("org-001", "env-dev", WarehouseTaskType.Putaway, 0, 100, null, null, "user-001", null),
            CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Count_execution_query_filters_status_location_keyword_before_offset_page_and_total_count()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var completed = CountExecution.Create("org-001", "env-dev", "COUNT-PAGE-CLOSED", "SKU-001", "pcs", "SITE-01", "BIN-A", 3m);
        completed.Complete(2m);
        dbContext.CountExecutions.AddRange(
            CountExecution.Create("org-001", "env-dev", "COUNT-PAGE-001", "SKU-001", "pcs", "SITE-01", "BIN-A", 3m),
            CountExecution.Create("org-001", "env-dev", "COUNT-PAGE-002", "SKU-001", "pcs", "SITE-01", "BIN-A", 3m),
            CountExecution.Create("org-001", "env-dev", "COUNT-OTHER-001", "SKU-001", "pcs", "SITE-01", "BIN-B", 3m),
            completed,
            CountExecution.Create("org-002", "env-dev", "COUNT-PAGE-003", "SKU-001", "pcs", "SITE-01", "BIN-A", 3m));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListCountExecutionsQueryHandler(dbContext).Handle(
            new ListCountExecutionsQuery("org-001", "env-dev", 1, 1, "Open", "BIN-A", "page"),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("COUNT-PAGE-001", item.CountNo);
        Assert.Equal("Open", item.Status);
        Assert.Equal("BIN-A", item.LocationCode);
        Assert.Null(item.CountedQuantity);
    }

    [Fact]
    public async Task Wms_list_queries_reject_numeric_status_filters()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inboundOrder = CreateInboundOrder("IN-NUMERIC-001");
        var outboundOrder = CreateOutboundOrder("OUT-NUMERIC-001");
        var warehouseTask = WarehouseTask.CreatePutaway("org-001", "env-dev", "WT-NUMERIC-001", "IN-NUMERIC-001", "10", "SKU-001", "pcs", "SITE-01", "RECV-01", "STAGE-01", 3m);
        dbContext.InboundOrders.Add(inboundOrder);
        dbContext.OutboundOrders.Add(outboundOrder);
        dbContext.WarehouseTasks.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var wcsTask = WcsTask.Dispatch("org-001", "env-dev", warehouseTask.Id, "agv", "EXT-NUMERIC-001", """{"step":1}""");
        wcsTask.Fail("PLC_TIMEOUT", "PLC timeout");
        dbContext.WcsTasks.Add(wcsTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var inboundResult = await new ListInboundOrdersQueryHandler(dbContext).Handle(
            new ListInboundOrdersQuery("org-001", "env-dev", 0, 100, "0", null),
            CancellationToken.None);
        var outboundResult = await new ListOutboundOrdersQueryHandler(dbContext).Handle(
            new ListOutboundOrdersQuery("org-001", "env-dev", 0, 100, "0", null),
            CancellationToken.None);
        var warehouseResult = await new ListWarehouseTasksQueryHandler(dbContext).Handle(
            new ListWarehouseTasksQuery("org-001", "env-dev", WarehouseTaskType.Putaway, 0, 100, "2", null, null, null),
            CancellationToken.None);
        var countResult = await new ListCountExecutionsQueryHandler(dbContext).Handle(
            new ListCountExecutionsQuery("org-001", "env-dev", 0, 100, "0", null, null),
            CancellationToken.None);
        var wcsResult = await new ListWcsTasksQueryHandler(dbContext).Handle(
            new ListWcsTasksQuery("org-001", "env-dev", null, null, 0, 100, "2", null, null),
            CancellationToken.None);

        Assert.Equal(0, inboundResult.Total);
        Assert.Empty(inboundResult.Items);
        Assert.Equal(0, outboundResult.Total);
        Assert.Empty(outboundResult.Items);
        Assert.Equal(0, warehouseResult.Total);
        Assert.Empty(warehouseResult.Items);
        Assert.Equal(0, countResult.Total);
        Assert.Empty(countResult.Items);
        Assert.Equal(0, wcsResult.Total);
        Assert.Empty(wcsResult.Items);
    }

    public static IEnumerable<object[]> EndpointTypes()
    {
        return WmsEndpointContracts.All.Select(x => new object[] { x.EndpointType });
    }

    private static WebApplicationFactory<Program> CreateAuthorizedFactory()
    {
        return new WmsLiveHttpTestFactory();
    }

    private static async Task PostJsonAndAssertOkAsync(HttpClient client, string route, object request)
    {
        var response = await client.PostAsJsonAsync(route, request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected successful WMS live HTTP response from {route}, got {(int)response.StatusCode}: {body}");
    }

    private sealed class WmsLiveHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"wms-live-http-acceptance-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.UseSetting("Inventory:BaseUrl", "http://inventory.local");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDistributedLock>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddInMemoryDistributedLock();
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options
                        .UseInMemoryDatabase(databaseName)
                        .UseInternalServiceProvider(efServices)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                efServices.Dispose();
            }
        }
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private static InboundOrder CreateInboundOrder(string orderNo)
    {
        var order = InboundOrder.Create(
            "org-001",
            "env-dev",
            orderNo,
            "purchase-receipt",
            $"PO-{orderNo}",
            "SITE-01",
            [new InboundOrderLineDraft("10", "SKU-001", "pcs", 3m, "STAGE-01", null, null, "qualified", "company", null)]);
        if (orderNo.Contains("CLOSED", StringComparison.Ordinal))
        {
            order.Complete($"idem-{orderNo}");
        }

        return order;
    }

    private static OutboundOrder CreateOutboundOrder(string orderNo)
    {
        var order = OutboundOrder.Create(
            "org-001",
            "env-dev",
            orderNo,
            "sales-shipment",
            $"SO-{orderNo}",
            "SITE-01",
            [new OutboundOrderLineDraft("10", "SKU-001", "pcs", 3m, "BIN-01", null, null, "qualified", "company", null)]);
        if (orderNo.Contains("CLOSED", StringComparison.Ordinal))
        {
            order.CompletePackReview($"PACK-{orderNo}", true, $"idem-{orderNo}");
        }

        return order;
    }
}

internal static class WmsTestProvider
{
    public static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"wms-wcs-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
