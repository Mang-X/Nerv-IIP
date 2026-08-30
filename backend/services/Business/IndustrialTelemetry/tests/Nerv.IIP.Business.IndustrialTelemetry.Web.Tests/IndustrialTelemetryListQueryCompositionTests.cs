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

        public async Task SeedAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.TelemetryTags.Add(TelemetryTag.Create(
                "org-001", "env-dev", "DEV-LIST-01", "temperature", "number", "celsius", "sample-10s"));
            dbContext.AlarmRules.Add(AlarmRule.Configure(
                "org-001", "env-dev", "DEV-LIST-01", "TEMP_RULE", "TEMP_HIGH", "warning",
                "temperature", ">=", 90m, "celsius", true));
            dbContext.AlarmEvents.Add(AlarmEvent.Raise(
                "org-001", "env-dev", "DEV-LIST-01", "TEMP_HIGH", "warning",
                new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero), "alarm-list-001"));
            dbContext.DeviceControlCommands.Add(DeviceControlCommand.Record(
                "operation-list-001",
                "org-001",
                "env-dev",
                "connector-host-001",
                "opcua-cell-01",
                "DEV-LIST-01",
                "write-tag",
                "temperature",
                "90",
                null,
                "user:operator-001",
                "list query regression",
                "idem-list-001",
                "corr-list-001",
                "completed",
                null,
                new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero)));
            dbContext.DeviceControlChannelBindings.Add(DeviceControlChannelBinding.Configure(
                "org-001", "env-dev", "DEV-LIST-01", "connector-host-001", "opcua-cell-01"));
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
