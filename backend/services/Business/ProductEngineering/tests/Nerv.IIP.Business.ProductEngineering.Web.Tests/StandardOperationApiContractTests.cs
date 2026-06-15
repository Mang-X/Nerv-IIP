using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.StandardOperations;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.StandardOperations;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.StandardOperations;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class StandardOperationApiContractTests
{
    [Fact]
    public void Standard_operation_endpoint_contracts_cover_issue_397_surface()
    {
        var contracts = StandardOperationEndpointContracts.All;

        Assert.Equal(5, contracts.Count);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/standard-operations" && x.PermissionCode == EngineeringPermissionCodes.StandardOperationsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/standard-operations/{operationCode}" && x.PermissionCode == EngineeringPermissionCodes.StandardOperationsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/standard-operations" && x.PermissionCode == EngineeringPermissionCodes.StandardOperationsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "PUT" && x.Route == "/api/business/v1/engineering/standard-operations/{operationCode}" && x.PermissionCode == EngineeringPermissionCodes.StandardOperationsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/standard-operations/{operationCode}/archive" && x.PermissionCode == EngineeringPermissionCodes.StandardOperationsManage);
        Assert.All(contracts, contract =>
        {
            Assert.StartsWith("/api/business/v1/engineering/standard-operations", contract.Route, StringComparison.Ordinal);
            Assert.Contains(contract.PermissionCode, EngineeringPermissionCodes.All);
            Assert.Matches("^[a-z][A-Za-z0-9]*$", contract.OperationId);
            Assert.Contains("StandardOperation", contract.OperationId, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(typeof(ListStandardOperationsEndpoint))]
    [InlineData(typeof(GetStandardOperationEndpoint))]
    [InlineData(typeof(CreateStandardOperationEndpoint))]
    [InlineData(typeof(UpdateStandardOperationEndpoint))]
    [InlineData(typeof(ArchiveStandardOperationEndpoint))]
    public void Standard_operation_endpoints_route_through_mediator(Type endpointType)
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
    public async Task Create_standard_operation_rejects_duplicate_business_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.Add(StandardOperation.Create(
            "org-001",
            "env-dev",
            "OP-MIX",
            "混合",
            "WC-MIX-01",
            5,
            30,
            "INHOUSE",
            true,
            false,
            false,
            null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateStandardOperationCommandHandler(new StandardOperationRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCreateCommand("OP-MIX"),
            CancellationToken.None));

        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_standard_operation_allocates_code_when_omitted()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateStandardOperationCommandHandler(
            new StandardOperationRepository(dbContext),
            new ProductEngineeringCodingService());

        var created = await handler.Handle(
            NewCreateCommand(null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.StartsWith("OP-", created.OperationCode, StringComparison.Ordinal);
        Assert.True(await dbContext.StandardOperations.AnyAsync(x => x.OperationCode == created.OperationCode));
    }

    [Fact]
    public async Task Update_standard_operation_changes_defaults_used_by_routing_authoring()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var operation = StandardOperation.Create(
            "org-001",
            "env-dev",
            "OP-MIX",
            "混合",
            "WC-MIX-01",
            5,
            30,
            "INHOUSE",
            true,
            false,
            false,
            null);
        dbContext.StandardOperations.Add(operation);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new UpdateStandardOperationCommandHandler(new StandardOperationRepository(dbContext));
        await handler.Handle(
            new UpdateStandardOperationCommand(
                "org-001",
                "env-dev",
                "OP-MIX",
                "精混",
                "WC-MIX-02",
                8,
                42,
                "INHOUSE-QC",
                true,
                true,
                false,
                "调整后的标准混合工序"),
            CancellationToken.None);

        Assert.Equal("精混", operation.OperationName);
        Assert.Equal("WC-MIX-02", operation.DefaultWorkCenterCode);
        Assert.Equal(50, operation.StandardMinutes);
        Assert.True(operation.RequiresQualityInspection);
    }

    [Fact]
    public async Task List_standard_operations_returns_enabled_filter_and_split_default_minutes()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.Add(StandardOperation.Create("org-001", "env-dev", "OP-MIX", "混合", "WC-MIX-01", 5, 30, "INHOUSE", true, false, false, null));
        var archived = StandardOperation.Create("org-001", "env-dev", "OP-PACK", "包装", "WC-PACK-01", 0, 12, "PACK", true, false, false, null);
        archived.Archive("not used");
        dbContext.StandardOperations.Add(archived);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListStandardOperationsQueryHandler(dbContext).Handle(
            new ListStandardOperationsQuery("org-001", "env-dev", Enabled: true, Search: " mix ", Skip: -10, Take: 10),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("OP-MIX", item.OperationCode);
        Assert.Equal("WC-MIX-01", item.DefaultWorkCenterCode);
        Assert.Equal(5, item.StandardSetupMinutes);
        Assert.Equal(30, item.StandardRunMinutes);
        Assert.Equal(35, item.StandardMinutes);
        Assert.True(item.Enabled);
    }

    [Fact]
    public async Task Get_standard_operation_returns_known_exception_when_missing()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.ThrowsAsync<KnownException>(() => new GetStandardOperationQueryHandler(dbContext).Handle(
            new GetStandardOperationQuery("org-001", "env-dev", "OP-MISSING"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Archive_standard_operation_returns_known_exception_when_already_archived()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var operation = StandardOperation.Create("org-001", "env-dev", "OP-PACK", "包装", "WC-PACK-01", 0, 12, "PACK", true, false, false, null);
        operation.Archive("not used");
        dbContext.StandardOperations.Add(operation);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ArchiveStandardOperationCommandHandler(new StandardOperationRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ArchiveStandardOperationCommand("org-001", "env-dev", "OP-PACK", "duplicate archive"),
            CancellationToken.None));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Archived standard operation", exception.Message, StringComparison.Ordinal);
    }

    private static CreateStandardOperationCommand NewCreateCommand(string? operationCode)
    {
        return new CreateStandardOperationCommand(
            "org-001",
            "env-dev",
            operationCode,
            "混合",
            "WC-MIX-01",
            5,
            30,
            "INHOUSE",
            true,
            false,
            false,
            null);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"product-engineering-standard-operations-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
