using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class TusVolumeDiagnosticsTests : IDisposable
{
    private readonly DirectoryInfo volume = Directory.CreateTempSubdirectory("filestorage-diagnostics-");

    [Fact]
    public async Task Internal_diagnostics_reports_orphans_and_volume_health_is_available_over_http()
    {
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = factory.WithWebHostBuilder(builder => builder
            .UseSetting("FileStorage:Tus:RootPath", volume.FullName)
            .UseSetting("FileStorage:Tus:MinimumFreeBytes", long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        const string orphan = "final-unmatched.bin";
        await File.WriteAllBytesAsync(Path.Combine(volume.FullName, orphan), [1, 2, 3]);
        using var client = host.CreateClient();

        using var health = await client.GetAsync("/health/tus-volume");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Degraded", await health.Content.ReadAsStringAsync());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        using var response = await client.GetAsync("/internal/file-storage/v1/diagnostics/reconcile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var result = await response.Content.ReadFromJsonAsync<TusFinalReconciliationResult>();
        Assert.Empty(result!.MissingFinalFiles);
        Assert.Equal(orphan, Assert.Single(result.OrphanFinalFiles));
        Assert.True(File.Exists(Path.Combine(volume.FullName, orphan)));
    }

    [Theory]
    [InlineData("expected-volume", 0, HealthStatus.Healthy)]
    [InlineData("expected-volume", long.MaxValue, HealthStatus.Degraded)]
    [InlineData("wrong-volume", 0, HealthStatus.Degraded)]
    [InlineData(null, 0, HealthStatus.Degraded)]
    public async Task Volume_health_reports_capacity_and_identity(
        string? marker, long minimumFreeBytes, HealthStatus expected)
    {
        if (marker is not null)
        {
            File.WriteAllText(Path.Combine(volume.FullName, ".filestorage-volume-v1"), marker);
        }

        var configuration = Configuration(minimumFreeBytes);
        var environment = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        }).Environment;
        var probe = new TusVolumeHealthCheck(configuration, environment, new LocalTusFileStoreAccessor(configuration));

        var result = await probe.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Reconciliation_reports_missing_and_orphan_final_bytes_without_mutation(
        bool hasExpectedBytes, bool hasOrphan)
    {
        var cancellationToken = CancellationToken.None;
        var configuration = Configuration(0);
        File.WriteAllText(Path.Combine(volume.FullName, ".filestorage-volume-v1"), "expected-volume");
        var store = new LocalUploadByteStore(configuration);
        if (hasExpectedBytes)
        {
            await PromoteAsync("expected-key", "expected-session");
        }
        await PromoteAsync("deleted-key", "deleted-session");
        if (hasOrphan)
        {
            await PromoteAsync("orphan-key", "orphan-session");
        }
        await using var staging = new MemoryStream([1, 2, 3]);
        await store.AppendAsync("staging-only", 0, staging, cancellationToken);

        var before = volume.GetFiles().Select(file => file.Name).Order(StringComparer.Ordinal).ToArray();
        var result = TusFinalReconciliation.Compare(store,
            [new("expected-file", "expected-key"), new("deleted-file", "deleted-key")], cancellationToken);

        Assert.Equal(hasExpectedBytes ? [] : ["expected-file"], result.MissingFinalFiles);
        Assert.Equal(hasOrphan ? [LocalUploadByteStore.GetFinalFileName("orphan-key")] : [], result.OrphanFinalFiles);
        Assert.Equal(before, volume.GetFiles().Select(file => file.Name).Order(StringComparer.Ordinal));

        async Task PromoteAsync(string objectKey, string uploadSessionId)
        {
            await using var bytes = new MemoryStream([1, 2, 3]);
            await store.AppendAsync(uploadSessionId, 0, bytes, cancellationToken);
            await store.PromoteAsync(uploadSessionId, objectKey, 3, null, cancellationToken);
        }
    }

    private IConfiguration Configuration(long minimumFreeBytes) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:Tus:RootPath"] = volume.FullName,
            ["FileStorage:Tus:VolumeIdentity"] = "expected-volume",
            ["FileStorage:Tus:MinimumFreeBytes"] = minimumFreeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        .Build();

    public void Dispose() => volume.Delete(recursive: true);
}
