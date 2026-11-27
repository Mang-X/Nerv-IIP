using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData("wrong-volume", 0, HealthStatus.Unhealthy)]
    [InlineData(null, 0, HealthStatus.Unhealthy)]
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
        TusFinalReconciliation.FinalFileRecord[] records = [new("expected-file", "expected-key"), new("deleted-file", "deleted-key")];
        var result = await TusFinalReconciliation.CompareAsync(store, records.ToAsyncEnumerable(), cancellationToken);
        Assert.False(result.Truncated);

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

    [Theory]
    [InlineData("healthy", HttpStatusCode.OK, "Healthy")]
    [InlineData("missing-root", HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    [InlineData("missing-marker", HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    [InlineData("wrong-marker", HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task Volume_http_probe_tracks_volume_loss_while_liveness_stays_healthy(
        string scenario, HttpStatusCode status, string body)
    {
        var markerPath = Path.Combine(volume.FullName, ".filestorage-volume-v1");
        await File.WriteAllTextAsync(markerPath, "expected-volume");
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment(Environments.Production)
            .UseSetting("InternalService:BearerToken", "diagnostics-test-internal-token-32bytes")
            .UseSetting("FileStorage:Tus:RootPath", volume.FullName)
            .UseSetting("FileStorage:Tus:VolumeIdentity", "expected-volume")
            .UseSetting("FileStorage:Tus:MinimumFreeBytes", "0"));
        using var client = host.CreateClient();
        switch (scenario)
        {
            case "missing-root": volume.Delete(recursive: true); break;
            case "missing-marker": File.Delete(markerPath); break;
            case "wrong-marker": await File.WriteAllTextAsync(markerPath, "wrong-volume"); break;
        }

        using var health = await client.GetAsync("/health/tus-volume");
        Assert.Equal(status, health.StatusCode);
        Assert.Equal(body, await health.Content.ReadAsStringAsync());
        using var live = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reconciliation_rejects_a_concurrent_request_and_accepts_retry()
    {
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = factory.WithWebHostBuilder(builder => builder
            .UseSetting("FileStorage:Tus:RootPath", volume.FullName));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        var gate = host.Services.GetRequiredService<TusFinalReconciliationGate>();
        Assert.True(await gate.Permit.WaitAsync(0));
        const string path = "/internal/file-storage/v1/diagnostics/reconcile";
        try
        {
            using var busy = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.TooManyRequests, busy.StatusCode);
        }
        finally
        {
            gate.Permit.Release();
        }

        using var retry = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Reconciliation_observes_cancellation_during_record_read_and_before_a_new_scan()
    {
        var store = new LocalUploadByteStore(Configuration(0));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TusFinalReconciliation.CompareAsync(store, CancelDuringRead(), cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TusFinalReconciliation.CompareAsync(store, CancelDuringRead(), cancellation.Token));

        async IAsyncEnumerable<TusFinalReconciliation.FinalFileRecord> CancelDuringRead()
        {
            await Task.CompletedTask;
            cancellation.Cancel();
            yield return new("file", "key");
        }
    }

    [Theory]
    [InlineData(1, 2, 1, 1, null)]
    [InlineData(3, 1, 2, 2, 1)]
    public async Task Reconciliation_bounds_scans_and_report_without_false_orphans(
        int maxEntries, int maxReportItems, int files, int missing, int? orphans)
    {
        await File.WriteAllBytesAsync(Path.Combine(volume.FullName, "final-unmatched.bin"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(volume.FullName, "staging.bin"), [2]);
        await File.WriteAllTextAsync(Path.Combine(volume.FullName, ".filestorage-volume-v1"), "expected-volume");
        var store = new LocalUploadByteStore(Configuration(0));
        TusFinalReconciliation.FinalFileRecord[] records = [new("one", "key-one"), new("two", "key-two")];
        var result = await TusFinalReconciliation.CompareAsync(
            store, records.ToAsyncEnumerable(), CancellationToken.None, maxEntries, maxReportItems);

        Assert.True(result.Truncated);
        Assert.Equal(maxEntries, result.ScannedVolumeEntries);
        Assert.Equal(files, result.ScannedStoredFiles);
        Assert.Equal(missing, result.MissingFinalFileCount);
        Assert.Equal(orphans, result.OrphanFinalFileCount);
        Assert.Single(result.MissingFinalFiles);
        Assert.Empty(result.OrphanFinalFiles);
    }

    private IConfiguration Configuration(long minimumFreeBytes) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:Tus:RootPath"] = volume.FullName,
            ["FileStorage:Tus:VolumeIdentity"] = "expected-volume",
            ["FileStorage:Tus:MinimumFreeBytes"] = minimumFreeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        .Build();

    public void Dispose()
    {
        if (Directory.Exists(volume.FullName))
        {
            volume.Delete(recursive: true);
        }
    }
}
