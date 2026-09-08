using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlChannelBindingAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class IndustrialTelemetryListQueryCompositionTests
{
    public static TheoryData<string> ListRoutes => new()
    {
        "/api/business/v1/iiot/tags",
        "/api/business/v1/iiot/alarm-rules",
        "/api/business/v1/iiot/alarms",
        "/api/business/v1/iiot/device-control-commands",
        "/api/business/v1/iiot/device-control-bindings",
    };

    [Theory]
    [MemberData(nameof(ListRoutes))]
    [Trait("Category", "PublicContract")]
    [Trait("Category", "Regression")]
    public async Task List_routes_trim_tenant_scope(string route)
    {
        await using var factory = new IndustrialTelemetryListQueryHttpTestFactory();
        await factory.SeedAsync();
        using var client = CreateAuthorizedClient(factory);

        using var response = await client.GetAsync(
            $"{route}?organizationId=%20org-001%20&environmentId=%20env-dev%20&skip=0&take=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSingleItem(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(ListRoutes))]
    [Trait("Category", "PublicContract")]
    [Trait("Category", "Regression")]
    public async Task List_routes_clamp_legacy_invalid_page(string route)
    {
        await using var factory = new IndustrialTelemetryListQueryHttpTestFactory();
        await factory.SeedAsync();
        using var client = CreateAuthorizedClient(factory);

        using var response = await client.GetAsync(
            $"{route}?organizationId=org-001&environmentId=env-dev&skip=-1&take=0");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        AssertSingleItem(body);
    }

    [Theory]
    [MemberData(nameof(ListRoutes))]
    [Trait("Category", "PublicContract")]
    [Trait("Category", "Regression")]
    public async Task List_routes_without_tenant_return_response_data_validation_error(string route)
    {
        await using var factory = new IndustrialTelemetryListQueryHttpTestFactory();
        using var client = CreateAuthorizedClient(factory);

        using var response = await client.GetAsync($"{route}?environmentId=env-dev");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(400, document.RootElement.GetProperty("code").GetInt32());
        Assert.Contains(
            "组织标识不能为空",
            document.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
        var errorData = document.RootElement.GetProperty("errorData");
        Assert.Equal(JsonValueKind.Array, errorData.ValueKind);
        Assert.NotEmpty(errorData.EnumerateArray());
        Assert.False(document.RootElement.TryGetProperty("data", out _));
    }

    // #2124 requires public default/boundary behavior; 501 rows distinguish both
    // an incorrectly low cap and a cap accidentally raised from 500 to 501.
    [Theory]
    [MemberData(nameof(ListRoutes))]
    [Trait("Category", "PublicContract")]
    [Trait("Category", "Regression")]
    public async Task List_routes_preserve_default_page_and_clamp_upper_bound(string route)
    {
        await using var factory = new IndustrialTelemetryListQueryHttpTestFactory();
        await factory.SeedAsync(501);
        using var client = CreateAuthorizedClient(factory);

        foreach (var (page, expectedCount) in new[] { ("", 100), ("&take=500", 500), ("&take=501", 500), ("&skip=500&take=500", 1) })
        {
            using var response = await client.GetAsync($"{route}?organizationId=org-001&environmentId=env-dev{page}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(501, data.GetProperty("total").GetInt32());
            Assert.Equal(expectedCount, data.GetProperty("items").GetArrayLength());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("%20%20%20")]
    [InlineData("%20RAISED%20")]
    [Trait("Category", "PublicContract")]
    public async Task Alarm_search_normalizes_blank_and_padded_status(string status)
    {
        await using var factory = new IndustrialTelemetryListQueryHttpTestFactory();
        await factory.SeedAsync();
        using var client = CreateAuthorizedClient(factory);
        using var response = await client.GetAsync($"/api/business/v1/iiot/alarms?organizationId=org-001&environmentId=env-dev&status={status}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSingleItem(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("device-control-commands", "organizationId")]
    [InlineData("device-control-commands", "environmentId")]
    [InlineData("device-control-bindings", "organizationId")]
    [InlineData("device-control-bindings", "environmentId")]
    [Trait("Category", "PublicContract")]
    [Trait("Category", "Regression")]
    public async Task Control_lists_preserve_tenant_length_limit(string route, string field)
    {
        await using var factory = new IndustrialTelemetryListQueryHttpTestFactory();
        using var client = CreateAuthorizedClient(factory);
        foreach (var length in new[] { 100, 101 })
        {
            var organization = field == "organizationId" ? new string('o', length) : "org-001";
            var environment = field == "environmentId" ? new string('e', length) : "env-dev";
            using var response = await client.GetAsync($"/api/business/v1/iiot/{route}?organizationId={organization}&environmentId={environment}");
            Assert.Equal(length == 100 ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            if (length == 101)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(400, document.RootElement.GetProperty("statusCode").GetInt32());
                Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(field, out var errors));
                Assert.NotEmpty(errors.EnumerateArray());
            }
        }
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        return client;
    }

    private static void AssertSingleItem(string body)
    {
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Single(data.GetProperty("items").EnumerateArray());
    }

    private sealed class IndustrialTelemetryListQueryHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"industrial-telemetry-list-query-{Guid.CreateVersion7():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        public async Task SeedAsync(int count = 1)
        {
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < count; index++)
            {
                dbContext.TelemetryTags.Add(TelemetryTag.Create(
                    "org-001", "env-dev", $"DEV-LIST-{index:D4}", "temperature", "number", "celsius", "sample-10s"));
                dbContext.AlarmRules.Add(AlarmRule.Configure(
                    "org-001", "env-dev", $"DEV-LIST-{index:D4}", "TEMP_RULE", "TEMP_HIGH", "warning",
                    "temperature", ">=", 90m, "celsius", true));
                dbContext.AlarmEvents.Add(AlarmEvent.Raise(
                    "org-001", "env-dev", $"DEV-LIST-{index:D4}", "TEMP_HIGH", "warning",
                    new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero), $"alarm-list-{index:D4}"));
                dbContext.DeviceControlCommands.Add(DeviceControlCommand.Record(
                    $"operation-list-{index:D4}",
                    "org-001",
                    "env-dev",
                    "connector-host-001",
                    "opcua-cell-01",
                    $"DEV-LIST-{index:D4}",
                    "write-tag",
                    "temperature",
                    "90",
                    null,
                    "user:operator-001",
                    "list query regression",
                    $"idem-list-{index:D4}",
                    $"corr-list-{index:D4}",
                    "completed",
                    null,
                    new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero)));
                dbContext.DeviceControlChannelBindings.Add(DeviceControlChannelBinding.Configure(
                    "org-001", "env-dev", $"DEV-LIST-{index:D4}", "connector-host-001", "opcua-cell-01"));
            }
            await dbContext.SaveChangesAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseInMemoryDatabase(databaseName)
                    .UseInternalServiceProvider(efServices)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                efServices.Dispose();
            }
        }
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
