using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.FileStorage.Web.Application.Archives;

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

    [Fact]
    public void Development_explicit_inmemory_starts()
    {
        using var factory = CreateFactory("Development", provider: "InMemory");

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void Development_without_versioned_storage_configuration_uses_unavailable_store()
    {
        using var factory = CreateFactory("Development", provider: "InMemory");

        var store = factory.Services.GetRequiredService<IVersionedObjectStore>();

        Assert.IsType<UnavailableVersionedObjectStore>(store);
    }

    [Theory]
    [InlineData("Storage:MinIO:Endpoint")]
    [InlineData("Storage:MinIO:AccessKey")]
    [InlineData("Storage:MinIO:SecretKey")]
    [InlineData("Storage:MinIO:ComplianceArchiveBucket")]
    public void Minio_configuration_requires_every_setting(string missingSetting)
    {
        var storageSettings = CompleteMinioSettings();
        storageSettings[missingSetting] = string.Empty;
        using var factory = CreateFactory(
            "Development",
            provider: "InMemory",
            storageSettings: storageSettings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("versioned object storage configuration is invalid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("startup-minio-access-key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("startup-minio-secret-key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Minio_configuration_requires_explicit_provider()
    {
        var storageSettings = CompleteMinioSettings();
        storageSettings["Storage:Provider"] = string.Empty;
        using var factory = CreateFactory(
            "Development",
            provider: "InMemory",
            storageSettings: storageSettings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("provider=<missing>", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://localhost:21")]
    [InlineData("http://user:password@localhost:9000")]
    [InlineData("http://localhost:9000/archive")]
    [InlineData("http://localhost:9000?region=local")]
    [InlineData("localhost:9000")]
    public void Minio_endpoint_must_be_an_http_origin(string endpoint)
    {
        var storageSettings = CompleteMinioSettings();
        storageSettings["Storage:MinIO:Endpoint"] = endpoint;
        using var factory = CreateFactory(
            "Development",
            provider: "InMemory",
            storageSettings: storageSettings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("endpointValid=False", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(endpoint, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ABCD")]
    [InlineData("ab")]
    [InlineData("192.168.1.1")]
    [InlineData("bad..bucket")]
    [InlineData("bad.-bucket")]
    [InlineData("bad-.bucket")]
    [InlineData("-bad-bucket")]
    public void Minio_bucket_must_use_a_valid_s3_name(string bucket)
    {
        var storageSettings = CompleteMinioSettings();
        storageSettings["Storage:MinIO:ComplianceArchiveBucket"] = bucket;
        using var factory = CreateFactory(
            "Development",
            provider: "InMemory",
            storageSettings: storageSettings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("complianceArchiveBucketValid=False", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(bucket, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_versioned_storage_provider_fails_fast()
    {
        using var factory = CreateFactory(
            "Development",
            provider: "InMemory",
            storageSettings: new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "Local"
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("provider=Local", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_minio_configuration_registers_minio_store()
    {
        using var factory = CreateFactory(
            "Development",
            provider: "InMemory",
            storageSettings: CompleteMinioSettings());

        var store = factory.Services.GetRequiredService<IVersionedObjectStore>();

        Assert.IsType<MinioVersionedObjectStore>(store);
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
    public void Development_inmemory_rejects_automigrate_with_specific_remedy()
    {
        using var factory = CreateFactory(
            "Development",
            provider: "InMemory",
            autoMigrate: true);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(
            "Persistence:AutoMigrate must be false when Persistence:Provider=InMemory.",
            exception.Message,
            StringComparison.Ordinal);
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
    [InlineData("InMemory", true)]
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
        bool autoMigrate = false,
        IReadOnlyDictionary<string, string?>? storageSettings = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("Persistence:Provider", provider);
                builder.UseSetting("Persistence:AutoMigrate", autoMigrate.ToString());
                builder.UseSetting("ConnectionStrings:FileStorageDb", connectionString ?? string.Empty);
                builder.UseSetting("InternalService:BearerToken", "startup-test-internal-token-32bytes");
                if (storageSettings is not null)
                {
                    foreach (var (key, value) in storageSettings)
                    {
                        builder.UseSetting(key, value);
                    }
                }
            });
    }

    private static Dictionary<string, string?> CompleteMinioSettings()
    {
        return new Dictionary<string, string?>
        {
            ["Storage:Provider"] = " MinIO ",
            ["Storage:MinIO:Endpoint"] = "http://localhost:9000",
            ["Storage:MinIO:AccessKey"] = "startup-minio-access-key",
            ["Storage:MinIO:SecretKey"] = "startup-minio-secret-key",
            ["Storage:MinIO:ComplianceArchiveBucket"] = "nerv-iip-compliance-archive"
        };
    }
}
