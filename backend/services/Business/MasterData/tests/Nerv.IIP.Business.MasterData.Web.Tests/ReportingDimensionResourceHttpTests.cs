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
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ReportingDimensionResourceHttpTests
{
    [Fact]
    public async Task Resource_directory_returns_site_timezone_and_cross_midnight_shift_window_over_http()
    {
        await using var factory = new ReportingDimensionResourceHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Sites.Add(Site.Create("org-001", "env-dev", "SITE-001", "上海工厂", "Asia/Shanghai"));
            dbContext.Shifts.Add(Shift.Create(
                "org-001",
                "env-dev",
                "SHIFT-NIGHT",
                "夜班",
                new TimeOnly(20, 0),
                new TimeOnly(4, 0),
                420,
                60));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "reporting-dimension-http-token");

        using var site = await GetSingleResourceAsync(client, "site");
        Assert.Equal("Asia/Shanghai", site.RootElement.GetProperty("timezone").GetString());

        using var shift = await GetSingleResourceAsync(client, "shift");
        Assert.Equal("20:00:00", shift.RootElement.GetProperty("startsAt").GetString());
        Assert.Equal("04:00:00", shift.RootElement.GetProperty("endsAt").GetString());
        Assert.True(shift.RootElement.GetProperty("crossesMidnight").GetBoolean());
        Assert.Equal(420, shift.RootElement.GetProperty("paidMinutes").GetInt32());
        Assert.Equal(60, shift.RootElement.GetProperty("breakMinutes").GetInt32());
    }

    private static async Task<JsonDocument> GetSingleResourceAsync(HttpClient client, string resourceType)
    {
        using var response = await client.GetAsync(
            "/api/business/v1/master-data/resources" +
            $"?organizationId=org-001&environmentId=env-dev&resourceType={resourceType}&skip=0&take=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resource = Assert.Single(document.RootElement
            .GetProperty("data")
            .GetProperty("resources")
            .EnumerateArray());
        return JsonDocument.Parse(resource.GetRawText());
    }

    private sealed class ReportingDimensionResourceHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"masterdata-reporting-dimension-http-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "reporting-dimension-http-token");
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
