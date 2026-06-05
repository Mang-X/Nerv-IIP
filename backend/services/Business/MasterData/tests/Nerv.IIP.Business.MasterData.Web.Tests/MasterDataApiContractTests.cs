using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Web.Application.Auth;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataApiContractTests
{
    [Theory]
    [InlineData(typeof(CreateSkuEndpoint))]
    [InlineData(typeof(CreateUnitOfMeasureEndpoint))]
    [InlineData(typeof(CreateUomConversionEndpoint))]
    [InlineData(typeof(CreateBusinessPartnerEndpoint))]
    [InlineData(typeof(CreateDepartmentEndpoint))]
    [InlineData(typeof(CreateTeamEndpoint))]
    [InlineData(typeof(AssignPersonnelSkillEndpoint))]
    [InlineData(typeof(CreateSiteEndpoint))]
    [InlineData(typeof(CreateProductionLineEndpoint))]
    [InlineData(typeof(CreateShiftEndpoint))]
    [InlineData(typeof(CreateWorkCalendarEndpoint))]
    [InlineData(typeof(CreateWorkCenterEndpoint))]
    [InlineData(typeof(RegisterDeviceAssetEndpoint))]
    [InlineData(typeof(CreateReferenceDataCodeEndpoint))]
    public void MasterData_mutation_endpoints_route_through_mediator(Type endpointType)
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
    public void MasterData_endpoint_sources_do_not_commit_transactions_directly()
    {
        var source = File.ReadAllText(Path.Combine(MasterDataServiceRoot(), "src", "Nerv.IIP.Business.MasterData.Web", "Endpoints", "MasterData", "MasterDataEndpoints.cs"));

        Assert.DoesNotContain("ApplicationDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterData_list_endpoint_routes_through_mediator()
    {
        var parameterTypes = typeof(ListMasterDataResourcesEndpoint)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public void MasterData_endpoint_contracts_have_stable_openapi_operation_ids()
    {
        var contracts = MasterDataEndpointContracts.All;

        Assert.Equal(17, contracts.Count);
        Assert.Equal(contracts.Count, contracts.Select(x => x.EndpointType).Distinct().Count());
        Assert.Equal(contracts.Count, contracts.Select(x => x.OperationId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(contracts, contract =>
        {
            Assert.Matches("^[a-z][A-Za-z0-9]*$", contract.OperationId);
            Assert.Contains("BusinessMasterData", contract.OperationId, StringComparison.Ordinal);
            Assert.StartsWith("/api/business/v1/master-data/", contract.Route, StringComparison.Ordinal);
            Assert.Contains(contract.HttpMethod, new[] { "GET", "POST" });
            Assert.Contains(contract.PermissionCode, NervIipBusinessMasterDataPermissionSet);
        });
    }

    [Fact]
    public void MasterData_business_endpoints_require_internal_service_authorization_policy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var failures = MasterDataEndpointContracts.All
            .Where(contract => !HasInternalServicePolicy(endpoints, contract.Route))
            .Select(contract => $"{contract.EndpointType.Name} is missing {InternalServiceAuthorizationPolicy.Name}.")
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void MasterData_endpoint_contracts_are_the_route_registry()
    {
        var source = File.ReadAllText(Path.Combine(MasterDataServiceRoot(), "src", "Nerv.IIP.Business.MasterData.Web", "Endpoints", "MasterData", "MasterDataEndpoints.cs"));

        Assert.DoesNotContain("[Http", source, StringComparison.Ordinal);
        Assert.Contains("Get(contract.Route);", source, StringComparison.Ordinal);
        Assert.Contains("Post(contract.Route);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterData_endpoints_define_permissions_from_contracts()
    {
        var source = File.ReadAllText(Path.Combine(MasterDataServiceRoot(), "src", "Nerv.IIP.Business.MasterData.Web", "Endpoints", "MasterData", "MasterDataEndpoints.cs"));
        var failures = MasterDataEndpointContracts.All
            .Where(contract =>
                !source.Contains($"MasterDataEndpointContracts.Get<{contract.EndpointType.Name}>()", StringComparison.Ordinal) ||
                !source.Contains("ConfigureMasterDataContract(contract);", StringComparison.Ordinal))
            .Select(contract => $"{contract.EndpointType.Name} does not configure permission contract")
            .ToArray();

        Assert.Empty(failures);
        Assert.DoesNotContain("Description(builder => builder.WithName(contract.OperationId));", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_references_supports_reference_data_codes_for_process_master_data()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "material-form", "powder", "Powder"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ResolveMasterDataReferencesQueryHandler(dbContext);
        var response = await handler.Handle(
            new ResolveMasterDataReferencesQuery(
                "org-001",
                "env-dev",
                [
                    new MasterDataReferenceRequest("reference-data", "powder", "material-form"),
                    new MasterDataReferenceRequest("reference-data:material-form", "powder")
                ]),
            CancellationToken.None);

        Assert.All(response.References, reference =>
        {
            Assert.True(reference.Exists);
            Assert.True(reference.Active);
            Assert.Equal("Powder", reference.DisplayName);
        });
    }

    [Fact]
    public async Task List_resources_supports_reference_data_codes()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "material-form", "powder", "Powder"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ListMasterDataResourcesQueryHandler(dbContext);
        var response = await handler.Handle(
            new ListMasterDataResourcesQuery("org-001", "env-dev", "reference-data"),
            CancellationToken.None);

        var resource = Assert.Single(response.Resources);
        Assert.Equal("reference-data", resource.ResourceType);
        Assert.Equal("material-form:powder", resource.Code);
        Assert.Equal("Powder", resource.DisplayName);
        Assert.True(resource.Active);
    }

    [Fact]
    public async Task List_resources_returns_offset_page_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-001", "Sku 1", "pcs", "finished-good"));
        dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-002", "Sku 2", "pcs", "finished-good"));
        dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-003", "Sku 3", "pcs", "finished-good"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListMasterDataResourcesQueryHandler(dbContext).Handle(
            new ListMasterDataResourcesQuery("org-001", "env-dev", "sku", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        Assert.Equal("SKU-002", Assert.Single(response.Resources).Code);
    }

    [Fact]
    public async Task MasterData_create_commands_create_core_resources_without_direct_save()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var created = new[]
        {
            await new CreateSkuCommandHandler(new SkuRepository(dbContext)).Handle(
                new CreateSkuCommand("org-001", "env-dev", "SKU-001", "Finished Good", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, ["gmp"]),
                CancellationToken.None),
            await new CreateUnitOfMeasureCommandHandler(new UnitOfMeasureRepository(dbContext)).Handle(
                new CreateUnitOfMeasureCommand("org-001", "env-dev", "kg", "Kilogram", "mass", 3, "half-up"),
                CancellationToken.None),
            await new CreateUomConversionCommandHandler(new UomConversionRepository(dbContext)).Handle(
                new CreateUomConversionCommand("org-001", "env-dev", "kg", "g", 1000m, 0m, 3, "half-up", new DateOnly(2026, 1, 1)),
                CancellationToken.None),
            await new CreateBusinessPartnerCommandHandler(new BusinessPartnerRepository(dbContext)).Handle(
                new CreateBusinessPartnerCommand("org-001", "env-dev", "SUP-001", "supplier", "Supplier A"),
                CancellationToken.None),
            await new CreateDepartmentCommandHandler(new DepartmentRepository(dbContext)).Handle(
                new CreateDepartmentCommand("org-001", "env-dev", "D-001", "Production", null),
                CancellationToken.None),
            await new CreateTeamCommandHandler(new TeamRepository(dbContext)).Handle(
                new CreateTeamCommand("org-001", "env-dev", "T-001", "Team A", "D-001", "S-001"),
                CancellationToken.None),
            await new AssignPersonnelSkillCommandHandler(new PersonnelSkillRepository(dbContext)).Handle(
                new AssignPersonnelSkillCommand("org-001", "env-dev", "user-001", "weighing", "senior", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                CancellationToken.None),
            await new CreateSiteCommandHandler(new SiteRepository(dbContext)).Handle(
                new CreateSiteCommand("org-001", "env-dev", "SITE-001", "Main Plant", "Asia/Shanghai"),
                CancellationToken.None),
            await new CreateProductionLineCommandHandler(new ProductionLineRepository(dbContext)).Handle(
                new CreateProductionLineCommand("org-001", "env-dev", "LINE-001", "Line 1", "SITE-001"),
                CancellationToken.None),
            await new CreateShiftCommandHandler(new ShiftRepository(dbContext)).Handle(
                new CreateShiftCommand("org-001", "env-dev", "S-001", "Night Shift", new TimeOnly(20, 0), new TimeOnly(8, 0), 720),
                CancellationToken.None),
            await new CreateWorkCalendarCommandHandler(new WorkCalendarRepository(dbContext)).Handle(
                new CreateWorkCalendarCommand("org-001", "env-dev", "CAL-001", "Standard Calendar"),
                CancellationToken.None),
            await new CreateWorkCenterCommandHandler(new WorkCenterRepository(dbContext)).Handle(
                new CreateWorkCenterCommand("org-001", "env-dev", "WC-001", "Mixing", 960, "work-center", "SITE-001", "LINE-001", "CAL-001", "minute", true),
                CancellationToken.None),
            await new RegisterDeviceAssetCommandHandler(new DeviceAssetRepository(dbContext)).Handle(
                new RegisterDeviceAssetCommand("org-001", "env-dev", "EQ-001", "Mixer 500", "LINE-001", "WC-001", "mixer", "ACME", "SN-001", 10m, 500m, "kg", "critical", true, true, new Dictionary<string, string>()),
                CancellationToken.None),
            await new CreateReferenceDataCodeCommandHandler(new ReferenceDataCodeRepository(dbContext)).Handle(
                new CreateReferenceDataCodeCommand("org-001", "env-dev", "material-form", "powder", "Powder"),
                CancellationToken.None),
        };

        Assert.Equal(14, created.Length);
        Assert.Contains(created, x => x.ResourceType == "sku" && x.Code == "SKU-001");
        Assert.Contains(created, x => x.ResourceType == "uom-conversion" && x.Code == "kg->g");
        Assert.Contains(created, x => x.ResourceType == "reference-data-code" && x.Code == "powder");
        Assert.Equal(14, dbContext.ChangeTracker.Entries().Count(entry => entry.State == EntityState.Added));
    }

    [Fact]
    public async Task Create_sku_command_rejects_duplicate_business_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Skus.Add(Domain.AggregatesModel.SkuAggregate.Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "SKU-001",
            "Finished Good",
            "kg",
            "finished-good",
            "material",
            "lot",
            "none",
            "180d",
            "ambient",
            "ean13",
            true,
            ["gmp"]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", "SKU-001", "Duplicate", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, []),
            CancellationToken.None));
        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_sku_command_generates_unique_server_codes_for_parallel_requests()
    {
        await using var provider = CreateInMemoryProvider();
        var numbering = new MasterDataNumberingService();

        var tasks = Enumerable.Range(1, 20)
            .Select(async index =>
            {
                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), numbering);
                var result = await handler.Handle(
                    new CreateSkuCommand("org-001", "env-dev", null, $"Finished Good {index}", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], $"sku-create-{index}"),
                    CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                return result.Code;
            });

        var codes = await Task.WhenAll(tasks);

        Assert.Equal(20, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^SKU-[0-9]{8}-[0-9]{6}$", code));
    }

    [Fact]
    public async Task Create_sku_command_reuses_existing_result_for_same_idempotency_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var numbering = new MasterDataNumberingService();
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), numbering);
        var command = new CreateSkuCommand("org-001", "env-dev", null, "Finished Good", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], "sku-idempotent-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.Code, second.Code);
        Assert.Single(dbContext.Skus);
    }

    [Fact]
    public async Task Create_sku_command_replay_returns_persisted_display_name()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), CreateNumberingService(scope));
        var command = new CreateSkuCommand("org-001", "env-dev", null, "Original Name", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], "sku-idempotent-display-name");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var persisted = await dbContext.Skus.SingleAsync(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev" && x.Code == first.Code, CancellationToken.None);
        persisted.Rename("Persisted Name");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var replay = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.Code, replay.Code);
        Assert.Equal("Persisted Name", replay.DisplayName);
    }

    [Fact]
    public async Task Create_sku_command_rejects_same_idempotency_key_with_different_name()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var numbering = CreateNumberingService(scope);
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), numbering);

        await handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", null, "Original Name", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], "sku-idempotent-name"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", null, "Changed Name", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], "sku-idempotent-name"),
            CancellationToken.None));

        Assert.Contains("different sku create payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_sku_command_db_numbering_generates_unique_codes_for_parallel_requests_after_counter_exists()
    {
        await using var provider = CreateInMemoryProvider("master-data-api-contract-db-numbering-parallel");
        using (var seedScope = provider.CreateScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seedHandler = new CreateSkuCommandHandler(new SkuRepository(seedContext), CreateNumberingService(seedScope));
            await seedHandler.Handle(
                new CreateSkuCommand("org-001", "env-dev", null, "Seed SKU", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], "sku-db-seed"),
                CancellationToken.None);
            await seedContext.SaveChangesAsync(CancellationToken.None);
        }

        var tasks = Enumerable.Range(1, 8)
            .Select(async index =>
            {
                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), CreateNumberingService(scope));
                var result = await handler.Handle(
                    new CreateSkuCommand("org-001", "env-dev", null, $"Parallel SKU {index}", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], $"sku-db-parallel-{index}"),
                    CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                return result.Code;
            });

        var codes = await Task.WhenAll(tasks);

        Assert.Equal(8, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^SKU-[0-9]{8}-[0-9]{6}$", code));
    }

    [Fact]
    public async Task Create_sku_command_db_numbering_reserves_counter_before_unit_of_work_save()
    {
        const string databaseName = "master-data-api-contract-db-numbering-uow";
        await using var provider = CreateInMemoryProvider(databaseName);
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), CreateNumberingService(scope));

        await handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", null, "Deferred Numbering", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], "sku-deferred-numbering"),
            CancellationToken.None);

        using var observerScope = provider.CreateScope();
        var observerContext = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(observerContext.NumberingCounters);
        Assert.Empty(observerContext.NumberingIdempotencyKeys);
        Assert.Empty(observerContext.Skus);
    }

    [Fact]
    public async Task Create_sku_command_persists_numbering_counter_and_idempotency_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), CreateNumberingService(scope));

        var result = await handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", null, "Persisted Numbering", "kg", "finished-good", "material", "lot", "none", "180d", "ambient", "ean13", true, [], "sku-persisted-numbering"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Matches("^SKU-[0-9]{8}-[0-9]{6}$", result.Code);
        Assert.Single(dbContext.NumberingCounters);
        var idempotency = Assert.Single(dbContext.NumberingIdempotencyKeys);
        Assert.Equal(result.Code, idempotency.Number);
    }

    private static ServiceProvider CreateInMemoryProvider(string? databaseName = null)
    {
        databaseName ??= $"master-data-api-contract-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static MasterDataNumberingService CreateNumberingService(IServiceScope scope)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var serviceScopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
        return new MasterDataNumberingService(dbContext, serviceScopeFactory);
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
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_masterdata_policy;Username=nerv;Password=nerv",
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

    private static readonly string[] NervIipBusinessMasterDataPermissionSet =
    [
        BusinessPermissionCodes.MasterDataProductsRead,
        BusinessPermissionCodes.MasterDataProductsManage,
        BusinessPermissionCodes.MasterDataPartnersRead,
        BusinessPermissionCodes.MasterDataPartnersManage,
        BusinessPermissionCodes.MasterDataResourcesRead,
        BusinessPermissionCodes.MasterDataResourcesManage
    ];

    private static string MasterDataServiceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "services", "Business", "MasterData");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate backend/services/Business/MasterData from test output directory.");
    }
}
