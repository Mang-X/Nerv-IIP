using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.ProductionVersions;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.StandardOperations;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringListQueryCompositionTests
{
    [Fact]
    [Trait("Contract", "Regression")]
    public void List_query_criteria_preserve_legacy_normalization_boundaries()
    {
        var tenant = TenantScope.From(" org-001 ", " env-dev ");

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
        Assert.Equal((0, 1), (OffsetPage.From(-1, 0).Skip, OffsetPage.From(-1, 0).Take));
        Assert.Equal(OffsetPage.MaxTake, OffsetPage.From(0, OffsetPage.MaxTake + 1).Take);
        Assert.Null(SearchTerm.From("   ").Value);
        Assert.Equal("pump", SearchTerm.From(" PuMp ").Value);
    }

    [Theory]
    [InlineData("", "env-dev", "组织标识不能为空。")]
    [InlineData("org-001", "   ", "环境标识不能为空。")]
    [Trait("Contract", "Regression")]
    public void Tenant_scope_rejects_missing_identifiers(
        string organizationId,
        string environmentId,
        string expectedMessage)
    {
        var exception = Assert.Throws<KnownException>(() => TenantScope.From(organizationId, environmentId));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    [Trait("Contract", "PublicContract")]
    [Trait("Contract", "Regression")]
    public async Task Standard_operation_list_http_contract_composes_tenant_search_and_page_rules()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedStandardOperationsAsync(dbContext);
        var handler = new ListStandardOperationsQueryHandler(dbContext);

        var defaults = await handler.Handle(
            new ListStandardOperationsQuery(" org-001 ", " env-dev ", null, "   "),
            CancellationToken.None);
        Assert.Equal(101, defaults.Total);
        Assert.Equal(OffsetPage.DefaultTake, defaults.Items.Count);

        var normalized = await handler.Handle(
            new ListStandardOperationsQuery(" org-001 ", " env-dev ", null, " pUmP ", Skip: -1, Take: 0),
            CancellationToken.None);
        Assert.Equal("OP-000", Assert.Single(normalized.Items).OperationCode);

        using var factory = ProductEngineeringWebTestFactory.Create("product-engineering-list-composition");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        var missingTenant = await client.GetAsync(
            "/api/business/v1/engineering/standard-operations?environmentId=env-dev");
        var missingTenantBody = JsonDocument.Parse(await missingTenant.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, missingTenant.StatusCode);
        Assert.False(missingTenantBody.GetProperty("success").GetBoolean());
        Assert.Equal(400, missingTenantBody.GetProperty("code").GetInt32());
        Assert.True(missingTenantBody.TryGetProperty("errorData", out _));
        Assert.False(missingTenantBody.TryGetProperty("data", out _));
    }

    [Fact]
    [Trait("Contract", "PublicContract")]
    public void Product_engineering_list_queries_share_tenant_validation_without_losing_specific_filters()
    {
        AssertTenantValidation(
            new ListEngineeringBomsQueryValidator(),
            new ListEngineeringBomsQuery("", "env-dev", "ITEM-001", "Published"),
            new ListEngineeringBomsQuery("org-001", "", "ITEM-001", "Published"));
        AssertTenantValidation(
            new ListManufacturingBomsQueryValidator(),
            new ListManufacturingBomsQuery("", "env-dev", "SKU-001", "Published"),
            new ListManufacturingBomsQuery("org-001", "", "SKU-001", "Published"));
        AssertTenantValidation(
            new ListRoutingsQueryValidator(),
            new ListRoutingsQuery("", "env-dev", "SKU-001", "Published"),
            new ListRoutingsQuery("org-001", "", "SKU-001", "Published"));
        AssertTenantValidation(
            new ListEngineeringDocumentsQueryValidator(),
            new ListEngineeringDocumentsQuery("", "env-dev", "ITEM-001", "drawing"),
            new ListEngineeringDocumentsQuery("org-001", "", "ITEM-001", "drawing"));
        AssertTenantValidation(
            new ListEngineeringItemsQueryValidator(),
            new ListEngineeringItemsQuery("", "env-dev", "ITEM-001", "Published"),
            new ListEngineeringItemsQuery("org-001", "", "ITEM-001", "Published"));
        AssertTenantValidation(
            new ListEngineeringChangesQueryValidator(),
            new ListEngineeringChangesQuery("", "env-dev", "Published"),
            new ListEngineeringChangesQuery("org-001", "", "Published"));
        AssertTenantValidation(
            new ListProductionVersionsQueryValidator(),
            new ListProductionVersionsQuery("", "env-dev", "SKU-001", "active"),
            new ListProductionVersionsQuery("org-001", "", "SKU-001", "active"));
        AssertTenantValidation(
            new ListStandardOperationsQueryValidator(),
            new ListStandardOperationsQuery("", "env-dev", true, "pump"),
            new ListStandardOperationsQuery("org-001", "", true, "pump"));
    }

    [Fact]
    [Trait("Contract", "PublicContract")]
    public async Task Product_engineering_list_openapi_preserves_scope_and_page_contracts()
    {
        using var factory = ProductEngineeringWebTestFactory.Create("product-engineering-list-openapi");
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = document.RootElement.GetProperty("paths");
        foreach (var route in new[]
                 {
                     "/api/business/v1/engineering/engineering-boms",
                     "/api/business/v1/engineering/manufacturing-boms",
                     "/api/business/v1/engineering/routings",
                     "/api/business/v1/engineering/documents",
                     "/api/business/v1/engineering/items",
                     "/api/business/v1/engineering/engineering-changes",
                     "/api/business/v1/engineering/production-versions",
                     "/api/business/v1/engineering/standard-operations",
                 })
        {
            var parameters = paths.GetProperty(route).GetProperty("get").GetProperty("parameters");
            Assert.Contains(parameters.EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "organizationId");
            Assert.Contains(parameters.EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "environmentId");
            Assert.Equal(0, FindParameter(parameters, "skip").GetProperty("schema").GetProperty("default").GetInt32());
            Assert.Equal(
                OffsetPage.DefaultTake,
                FindParameter(parameters, "take").GetProperty("schema").GetProperty("default").GetInt32());
        }
    }

    private static async Task SeedStandardOperationsAsync(ApplicationDbContext dbContext)
    {
        dbContext.StandardOperations.AddRange(Enumerable.Range(0, 101).Select(index =>
            StandardOperation.Create(
                "org-001",
                "env-dev",
                $"OP-{index:000}",
                $"Pump {index:000}",
                "WC-PUMP",
                1,
                1,
                "STD",
                requiresReporting: true,
                requiresQualityInspection: false,
                isOutsourced: false,
                description: null)));
        dbContext.StandardOperations.Add(StandardOperation.Create(
            "org-other",
            "env-dev",
            "OP-FOREIGN",
            "Pump foreign",
            "WC-PUMP",
            1,
            1,
            "STD",
            requiresReporting: true,
            requiresQualityInspection: false,
            isOutsourced: false,
            description: null));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"product-engineering-list-composition-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static void AssertTenantValidation<T>(
        AbstractValidator<T> validator,
        T missingOrganization,
        T missingEnvironment)
    {
        var organizationResult = validator.Validate(missingOrganization);
        var environmentResult = validator.Validate(missingEnvironment);

        Assert.Contains(organizationResult.Errors, error => error.ErrorMessage == "组织标识不能为空。");
        Assert.Contains(environmentResult.Errors, error => error.ErrorMessage == "环境标识不能为空。");
    }

    private static JsonElement FindParameter(JsonElement parameters, string name) =>
        parameters.EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == name);
}
