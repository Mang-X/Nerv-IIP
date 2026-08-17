using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.FileStorage.Web.Tests;

[CollectionDefinition("file-storage-startup", DisableParallelization = true)]
public sealed class FileStorageStartupCollection;

[Collection("file-storage-startup")]
public sealed class FileStorageStartupGovernanceTests
{
    private const string PostgreSqlConnectionString =
        "Host=localhost;Database=nerv_iip_filestorage_startup;Username=nerv;Password=startup-test-secret";

    [Fact]
    public void Development_requires_an_explicit_persistence_provider()
    {
        using var factory = CreateFactory("Development", provider: string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("provider=<missing>", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("startup-test-secret", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Inmemory_is_rejected_with_apphost_postgresql_remedy(string environment)
    {
        using var factory = CreateFactory(environment, provider: "InMemory");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("FileStorage does not support Persistence:Provider=InMemory.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Aspire AppHost", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("startup-test-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_postgresql_with_connection_starts()
    {
        using var factory = CreateFactory(
            "Development",
            provider: "PostgreSQL",
            connectionString: PostgreSqlConnectionString);

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void Development_postgresql_provider_ignores_surrounding_whitespace()
    {
        using var factory = CreateFactory(
            "Development",
            provider: " PostgreSQL ",
            connectionString: PostgreSqlConnectionString);

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void Inmemory_with_automigrate_is_rejected_before_shared_automigrate_validation()
    {
        using var factory = CreateFactory(
            "Development",
            provider: "InMemory",
            autoMigrate: true);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("FileStorage does not support Persistence:Provider=InMemory.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Aspire AppHost", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_postgresql_without_connection_recommends_the_missing_connection()
    {
        using var factory = CreateFactory(
            "Development",
            provider: "PostgreSQL");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(
            "PostgreSQL requires ConnectionStrings:FileStorageDb.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_postgresql_with_connection_and_automigrate_disabled_starts()
    {
        using var factory = CreateFactory(
            "Production",
            provider: "PostgreSQL",
            connectionString: PostgreSqlConnectionString,
            autoMigrate: false);

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("PostgreSQL", false)]
    public void Production_rejects_nonpersistent_or_incomplete_configuration(
        string? provider,
        bool includeConnectionString)
    {
        using var factory = CreateFactory(
            "Production",
            provider,
            includeConnectionString ? PostgreSqlConnectionString : null);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("FileStorage persistence configuration is invalid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("startup-test-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_rejects_web_host_automigrate()
    {
        using var factory = CreateFactory(
            "Production",
            provider: "PostgreSQL",
            connectionString: PostgreSqlConnectionString,
            autoMigrate: true);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(
            "Persistence:AutoMigrate=true is only allowed for FileStorage in Development.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("startup-test-secret", exception.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        string? provider,
        string? connectionString = null,
        bool autoMigrate = false)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("Persistence:Provider", provider);
                builder.UseSetting("Persistence:AutoMigrate", autoMigrate.ToString());
                builder.UseSetting("ConnectionStrings:FileStorageDb", connectionString ?? string.Empty);
                builder.UseSetting("InternalService:BearerToken", "startup-test-internal-token-32bytes");
            });
    }
}
