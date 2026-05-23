using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductEngineering;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringReleaseApiContractTests
{
    [Fact]
    public void Product_engineering_release_endpoint_contracts_cover_issue_127_surface()
    {
        var contracts = ProductEngineeringEndpointContracts.All;

        Assert.Equal(8, contracts.Count);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/documents" && x.PermissionCode == EngineeringPermissionCodes.DocumentsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/items" && x.PermissionCode == EngineeringPermissionCodes.ItemsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-boms/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/manufacturing-boms/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/routings/release" && x.PermissionCode == EngineeringPermissionCodes.RoutingsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-changes/release" && x.PermissionCode == EngineeringPermissionCodes.ChangesManage);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/routings" && x.PermissionCode == EngineeringPermissionCodes.RoutingsRead);
        Assert.All(contracts, contract =>
        {
            Assert.StartsWith("/api/business/v1/engineering", contract.Route, StringComparison.Ordinal);
            Assert.Contains(contract.PermissionCode, EngineeringPermissionCodes.All);
            Assert.Matches("^[a-z][A-Za-z0-9]*$", contract.OperationId);
            Assert.Contains("Business", contract.OperationId, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(typeof(RegisterEngineeringDocumentEndpoint))]
    [InlineData(typeof(CreateEngineeringItemRevisionEndpoint))]
    [InlineData(typeof(ReleaseEngineeringBomEndpoint))]
    [InlineData(typeof(ReleaseManufacturingBomEndpoint))]
    [InlineData(typeof(ReleaseRoutingEndpoint))]
    [InlineData(typeof(ReleaseEngineeringChangeEndpoint))]
    [InlineData(typeof(ListEngineeringBomsEndpoint))]
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
        Assert.Contains(documentResult.Errors, x => x.PropertyName == nameof(RegisterEngineeringDocumentCommand.OrganizationId));
        Assert.Contains(documentResult.Errors, x => x.PropertyName == nameof(RegisterEngineeringDocumentCommand.FileId));

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
        Assert.Contains(bomResult.Errors, x => x.PropertyName == nameof(ReleaseEngineeringBomCommand.EnvironmentId));
        Assert.Contains(bomResult.Errors, x => x.PropertyName == nameof(ReleaseEngineeringBomCommand.Lines));
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

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"product-engineering-release-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
