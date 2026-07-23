using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.AppHub.Web.Tests;

[CollectionDefinition("readiness", DisableParallelization = true)]
public sealed class ReadinessCollection;

[Collection("readiness")]
public sealed class AppHubServiceReadinessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(null)]
    [InlineData("InMemory")]
    public void Production_rejects_missing_or_inmemory_persistence(string? provider)
    {
        using var guardedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Persistence:Provider", provider);
            builder.UseSetting(
                "ConnectionStrings:AppHubDb",
                "Host=localhost;Database=unused;Username=nerv;Password=apphub-readiness-secret");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => guardedFactory.CreateClient());

        Assert.Contains("AppHub persistence configuration is invalid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("apphub-readiness-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Postgres_automigrate_is_rejected_outside_development()
    {
        var environment = PreserveEnvironment(
            "Persistence__Provider",
            "Persistence__AutoMigrate",
            "ConnectionStrings__AppHubDb");

        try
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
            Environment.SetEnvironmentVariable("Persistence__AutoMigrate", " true ");
            Environment.SetEnvironmentVariable("ConnectionStrings__AppHubDb", "Host=localhost;Database=nerv_iip_apphub_guard;Username=nerv;Password=nerv");

            using var guardedFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

            var exception = Assert.Throws<InvalidOperationException>(() => guardedFactory.CreateClient());
            Assert.Contains("Persistence:AutoMigrate=true", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(environment);
        }
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
