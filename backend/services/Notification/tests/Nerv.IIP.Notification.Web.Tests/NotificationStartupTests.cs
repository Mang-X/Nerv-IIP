using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.Notification.Web.Tests;

[CollectionDefinition("notification-startup", DisableParallelization = true)]
public sealed class NotificationStartupCollection;

[Collection("notification-startup")]
public sealed class NotificationStartupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("InMemory")]
    public void Production_rejects_missing_or_inmemory_persistence(string? provider)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Persistence:Provider", provider);
                builder.UseSetting(
                    "ConnectionStrings:NotificationDb",
                    "Host=localhost;Database=unused;Username=nerv;Password=notification-startup-secret");
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Notification persistence configuration is invalid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("notification-startup-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_include_correlation_response_header()
    {
        await using var factory = CreateInMemoryFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Organization-Id", "org-001");
        client.DefaultRequestHeaders.Add("X-Environment-Id", "env-001");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-notification-startup");

        var response = await client.GetAsync("/api/notifications/v1/messages");

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values));
        Assert.Equal("corr-notification-startup", Assert.Single(values));
    }

    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        await using var factory = CreateInMemoryFactory();
        using var client = factory.CreateClient();

        var health = await client.GetStringAsync("/health");

        Assert.Equal("Healthy", health);
    }

    [Fact]
    public void Postgres_automigrate_is_rejected_outside_development()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Persistence:Provider", "PostgreSQL");
                builder.UseSetting("Persistence:AutoMigrate", "true");
                builder.UseSetting(
                    "ConnectionStrings:NotificationDb",
                    "Host=localhost;Database=nerv_iip_notification_guard;Username=nerv;Password=nerv");
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(
            "Persistence:AutoMigrate=true is only allowed for Notification in Development.",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateInMemoryFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                    });
                });
            });
    }

}
