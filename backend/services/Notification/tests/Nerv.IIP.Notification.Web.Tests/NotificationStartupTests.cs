using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationStartupTests
{
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
    public void Postgres_automigrate_is_rejected_outside_development()
    {
        var environment = PreserveEnvironment(
            "Persistence__Provider",
            "Persistence__AutoMigrate",
            "ConnectionStrings__NotificationDb");

        try
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
            Environment.SetEnvironmentVariable("Persistence__AutoMigrate", "true");
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__NotificationDb",
                "Host=localhost;Database=nerv_iip_notification_guard;Username=nerv;Password=nerv");

            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

            var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains(
                "Persistence:AutoMigrate=true is only allowed for Notification in Development.",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(environment);
        }
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

    private static IReadOnlyDictionary<string, string?> PreserveEnvironment(params string[] names)
    {
        return names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
    }

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
