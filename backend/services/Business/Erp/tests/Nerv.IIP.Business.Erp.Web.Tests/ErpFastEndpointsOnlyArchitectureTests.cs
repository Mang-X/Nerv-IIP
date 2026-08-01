extern alias WmsWeb;

using FastEndpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using WmsListInboundOrdersEndpoint = WmsWeb::Nerv.IIP.Business.Wms.Web.Endpoints.Wms.ListInboundOrdersEndpoint;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ErpFastEndpointsOnlyArchitectureTests
{
    [Fact]
    public void Erp_web_assembly_does_not_define_mvc_controllers()
    {
        var controllerTypes = typeof(Program).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract
                && (typeof(ControllerBase).IsAssignableFrom(type)
                    || type.GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true).Length > 0))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(controllerTypes);
    }

    [Fact]
    public void Erp_host_registers_only_erp_fastendpoints_without_mvc_endpoint_data_sources()
    {
        Assert.NotEqual(typeof(Program).Assembly, typeof(WmsListInboundOrdersEndpoint).Assembly);

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();

        var endpointDataSources = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
        var routePatterns = endpointDataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);
        var missingContractRoutes = ErpEndpointContracts.All
            .Select(contract => contract.Route)
            .Distinct(StringComparer.Ordinal)
            .Where(route => !routePatterns.Contains(route))
            .ToArray();

        Assert.Null(factory.Services.GetService<IControllerFactory>());
        Assert.DoesNotContain(endpointDataSources, source =>
            source.GetType().FullName?.Contains("ControllerActionEndpointDataSource", StringComparison.Ordinal) == true);
        Assert.Empty(missingContractRoutes);
        Assert.DoesNotContain("/api/business/v1/wms/inbound-orders", routePatterns);
    }

    [Fact]
    public void Every_erp_public_contract_is_backed_by_a_fastendpoint_type()
    {
        var contracts = ErpEndpointContracts.All;

        Assert.NotEmpty(contracts);
        Assert.DoesNotContain(contracts, contract => contract.EndpointType is null);

        var nonFastEndpointContracts = contracts
            .Where(contract => !IsFastEndpointType(contract.EndpointType))
            .Select(contract => $"{contract.HttpMethod} {contract.Route} -> {contract.EndpointType.FullName}")
            .ToArray();

        Assert.Empty(nonFastEndpointContracts);
    }

    private static bool IsFastEndpointType(Type endpointType)
    {
        return typeof(IEndpoint).IsAssignableFrom(endpointType);
    }
}
