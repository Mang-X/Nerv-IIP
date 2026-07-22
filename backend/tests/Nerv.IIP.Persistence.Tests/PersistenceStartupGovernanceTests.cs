using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Persistence;

namespace Nerv.IIP.Persistence.Tests;

public sealed class PersistenceStartupGovernanceTests
{
    private const string Secret = "governance-contract-secret";
    private static readonly PersistenceStartupRequirements Requirements = new(
        "TestService",
        ["TestDb", "PostgreSQL"])
    {
        NonDevelopmentMigrationRemedy = "Run the TestService migrator outside Development."
    };

    [Fact]
    public void Development_accepts_explicit_inmemory()
    {
        var decision = Resolve("Development", provider: "InMemory");

        Assert.False(decision.UsePostgreSql);
        Assert.False(decision.AutoMigrate);
    }

    [Theory]
    [InlineData(" PostgreSQL ")]
    [InlineData("postgresql")]
    [InlineData("POSTGRESQL")]
    public void PostgreSql_provider_is_trimmed_and_case_insensitive(string provider)
    {
        var decision = Resolve(
            "Development",
            provider,
            connectionString: $"Host=localhost;Database=test;Username=nerv;Password={Secret}");

        Assert.True(decision.UsePostgreSql);
        Assert.False(decision.AutoMigrate);
    }

    [Fact]
    public void PostgreSql_accepts_a_configured_fallback_connection_alias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["ConnectionStrings:PostgreSQL"] = $"Host=localhost;Database=test;Username=nerv;Password={Secret}"
        });

        var decision = PersistenceStartupGovernance.Resolve(
            configuration,
            new TestHostEnvironment("Development"),
            Requirements);

        Assert.True(decision.UsePostgreSql);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SqlServer")]
    public void Development_rejects_missing_or_unknown_provider_without_leaking_credentials(string? provider)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            "Development",
            provider,
            connectionString: $"Host=localhost;Database=test;Username=nerv;Password={Secret}"));

        Assert.Contains("TestService persistence configuration is invalid", exception.Message, StringComparison.Ordinal);
        Assert.Contains("environment=Development", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSql_requires_a_service_connection_alias()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve("Development", "PostgreSQL"));

        Assert.Contains("ConnectionStrings:TestDb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("connectionConfigured=False", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("InMemory")]
    public void NonDevelopment_rejects_nonpersistent_provider(string? provider)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve("Production", provider));

        Assert.Contains("Non-Development environments require Persistence:Provider=PostgreSQL", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoMigrate_is_rejected_outside_development()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            "Staging",
            "PostgreSQL",
            $"Host=localhost;Database=test;Username=nerv;Password={Secret}",
            autoMigrate: true));

        Assert.Contains("Persistence:AutoMigrate=true is only allowed for TestService in Development", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Run the TestService migrator outside Development.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_inmemory_rejects_automigrate()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            "Development",
            "InMemory",
            autoMigrate: true));

        Assert.Contains("Persistence:AutoMigrate must be false when Persistence:Provider=InMemory", exception.Message, StringComparison.Ordinal);
    }

    private static PersistenceStartupDecision Resolve(
        string environment,
        string? provider,
        string? connectionString = null,
        bool autoMigrate = false)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = provider,
            ["Persistence:AutoMigrate"] = autoMigrate.ToString(),
            ["ConnectionStrings:TestDb"] = connectionString
        });
        return PersistenceStartupGovernance.Resolve(
            configuration,
            new TestHostEnvironment(environment),
            Requirements);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Nerv.IIP.Persistence.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
