using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Queries.QualityReasons;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class QualityReasonEndpointHttpTests
{
    [Fact]
    public async Task Scrap_reason_http_endpoint_binds_scope_search_and_paging_and_returns_wire_json()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-service-token");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.QualityReasons.AddRange(
                QualityReason.Create("org-001", "env-dev", "SCRAP-SURFACE-A", "Surface A", "Appearance", "major", "scrap", true),
                QualityReason.Create("org-001", "env-dev", "SCRAP-SURFACE-B", "Surface B", "Appearance", "major", "scrap", true),
                QualityReason.Create("org-001", "env-dev", "REWORK-SURFACE", "Surface Rework", "Appearance", "minor", "rework", true),
                QualityReason.Create("org-001", "env-test", "SCRAP-SURFACE-ENV", "Surface Other Environment", "Appearance", "major", "scrap", true),
                QualityReason.Create("org-002", "env-dev", "SCRAP-SURFACE-ORG", "Surface Other Organization", "Appearance", "major", "scrap", true));
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.GetAsync(
            "/api/business/v1/quality/scrap-reason-codes?organizationId=org-001&environmentId=env-dev&search=surface&skip=1&take=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<QualityReasonListResponse>>();
        Assert.NotNull(envelope?.Data);
        var data = envelope!.Data!;
        var item = Assert.Single(data.Items);
        Assert.Equal("SCRAP-SURFACE-B", item.ReasonCode);
        Assert.Equal(2, data.Total);
        Assert.Equal("scrap", item.DefaultDisposition);
        Assert.True(item.Enabled);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databaseName = $"quality-scrap-http-{Guid.NewGuid():N}";
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_scrap_http;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options => options
                        .UseInMemoryDatabase(databaseName)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                });
            });
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
