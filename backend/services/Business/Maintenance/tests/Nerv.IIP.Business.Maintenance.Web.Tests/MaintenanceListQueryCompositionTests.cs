using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceListQueryCompositionTests
{
    [Theory]
    [InlineData(null, "env-dev")]
    [InlineData("org-001", null)]
    [InlineData("   ", "env-dev")]
    public void Public_work_order_list_requires_both_tenant_values(string? organizationId, string? environmentId)
    {
        var result = new ListMaintenanceWorkOrdersQueryValidator().Validate(
            new ListMaintenanceWorkOrdersQuery(organizationId!, environmentId!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName is "OrganizationId" or "EnvironmentId");
    }

    [Theory]
    [InlineData(null, "env-dev")]
    [InlineData("org-001", null)]
    [InlineData("   ", "env-dev")]
    public void Public_plan_list_requires_both_tenant_values(string? organizationId, string? environmentId)
    {
        var result = new ListMaintenancePlansQueryValidator().Validate(
            new ListMaintenancePlansQuery(organizationId!, environmentId!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName is "OrganizationId" or "EnvironmentId");
    }

    [Theory]
    [InlineData(null, "env-dev")]
    [InlineData("org-001", null)]
    [InlineData("   ", "env-dev")]
    public void Public_downtime_reason_list_requires_both_tenant_values(string? organizationId, string? environmentId)
    {
        var result = new ListDowntimeReasonsQueryValidator().Validate(
            new ListDowntimeReasonsQuery(organizationId!, environmentId!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName is "OrganizationId" or "EnvironmentId");
    }

    [Fact]
    public void List_criteria_normalize_tenant_page_and_keyword_without_changing_request_values()
    {
        var tenant = TenantScope.From(" org-001 ", " env-dev ");
        var page = OffsetPage.From(-1, 201);
        var keyword = SearchTerm.From("  PuMp ");

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
        Assert.Equal(0, page.Skip);
        Assert.Equal(200, page.Take);
        Assert.Equal("pump", keyword.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_term_treats_blank_keyword_as_absent(string? value)
    {
        Assert.Null(SearchTerm.From(value).Value);
    }

    [Theory]
    [InlineData("/api/business/v1/maintenance/work-orders?environmentId=env-dev", "组织标识不能为空")]
    [InlineData("/api/business/v1/maintenance/work-orders?organizationId=org-001", "环境标识不能为空")]
    [InlineData("/api/business/v1/maintenance/plans?environmentId=env-dev", "组织标识不能为空")]
    [InlineData("/api/business/v1/maintenance/plans?organizationId=org-001", "环境标识不能为空")]
    [InlineData("/api/business/v1/maintenance/downtime-reasons?environmentId=env-dev", "组织标识不能为空")]
    [InlineData("/api/business/v1/maintenance/downtime-reasons?organizationId=org-001", "环境标识不能为空")]
    public async Task Public_list_endpoints_return_response_data_error_for_missing_tenant(string path, string expectedMessage)
    {
        await using var factory = CreateValidationFactory();
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var response = await client.GetAsync(path);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(expectedMessage, document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(400, document.RootElement.GetProperty("code").GetInt32());
        var errorData = document.RootElement.GetProperty("errorData");
        Assert.Equal(JsonValueKind.Array, errorData.ValueKind);
        Assert.NotEmpty(errorData.EnumerateArray());
        Assert.False(document.RootElement.TryGetProperty("data", out _), body);
    }

    [Fact]
    public async Task Public_list_endpoints_apply_composed_defaults_bounds_and_keywords()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        db.MaintenanceWorkOrders.AddRange(
            MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEVICE-PUMP-01", "high", "reporter"),
            MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEVICE-PUMP-02", "high", "reporter"),
            MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEVICE-OTHER", "high", "reporter"));
        db.MaintenancePlans.AddRange(
            MaintenancePlan.Create("org-001", "env-dev", "DEVICE-PUMP", "PM-PUMP-01", "P7D", new DateOnly(2026, 8, 1), "maintenance"),
            MaintenancePlan.Create("org-001", "env-dev", "DEVICE-PUMP", "PM-PUMP-02", "P7D", new DateOnly(2026, 8, 1), "maintenance"),
            MaintenancePlan.Create("org-001", "env-dev", "DEVICE-OTHER", "PM-OTHER", "P7D", new DateOnly(2026, 8, 1), "maintenance"));
        db.DowntimeReasons.AddRange(
            DowntimeReason.Create("org-001", "env-dev", "PUMP-01", "Pump failure", "breakdown", "availability"),
            DowntimeReason.Create("org-001", "env-dev", "PUMP-02", "Pump sensor", "breakdown", "availability"),
            DowntimeReason.Create("org-001", "env-dev", "OTHER-01", "Other failure", "other", "other"));
        await db.SaveChangesAsync();

        var sender = new QueryHandlerListSender(db);
        await using var factory = CreateKestrelFactory(sender);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        (string DefaultPath, string FilteredPath, string SkippedPath, string LowerBoundPath, string UpperBoundPath, string ItemProperty, string[] MatchingValues, string NonMatchingValue)[] lists =
        [
            (
                "/api/business/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&keyword=%20%20",
                "/api/business/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=0&take=1&keyword=%20%20pump%20%20",
                "/api/business/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=1&take=1&keyword=%20%20pump%20%20",
                "/api/business/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=-1&take=0&keyword=%20%20pump%20%20",
                "/api/business/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=0&take=201&keyword=%20%20pump%20%20",
                "deviceAssetId",
                ["DEVICE-PUMP-01", "DEVICE-PUMP-02"],
                "DEVICE-OTHER"),
            (
                "/api/business/v1/maintenance/plans?organizationId=org-001&environmentId=env-dev",
                "/api/business/v1/maintenance/plans?organizationId=org-001&environmentId=env-dev&skip=0&take=1&deviceAssetId=DEVICE-PUMP",
                "/api/business/v1/maintenance/plans?organizationId=org-001&environmentId=env-dev&skip=1&take=1&deviceAssetId=DEVICE-PUMP",
                "/api/business/v1/maintenance/plans?organizationId=org-001&environmentId=env-dev&skip=-1&take=0&deviceAssetId=DEVICE-PUMP",
                "/api/business/v1/maintenance/plans?organizationId=org-001&environmentId=env-dev&skip=0&take=201&deviceAssetId=DEVICE-PUMP",
                "planCode",
                ["PM-PUMP-01", "PM-PUMP-02"],
                "PM-OTHER"),
            (
                "/api/business/v1/maintenance/downtime-reasons?organizationId=org-001&environmentId=env-dev&keyword=%20%20",
                "/api/business/v1/maintenance/downtime-reasons?organizationId=org-001&environmentId=env-dev&skip=0&take=1&keyword=%20%20pump%20%20",
                "/api/business/v1/maintenance/downtime-reasons?organizationId=org-001&environmentId=env-dev&skip=1&take=1&keyword=%20%20pump%20%20",
                "/api/business/v1/maintenance/downtime-reasons?organizationId=org-001&environmentId=env-dev&skip=-1&take=0&keyword=%20%20pump%20%20",
                "/api/business/v1/maintenance/downtime-reasons?organizationId=org-001&environmentId=env-dev&skip=0&take=201&keyword=%20%20pump%20%20",
                "reasonCode",
                ["PUMP-01", "PUMP-02"],
                "OTHER-01"),
        ];

        foreach (var list in lists)
        {
            var defaultResponse = await client.GetAsync(list.DefaultPath);
            Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
            var defaultValues = await AssertListPageAsync(defaultResponse, list.ItemProperty, 0, 100, 3, 3);
            Assert.Equal(
                list.MatchingValues.Append(list.NonMatchingValue).OrderBy(value => value),
                defaultValues.OrderBy(value => value));

            var filteredResponse = await client.GetAsync(list.FilteredPath);
            Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
            var filteredValues = await AssertListPageAsync(filteredResponse, list.ItemProperty, 0, 1, 2, 1);
            Assert.Contains(filteredValues.Single(), list.MatchingValues);
            Assert.DoesNotContain(list.NonMatchingValue, filteredValues);

            var skippedResponse = await client.GetAsync(list.SkippedPath);
            Assert.Equal(HttpStatusCode.OK, skippedResponse.StatusCode);
            var skippedValues = await AssertListPageAsync(skippedResponse, list.ItemProperty, 1, 1, 2, 1);
            Assert.Contains(skippedValues.Single(), list.MatchingValues);
            Assert.DoesNotContain(list.NonMatchingValue, skippedValues);
            Assert.NotEqual(filteredValues.Single(), skippedValues.Single());

            var lowerBoundResponse = await client.GetAsync(list.LowerBoundPath);
            Assert.Equal(HttpStatusCode.OK, lowerBoundResponse.StatusCode);
            var lowerBoundValues = await AssertListPageAsync(lowerBoundResponse, list.ItemProperty, 0, 1, 2, 1);
            Assert.Contains(lowerBoundValues.Single(), list.MatchingValues);
            Assert.DoesNotContain(list.NonMatchingValue, lowerBoundValues);

            var upperBoundResponse = await client.GetAsync(list.UpperBoundPath);
            Assert.Equal(HttpStatusCode.OK, upperBoundResponse.StatusCode);
            var upperBoundValues = await AssertListPageAsync(upperBoundResponse, list.ItemProperty, 0, 200, 2, 2);
            Assert.Equal(list.MatchingValues.OrderBy(value => value), upperBoundValues.OrderBy(value => value));
            Assert.DoesNotContain(list.NonMatchingValue, upperBoundValues);
        }
    }

    [Fact]
    public async Task Maintenance_lists_apply_normalized_scope_keyword_and_page_before_querying()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        db.MaintenanceWorkOrders.Add(MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "DEVICE-PUMP", "high", "reporter"));
        db.MaintenancePlans.Add(MaintenancePlan.Create(
            "org-001", "env-dev", "DEVICE-PUMP", "PM-PUMP", "P7D", new DateOnly(2026, 8, 1), "maintenance"));
        db.DowntimeReasons.Add(DowntimeReason.Create(
            "org-001", "env-dev", "PUMP-01", "Pump failure", "breakdown", "availability"));
        await db.SaveChangesAsync();

        var workOrders = await new ListMaintenanceWorkOrdersQueryHandler(db).Handle(
            new ListMaintenanceWorkOrdersQuery(" org-001 ", " env-dev ", Skip: -1, Take: 201, Keyword: "  pump  "),
            CancellationToken.None);
        var plans = await new ListMaintenancePlansQueryHandler(db).Handle(
            new ListMaintenancePlansQuery(" org-001 ", " env-dev ", Skip: -1, Take: 201),
            CancellationToken.None);
        var downtimeReasons = await new ListDowntimeReasonsQueryHandler(db).Handle(
            new ListDowntimeReasonsQuery(" org-001 ", " env-dev ", Skip: -1, Take: 201, Keyword: "  pump  "),
            CancellationToken.None);

        Assert.Equal((0, 200), (workOrders.Skip, workOrders.Take));
        Assert.Equal("DEVICE-PUMP", Assert.Single(workOrders.Items).DeviceAssetId);
        Assert.Equal((0, 200), (plans.Skip, plans.Take));
        Assert.Equal("PM-PUMP", Assert.Single(plans.Items).PlanCode);
        Assert.Equal((0, 200), (downtimeReasons.Skip, downtimeReasons.Take));
        Assert.Equal("PUMP-01", Assert.Single(downtimeReasons.Items).ReasonCode);
    }

    private static WebApplicationFactory<Program> CreateKestrelFactory(ISender sender)
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.local");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        factory.UseKestrel(0);
        return factory;
    }

    private static WebApplicationFactory<Program> CreateValidationFactory()
    {
        var databaseName = $"maintenance-list-validation-{Guid.CreateVersion7():N}";
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.local");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
                });
            });
    }

    private static async Task<string[]> AssertListPageAsync(
        HttpResponseMessage response,
        string itemProperty,
        int expectedSkip,
        int expectedTake,
        int expectedTotal,
        int expectedItemCount)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(expectedSkip, data.GetProperty("skip").GetInt32());
        Assert.Equal(expectedTake, data.GetProperty("take").GetInt32());
        Assert.Equal(expectedTotal, data.GetProperty("total").GetInt32());
        var values = data.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty(itemProperty).GetString()!)
            .ToArray();
        Assert.Equal(expectedItemCount, values.Length);
        return values;
    }

    private sealed class QueryHandlerListSender(ApplicationDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                ListMaintenanceWorkOrdersQuery query => await new ListMaintenanceWorkOrdersQueryHandler(dbContext).Handle(query, cancellationToken),
                ListMaintenancePlansQuery query => await new ListMaintenancePlansQueryHandler(dbContext).Handle(query, cancellationToken),
                ListDowntimeReasonsQuery query => await new ListDowntimeReasonsQueryHandler(dbContext).Handle(query, cancellationToken),
                _ => throw new NotSupportedException($"Unexpected request type: {request.GetType().Name}"),
            };
            return (TResponse)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
