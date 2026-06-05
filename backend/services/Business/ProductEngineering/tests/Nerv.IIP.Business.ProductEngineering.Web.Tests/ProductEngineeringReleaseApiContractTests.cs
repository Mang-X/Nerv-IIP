using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductEngineering;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductionVersions;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringReleaseApiContractTests
{
    [Fact]
    public void Product_engineering_release_endpoint_contracts_cover_issue_127_surface()
    {
        var contracts = ProductEngineeringEndpointContracts.All;

        Assert.Equal(9, contracts.Count);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/documents" && x.PermissionCode == EngineeringPermissionCodes.DocumentsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/items" && x.PermissionCode == EngineeringPermissionCodes.ItemsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-boms/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/manufacturing-boms/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/routings/release" && x.PermissionCode == EngineeringPermissionCodes.RoutingsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-changes/release" && x.PermissionCode == EngineeringPermissionCodes.ChangesManage);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/manufacturing-boms" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/routings" && x.PermissionCode == EngineeringPermissionCodes.RoutingsRead);
        Assert.All(contracts, contract =>
        {
            Assert.StartsWith("/api/business/v1/engineering", contract.Route, StringComparison.Ordinal);
            Assert.Contains(contract.PermissionCode, EngineeringPermissionCodes.All);
            Assert.Matches("^[a-z][A-Za-z0-9]*$", contract.OperationId);
            Assert.Contains("Business", contract.OperationId, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Product_engineering_business_endpoints_require_internal_service_authorization_policy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var contracts = ProductEngineeringEndpointContracts.All
            .Select(contract => (contract.EndpointType, contract.Route))
            .Concat(ProductionVersionEndpointContracts.All.Select(contract => (contract.EndpointType, contract.Route)))
            .ToArray();

        var failures = contracts
            .Where(contract => !HasInternalServicePolicy(endpoints, contract.Route))
            .Select(contract => $"{contract.EndpointType.Name} is missing {InternalServiceAuthorizationPolicy.Name}.")
            .ToArray();

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(typeof(RegisterEngineeringDocumentEndpoint))]
    [InlineData(typeof(CreateEngineeringItemRevisionEndpoint))]
    [InlineData(typeof(ReleaseEngineeringBomEndpoint))]
    [InlineData(typeof(ReleaseManufacturingBomEndpoint))]
    [InlineData(typeof(ReleaseRoutingEndpoint))]
    [InlineData(typeof(ReleaseEngineeringChangeEndpoint))]
    [InlineData(typeof(ListEngineeringBomsEndpoint))]
    [InlineData(typeof(ListManufacturingBomsEndpoint))]
    [InlineData(typeof(ListRoutingsEndpoint))]
    public void Product_engineering_release_endpoints_route_through_mediator(Type endpointType)
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
    public async Task List_manufacturing_boms_returns_released_material_lines_for_mrp_snapshots()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000")
            .AddMaterialLine("SKU-RM-1000", 3m, "pcs", 0m);
        bom.ReleaseFromEngineeringBom("EBOM-1000:A", EngineeringVersionStatus.Published, new DateOnly(2026, 6, 1));
        dbContext.ManufacturingBoms.Add(bom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListManufacturingBomsQueryHandler(dbContext).Handle(
            new ListManufacturingBomsQuery("org-001", "env-dev", "SKU-FG-1000", "Published"),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("MBOM-1000", item.BomCode);
        var line = Assert.Single(item.MaterialLines);
        Assert.Equal("SKU-RM-1000", line.SkuCode);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal("pcs", line.UnitOfMeasureCode);
    }

    [Fact]
    public async Task List_manufacturing_boms_returns_offset_page_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ManufacturingBoms.AddRange(
            ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-001", "A", "SKU-FG-1000"),
            ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-002", "A", "SKU-FG-1000"),
            ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-003", "A", "SKU-FG-1000"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListManufacturingBomsQueryHandler(dbContext).Handle(
            new ListManufacturingBomsQuery("org-001", "env-dev", "SKU-FG-1000", null, Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("MBOM-002", item.BomCode);
    }

    [Fact]
    public void Release_command_validators_reject_blank_scope_and_file_references()
    {
        var documentValidator = new RegisterEngineeringDocumentCommandValidator();
        var documentResult = documentValidator.Validate(new RegisterEngineeringDocumentCommand(
            "",
            "env-dev",
            "DOC-1000",
            "A",
            "",
            "pump.dwg",
            "application/dwg",
            "cad-drawing"));

        Assert.False(documentResult.IsValid);
        Assert.Contains(documentResult.Errors, x => IsValidationFailureFor(x, nameof(RegisterEngineeringDocumentCommand.OrganizationId)));
        Assert.Contains(documentResult.Errors, x => IsValidationFailureFor(x, nameof(RegisterEngineeringDocumentCommand.FileId)));

        var bomValidator = new ReleaseEngineeringBomCommandValidator();
        var bomResult = bomValidator.Validate(new ReleaseEngineeringBomCommand(
            "org-001",
            "",
            "EBOM-1000",
            "A",
            "ENG-1000",
            new DateOnly(2026, 6, 1),
            []));

        Assert.False(bomResult.IsValid);
        Assert.Contains(bomResult.Errors, x => IsValidationFailureFor(x, nameof(ReleaseEngineeringBomCommand.EnvironmentId)));
        Assert.Contains(bomResult.Errors, x => IsValidationFailureFor(x, nameof(ReleaseEngineeringBomCommand.Lines)));
    }

    [Fact]
    public async Task Release_manufacturing_bom_rejects_duplicate_business_key_and_stores_unique_ebom_version_reference()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-1000", "A", "ENG-1000")
            .AddLine("ENG-1001", 2m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        dbContext.ManufacturingBoms.Add(ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        var command = new ReleaseManufacturingBomCommand(
            "org-001",
            "env-dev",
            "MBOM-1000",
            "A",
            "SKU-FG-1000",
            "EBOM-1000",
            "A",
            new DateOnly(2026, 6, 1),
            [new ManufacturingBomMaterialLineCommand("SKU-RM-1000", 1.5m, "KG", 0m)],
            []);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);

        var createdCommand = command with { Revision = "B" };
        await handler.Handle(createdCommand, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await dbContext.ManufacturingBoms.SingleAsync(x => x.BomCode == "MBOM-1000" && x.Revision == "B", CancellationToken.None);
        Assert.Equal("EBOM-1000:A", created.EngineeringBomVersionId);
    }

    [Fact]
    public async Task Release_routing_rejects_duplicate_business_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-1000", "A", "SKU-FG-1000")
            .AddOperation(10, "WC-MIX-01", "Mix", 30);
        existing.Release(new DateOnly(2026, 6, 1));
        dbContext.Routings.Add(existing);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseRoutingCommandHandler(new RoutingRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseRoutingCommand(
                "org-001",
                "env-dev",
                "ROUTE-1000",
                "A",
                "SKU-FG-1000",
                new DateOnly(2026, 6, 1),
                [new RoutingOperationCommand(10, "WC-MIX-01", "Mix", 30)]),
            CancellationToken.None));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_engineering_document_generates_number_and_replays_idempotent_create()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var numbering = new ProductEngineeringNumberingService();
        var handler = new RegisterEngineeringDocumentCommandHandler(new EngineeringDocumentRepository(dbContext), numbering);
        var command = new RegisterEngineeringDocumentCommand(
            "org-001",
            "env-dev",
            null,
            "A",
            "file-001",
            "shock-absorber.dwg",
            "application/dwg",
            "cad-drawing",
            "engineering-document-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Matches("^EDOC-[0-9]{8}-[0-9]{6}$", first.Id);
        Assert.Single(dbContext.EngineeringDocuments);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"product-engineering-release-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static bool IsValidationFailureFor(FluentValidation.Results.ValidationFailure failure, string propertyName)
    {
        return NormalizeValidationPropertyName(failure.PropertyName) == NormalizeValidationPropertyName(propertyName);
    }

    private static bool HasInternalServicePolicy(IEnumerable<RouteEndpoint> endpoints, string route)
    {
        return endpoints
            .Where(endpoint => string.Equals(endpoint.RoutePattern.RawText, route, StringComparison.Ordinal))
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Any(authorizeData => string.Equals(authorizeData.Policy, InternalServiceAuthorizationPolicy.Name, StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_product_engineering_policy;Username=nerv;Password=nerv",
            ["InternalService:BearerToken"] = "test-internal-service-token",
        };

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
            });
    }

    private static string NormalizeValidationPropertyName(string propertyName)
    {
        return propertyName.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }
}
