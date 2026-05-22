using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.ProductionVersions;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.ProductionVersions;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductionVersions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductionVersionApiContractTests
{
    [Fact]
    public void Production_version_endpoint_contracts_cover_mes_resolution_surface()
    {
        var contracts = ProductionVersionEndpointContracts.All;

        Assert.Equal(5, contracts.Count);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/production-versions/resolve");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/production-versions");
        Assert.All(contracts, contract =>
        {
            Assert.StartsWith("/api/business/v1/engineering/production-versions", contract.Route, StringComparison.Ordinal);
            Assert.Contains(contract.PermissionCode, EngineeringPermissionCodes.All);
            Assert.Matches("^[a-z][A-Za-z0-9]*$", contract.OperationId);
            Assert.Contains("ProductionVersion", contract.OperationId, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(typeof(CreateProductionVersionEndpoint))]
    [InlineData(typeof(UpdateProductionVersionEndpoint))]
    [InlineData(typeof(ArchiveProductionVersionEndpoint))]
    [InlineData(typeof(ListProductionVersionsEndpoint))]
    [InlineData(typeof(ResolveProductionVersionEndpoint))]
    public void Production_version_endpoints_route_through_mediator(Type endpointType)
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
    public async Task Create_command_rejects_overlapping_default_for_same_sku()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductionVersions.Add(ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-A",
            "routing-A",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateProductionVersionCommandHandler(new ProductionVersionRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateProductionVersionCommand(
                "org-001",
                "env-dev",
                "SKU-FG-1000",
                "mbom-B",
                "routing-B",
                new DateOnly(2026, 6, 1),
                null,
                null,
                null,
                20,
                true,
                EngineeringVersionStatus.Published,
                EngineeringVersionStatus.Published),
            CancellationToken.None));
        Assert.Contains("default", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_query_returns_active_version_binding_for_mes_work_order_creation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductionVersions.Add(ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-low-volume",
            "routing-cell",
            new DateOnly(2026, 1, 1),
            null,
            1m,
            50m,
            20,
            false,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));
        dbContext.ProductionVersions.Add(ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-default",
            "routing-default",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ResolveProductionVersionQueryHandler(dbContext);

        var response = await handler.Handle(
            new ResolveProductionVersionQuery("org-001", "env-dev", "SKU-FG-1000", new DateOnly(2026, 6, 1), 24m),
            CancellationToken.None);

        Assert.Equal("SKU-FG-1000", response.SkuCode);
        Assert.Equal("mbom-low-volume", response.MbomVersionId);
        Assert.Equal("routing-cell", response.RoutingVersionId);
        Assert.Equal("active", response.Status);
        Assert.False(string.IsNullOrWhiteSpace(response.ProductionVersionId));
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"product-engineering-api-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
