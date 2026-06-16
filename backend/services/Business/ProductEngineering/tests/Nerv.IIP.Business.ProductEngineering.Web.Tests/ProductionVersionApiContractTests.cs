using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
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

    [Theory]
    [InlineData(typeof(CreateProductionVersionCommand))]
    [InlineData(typeof(UpdateProductionVersionCommand))]
    public void Production_version_commands_do_not_expose_internal_binding_statuses(Type commandType)
    {
        var propertyNames = commandType
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("MbomStatus", propertyNames);
        Assert.DoesNotContain("RoutingStatus", propertyNames);
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

        var handler = new CreateProductionVersionCommandHandler(
            new ProductionVersionRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext));

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
                true),
            CancellationToken.None));
        Assert.Contains("effective window", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_command_rejects_overlapping_non_default_for_same_sku()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductionVersions.Add(ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "MBOM-1000:A",
            "ROUTE-1000:A",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            null,
            null,
            10,
            false,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));
        dbContext.ManufacturingBoms.Add(ReleasedManufacturingBom("MBOM-1000", "B", "SKU-FG-1000", new DateOnly(2026, 1, 1)));
        dbContext.Routings.Add(ReleasedRouting("ROUTE-1000", "B", "SKU-FG-1000", new DateOnly(2026, 1, 1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CreateProductionVersionCommandHandler(
            new ProductionVersionRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCreateCommand("MBOM-1000:B", "ROUTE-1000:B", validFrom: new DateOnly(2026, 6, 1)),
            CancellationToken.None));

        Assert.Contains("effective window", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_command_rejects_missing_or_unpublished_mbom_and_routing_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ManufacturingBoms.Add(ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-DRAFT", "A", "SKU-FG-1000")
            .AddMaterialLine("SKU-RM-1000", 1m, "EA", 0m));
        dbContext.Routings.Add(ReleasedRouting("ROUTE-1000", "A", "SKU-FG-1000", new DateOnly(2026, 1, 1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CreateProductionVersionCommandHandler(
            new ProductionVersionRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext));

        var missing = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCreateCommand("MBOM-MISSING:A", "ROUTE-1000:A"),
            CancellationToken.None));
        var draft = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCreateCommand("MBOM-DRAFT:A", "ROUTE-1000:A"),
            CancellationToken.None));

        Assert.Contains("MBOM", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("published", draft.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_command_rejects_mismatched_sku_and_effectivity_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ManufacturingBoms.Add(ReleasedManufacturingBom("MBOM-OTHER", "A", "SKU-FG-OTHER", new DateOnly(2026, 1, 1)));
        dbContext.ManufacturingBoms.Add(ReleasedManufacturingBom("MBOM-VALID", "A", "SKU-FG-1000", new DateOnly(2026, 7, 1)));
        dbContext.Routings.Add(ReleasedRouting("ROUTE-OTHER", "A", "SKU-FG-OTHER", new DateOnly(2026, 1, 1)));
        dbContext.Routings.Add(ReleasedRouting("ROUTE-VALID", "A", "SKU-FG-1000", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CreateProductionVersionCommandHandler(
            new ProductionVersionRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext));

        var skuMismatch = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCreateCommand("MBOM-OTHER:A", "ROUTE-OTHER:A"),
            CancellationToken.None));
        var notEffective = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCreateCommand("MBOM-VALID:A", "ROUTE-VALID:A", validFrom: new DateOnly(2026, 6, 1)),
            CancellationToken.None));

        Assert.Contains("SKU", skuMismatch.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("effective", notEffective.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_command_rejects_cross_tenant_production_version_id()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var foreignProductionVersion = ProductionVersion.Create(
            "org-002",
            "env-dev",
            "SKU-FG-1000",
            "MBOM-FOREIGN:A",
            "ROUTE-FOREIGN:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            false,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.Add(foreignProductionVersion);
        dbContext.ManufacturingBoms.Add(ReleasedManufacturingBom("MBOM-FOREIGN", "B", "SKU-FG-1000", new DateOnly(2026, 1, 1), "org-002"));
        dbContext.Routings.Add(ReleasedRouting("ROUTE-FOREIGN", "B", "SKU-FG-1000", new DateOnly(2026, 1, 1), "org-002"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new UpdateProductionVersionCommandHandler(
            new ProductionVersionRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new UpdateProductionVersionCommand(
                "org-001",
                "env-dev",
                foreignProductionVersion.Id.Id.ToString("D"),
                "MBOM-FOREIGN:B",
                "ROUTE-FOREIGN:B",
                new DateOnly(2026, 6, 1),
                null,
                null,
                null,
                20,
                false),
            CancellationToken.None));

        Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("MBOM-FOREIGN:A", foreignProductionVersion.MbomVersionId);
        Assert.Equal(10, foreignProductionVersion.Priority);
    }

    [Fact]
    public async Task Archive_command_rejects_cross_tenant_production_version_id()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var foreignProductionVersion = ProductionVersion.Create(
            "org-002",
            "env-dev",
            "SKU-FG-1000",
            "MBOM-FOREIGN:A",
            "ROUTE-FOREIGN:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            false,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.Add(foreignProductionVersion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ArchiveProductionVersionCommandHandler(new ProductionVersionRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ArchiveProductionVersionCommand("org-001", "env-dev", foreignProductionVersion.Id.Id.ToString("D"), "review isolation"),
            CancellationToken.None));

        Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProductionVersionStatus.Active, foreignProductionVersion.Status);
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

    [Fact]
    public async Task List_production_versions_returns_offset_page_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductionVersions.AddRange(
            NewProductionVersion("SKU-FG-1000", "mbom-1", "routing-1", 10),
            NewProductionVersion("SKU-FG-2000", "mbom-2", "routing-2", 20),
            NewProductionVersion("SKU-FG-3000", "mbom-3", "routing-3", 30));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListProductionVersionsQueryHandler(dbContext).Handle(
            new ListProductionVersionsQuery("org-001", "env-dev", null, null, Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("SKU-FG-2000", item.SkuCode);

        var firstPage = await new ListProductionVersionsQueryHandler(dbContext).Handle(
            new ListProductionVersionsQuery("org-001", "env-dev", null, null, Skip: -10, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, firstPage.Total);
        Assert.Equal("SKU-FG-1000", Assert.Single(firstPage.Items).SkuCode);
    }

    private static ProductionVersion NewProductionVersion(string skuCode, string mbomVersionId, string routingVersionId, int priority)
    {
        return ProductionVersion.Create(
            "org-001",
            "env-dev",
            skuCode,
            mbomVersionId,
            routingVersionId,
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            priority,
            false,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
    }

    private static CreateProductionVersionCommand NewCreateCommand(
        string mbomVersionId,
        string routingVersionId,
        DateOnly? validFrom = null)
    {
        return new CreateProductionVersionCommand(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            mbomVersionId,
            routingVersionId,
            validFrom ?? new DateOnly(2026, 6, 1),
            null,
            null,
            null,
            10,
            false);
    }

    private static ManufacturingBom ReleasedManufacturingBom(
        string bomCode,
        string revision,
        string skuCode,
        DateOnly effectiveDate,
        string organizationId = "org-001")
    {
        var bom = ManufacturingBom.CreateDraft(organizationId, "env-dev", bomCode, revision, skuCode)
            .AddMaterialLine("SKU-RM-1000", 1m, "EA", 0m);
        bom.ReleaseFromEngineeringBom("EBOM-1000:A", EngineeringVersionStatus.Published, effectiveDate);
        return bom;
    }

    private static Routing ReleasedRouting(
        string routingCode,
        string revision,
        string skuCode,
        DateOnly effectiveDate,
        string organizationId = "org-001")
    {
        var routing = Routing.CreateDraft(organizationId, "env-dev", routingCode, revision, skuCode)
            .AddOperation(10, "WC-MIX-01", "mixing", "Mix", 30);
        routing.Release(effectiveDate);
        return routing;
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
