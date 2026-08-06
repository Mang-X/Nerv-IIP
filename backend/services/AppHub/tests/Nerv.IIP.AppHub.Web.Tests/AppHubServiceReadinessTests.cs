using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Testing;

namespace Nerv.IIP.AppHub.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
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
    public async Task Postgres_automigrate_is_rejected_outside_development()
    {
        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose, so the guard
        // configuration cannot outlive this test. It is still process-global while the scope is
        // open: a host built by a test that never takes a scope can read it.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("Persistence__AutoMigrate", " true ")
            .SetEnvironmentVariable("ConnectionStrings__AppHubDb", "Host=localhost;Database=nerv_iip_apphub_guard;Username=nerv;Password=nerv");

        using var guardedFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Assert.Throws<InvalidOperationException>(() => guardedFactory.CreateClient());
        Assert.Contains("Persistence:AutoMigrate=true", exception.Message, StringComparison.Ordinal);
    }
}
