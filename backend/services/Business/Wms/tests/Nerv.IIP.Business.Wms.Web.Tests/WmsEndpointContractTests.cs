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
using Nerv.IIP.ServiceAuth;
using WarehouseTask = Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate.WarehouseTask;
using WarehouseTaskId = Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate.WarehouseTaskId;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsEndpointContractTests
{
    [Fact]
    public void Wms_endpoints_expose_issue_136_routes_permissions_policies_and_operation_ids()
    {
        var contracts = WmsEndpointContracts.All.ToArray();

        Assert.Equal(14, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsInboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/inbound-orders" && x.PermissionCode == WmsPermissionCodes.ReceiptsRead && x.OperationId == "listWmsInboundOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsPutawayTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "completeWmsInboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "createWmsOutboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/wms/outbound-orders" && x.PermissionCode == WmsPermissionCodes.ShipmentsRead && x.OperationId == "listWmsOutboundOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "createWmsPickingTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete" && x.PermissionCode == WmsPermissionCodes.ShipmentsManage && x.OperationId == "completeWmsOutboundOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/wms/count-executions" && x.PermissionCode == WmsPermissionCodes.ReceiptsManage && x.OperationId == "createWmsCountExecution");
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
    public async Task Wms_live_http_acceptance_host_dispatches_fails_retries_and_completes_wcs_task()
    {
        await using var factory = CreateAuthorizedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        WarehouseTaskId warehouseTaskId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var warehouseTask = WarehouseTask.CreatePutaway("org-acceptance", "env-acceptance", $"WT-{suffix}", $"IN-WCS-{suffix}", "10", $"SKU-WCS-{suffix}", "pcs", "SITE-01", "RECV-01", "STAGE-01", 3m);
            dbContext.WarehouseTasks.Add(warehouseTask);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            warehouseTaskId = warehouseTask.Id;
            var commandHandler = new Application.Commands.DispatchWcsTaskCommandHandler(dbContext);
            await commandHandler.Handle(new Application.Commands.DispatchWcsTaskCommand(warehouseTask.Id, "agv", $"WCS-{suffix}-1", """{"step":1}"""), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new Application.Commands.FailWcsTaskCommandHandler(dbContext).Handle(new Application.Commands.FailWcsTaskCommand($"WCS-{suffix}-1", "PLC_TIMEOUT", "PLC timeout"), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await commandHandler.Handle(new Application.Commands.DispatchWcsTaskCommand(warehouseTask.Id, "agv", $"WCS-{suffix}-2", """{"step":2}"""), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new Application.Commands.CompleteWcsTaskCommandHandler(dbContext).Handle(new Application.Commands.CompleteWcsTaskCommand($"WCS-{suffix}-2", """{"ok":true}"""), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var diagnostics = await client.GetStringAsync($"/api/business/v1/wms/wcs-tasks?OrganizationId=org-acceptance&EnvironmentId=env-acceptance&ExternalTaskId=WCS-{suffix}-2&WarehouseTaskId={warehouseTaskId}");

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

    public static IEnumerable<object[]> EndpointTypes()
    {
        return WmsEndpointContracts.All.Select(x => new object[] { x.EndpointType });
    }

    private static WebApplicationFactory<Program> CreateAuthorizedFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.ConfigureTestServices(services =>
                {
                    var databaseName = $"wms-live-http-acceptance-{Guid.NewGuid():N}";
                    var efServices = new ServiceCollection()
                        .AddEntityFrameworkInMemoryDatabase()
                        .BuildServiceProvider();

                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options =>
                        options
                            .UseInMemoryDatabase(databaseName)
                            .UseInternalServiceProvider(efServices)
                            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                });
            });
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
