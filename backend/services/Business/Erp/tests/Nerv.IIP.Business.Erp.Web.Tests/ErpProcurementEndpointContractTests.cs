using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpProcurementEndpointContractTests
{
    [Fact]
    public void Erp_procurement_endpoints_expose_issue_137_routes_permissions_policies_and_operation_ids()
    {
        var contracts = ErpProcurementEndpointContracts.All.ToArray();

        Assert.Equal(6, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/purchase-requisitions/from-suggestion"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createErpPurchaseRequisitionFromSuggestion");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/rfqs"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createErpRequestForQuotation");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/supplier-quotations"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "receiveErpSupplierQuotation");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/purchase-orders"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createErpPurchaseOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/purchase-receipts"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "recordErpPurchaseReceipt");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/erp/purchase-orders"
            && x.PermissionCode == ErpPermissionCodes.ProcurementRead
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listErpPurchaseOrders");
    }

    [Theory]
    [InlineData(typeof(CreatePurchaseRequisitionFromSuggestionEndpoint))]
    [InlineData(typeof(CreateRequestForQuotationEndpoint))]
    [InlineData(typeof(ReceiveSupplierQuotationEndpoint))]
    [InlineData(typeof(CreatePurchaseOrderEndpoint))]
    [InlineData(typeof(RecordPurchaseReceiptEndpoint))]
    [InlineData(typeof(ListPurchaseOrdersEndpoint))]
    public void Erp_procurement_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task List_purchase_orders_query_returns_procurement_projection()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreatePurchaseOrderCommandHandler(dbContext);
        await handler.Handle(new CreatePurchaseOrderCommand(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListPurchaseOrdersQueryHandler(dbContext).Handle(
            new ListPurchaseOrdersQuery("org-001", "env-dev"), CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Equal("PO-001", response.Items.Single().PurchaseOrderNo);
        Assert.Equal(36m, response.Items.Single().TotalAmount);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"erp-procurement-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }
}
