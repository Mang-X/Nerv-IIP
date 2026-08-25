using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ToolingAuditHttpTests
{
    [Fact]
    public async Task Three_write_endpoints_persist_forwarded_audit_context_and_whitelisted_summaries()
    {
        await using var factory = new ToolingAuditHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tooling-audit-test-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:tooling-admin-001");

        SetOperationHeaders(client, "corr-register", "cause-register", "tooling-register-http");
        var register = await client.PostAsJsonAsync("/api/business/v1/master-data/tooling-assets", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "TOOL-HTTP-001",
            name = "SENTINEL-TOOLING-NAME",
            toolingType = "mould",
            workCenterCodes = new[] { "WC-01" },
            skuCodes = new[] { "SKU-A" },
            maintenanceLifeCount = 100,
            idempotencyKey = "tooling-register-http",
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        SetOperationHeaders(client, "corr-status", "cause-status", "tooling-status-http");
        var status = await client.PostAsJsonAsync("/api/business/v1/master-data/tooling-assets/status", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "TOOL-HTTP-001",
            status = ToolingAssetStatus.Maintenance,
            reason = " planned service ",
        });
        Assert.True(status.StatusCode == HttpStatusCode.NoContent, await status.Content.ReadAsStringAsync());

        SetOperationHeaders(client, "corr-usage", "cause-usage", "tooling-usage-http");
        var usage = await client.PostAsJsonAsync("/api/business/v1/master-data/tooling-assets/usage", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "TOOL-HTTP-001",
            count = 7,
        });
        Assert.Equal(HttpStatusCode.NoContent, usage.StatusCode);

        using var observerScope = factory.Services.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await observer.ToolingAuditEntries.AsNoTracking().OrderBy(entry => entry.OccurredAtUtc).ToArrayAsync();
        Assert.Equal(3, audits.Length);
        Assert.All(audits, audit => Assert.Equal("user:tooling-admin-001", audit.ActorId));
        Assert.Equal(["corr-register", "corr-status", "corr-usage"], audits.Select(audit => audit.CorrelationId));
        Assert.Equal(["cause-register", "cause-status", "cause-usage"], audits.Select(audit => audit.CausationId));
        Assert.DoesNotContain(audits, audit => AuditText(audit).Contains("SENTINEL-TOOLING-NAME", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Internal_service_without_forwarded_actor_fails_closed_without_business_or_audit_rows()
    {
        await using var factory = new ToolingAuditHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tooling-audit-test-token");
        SetOperationHeaders(client, "corr-missing-actor", "cause-missing-actor", "tooling-register-missing-actor");

        var response = await client.PostAsJsonAsync("/api/business/v1/master-data/tooling-assets", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "TOOL-HTTP-MISSING-ACTOR",
            name = "Tool",
            toolingType = "mould",
            workCenterCodes = new[] { "WC-01" },
            skuCodes = new[] { "SKU-A" },
            maintenanceLifeCount = (long?)null,
            idempotencyKey = "tooling-register-missing-actor",
        });

        using var responseBody = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(responseBody);
        Assert.False(responseBody.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("X-Authenticated-Actor", responseBody.RootElement.GetProperty("message").GetString());
        using var observerScope = factory.Services.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await observer.ToolingAssets.AsNoTracking().ToArrayAsync());
        Assert.Empty(await observer.ToolingAuditEntries.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData("actor", "bearer:SENTINEL-TOKEN")]
    [InlineData("correlation", "password=SENTINEL")]
    [InlineData("causation", "connection-string-SENTINEL")]
    [InlineData("operation", "authorization-SENTINEL")]
    [InlineData("actor", "user:tooling-audit-test-token")]
    [InlineData("correlation", "tooling-audit-test-token")]
    [InlineData("causation", "tooling-audit-test-token")]
    [InlineData("operation", "tooling-audit-test-token")]
    [InlineData("actor", "user:eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhIn0.c2lnbmF0dXJl")]
    public async Task Sensitive_audit_context_fails_closed_without_business_or_audit_rows(
        string field,
        string invalidValue)
    {
        await using var factory = new ToolingAuditHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tooling-audit-test-token");
        client.DefaultRequestHeaders.Add(
            "X-Authenticated-Actor",
            field == "actor" ? invalidValue : "user:tooling-admin-001");
        SetOperationHeaders(
            client,
            field == "correlation" ? invalidValue : "corr-register",
            field == "causation" ? invalidValue : "cause-register",
            field == "operation" ? invalidValue : "tooling-register-http");

        var response = await client.PostAsJsonAsync("/api/business/v1/master-data/tooling-assets", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "TOOL-HTTP-SENSITIVE",
            name = "Tool",
            toolingType = "mould",
            workCenterCodes = new[] { "WC-01" },
            skuCodes = new[] { "SKU-A" },
            maintenanceLifeCount = (long?)null,
            idempotencyKey = field == "operation" ? invalidValue : "tooling-register-http",
        });

        using var responseBody = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(responseBody);
        Assert.False(responseBody.RootElement.GetProperty("success").GetBoolean());
        using var observerScope = factory.Services.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await observer.ToolingAssets.AsNoTracking().ToArrayAsync());
        Assert.Empty(await observer.ToolingAuditEntries.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData("password=SENTINEL-PASSWORD")]
    [InlineData("Bearer SENTINEL-TOKEN")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhIn0.c2lnbmF0dXJl")]
    [InlineData("tooling-audit-test-token")]
    public async Task Sensitive_status_reason_fails_before_business_change_and_audit_append(string reason)
    {
        await using var factory = new ToolingAuditHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tooling-audit-test-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:tooling-admin-001");
        SetOperationHeaders(client, "corr-register-reason", "cause-register-reason", "tooling-register-reason");
        var register = await client.PostAsJsonAsync("/api/business/v1/master-data/tooling-assets", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "TOOL-HTTP-REASON",
            name = "Tool",
            toolingType = "mould",
            workCenterCodes = new[] { "WC-01" },
            skuCodes = new[] { "SKU-A" },
            maintenanceLifeCount = (long?)null,
            idempotencyKey = "tooling-register-reason",
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        SetOperationHeaders(client, "corr-status-reason", "cause-status-reason", "tooling-status-reason");
        var response = await client.PostAsJsonAsync("/api/business/v1/master-data/tooling-assets/status", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "TOOL-HTTP-REASON",
            status = ToolingAssetStatus.Maintenance,
            reason,
        });

        using var responseBody = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(responseBody);
        Assert.False(responseBody.RootElement.GetProperty("success").GetBoolean());
        using var observerScope = factory.Services.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(ToolingAssetStatus.Available, (await observer.ToolingAssets.AsNoTracking().SingleAsync()).Status);
        var audit = await observer.ToolingAuditEntries.AsNoTracking().SingleAsync();
        Assert.Equal(ToolingAuditEntry.RegisterOperation, audit.OperationKind);
        Assert.DoesNotContain(reason, AuditText(audit), StringComparison.Ordinal);
    }

    private static void SetOperationHeaders(HttpClient client, string correlationId, string causationId, string operationId)
    {
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Remove("X-Causation-Id");
        client.DefaultRequestHeaders.Remove("X-Idempotency-Key");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        client.DefaultRequestHeaders.Add("X-Causation-Id", causationId);
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", operationId);
    }

    private static string AuditText(Domain.AggregatesModel.ToolingAssetAggregate.ToolingAuditEntry audit) => string.Join('|',
        audit.OperationKind,
        audit.ToolingAssetId,
        audit.ToolingCode,
        audit.ActorId,
        audit.CorrelationId,
        audit.CausationId,
        audit.OperationId,
        audit.RequestFingerprint,
        audit.Reason);

    private sealed class ToolingAuditHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"masterdata-tooling-audit-http-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "tooling-audit-test-token");
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
