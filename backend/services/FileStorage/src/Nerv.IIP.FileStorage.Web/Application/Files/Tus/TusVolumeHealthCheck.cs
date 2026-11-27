using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

internal sealed class TusVolumeHealthCheck(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILocalTusFileStoreAccessor stores,
    S3FinalFileStore? s3 = null) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var volume = CheckLocalVolume();
        if (s3 is { IsEnabled: true })
        {
            var bucket = await s3.CheckHealthAsync(cancellationToken);
            if (bucket.Status == HealthStatus.Unhealthy)
            {
                return bucket;
            }
        }

        return volume;
    }

    private HealthCheckResult CheckLocalVolume()
    {
        var rootPath = configuration["FileStorage:Tus:RootPath"];
        if (!Directory.Exists(rootPath) || !stores.TryGet(out var store))
        {
            return HealthCheckResult.Unhealthy("The tus volume is unavailable.");
        }

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(rootPath!).GetEnumerator();
            _ = entries.MoveNext();
            store.VerifyVolume(required: s3 is { IsEnabled: true } || !environment.IsDevelopment());
            var minimumFreeBytes = configuration.GetValue("FileStorage:Tus:MinimumFreeBytes", 256L * 1024 * 1024);
            var drivePath = OperatingSystem.IsWindows() ? Path.GetPathRoot(rootPath!)! : rootPath!;
            var freeBytes = new DriveInfo(drivePath).AvailableFreeSpace;
            return freeBytes < minimumFreeBytes
                ? HealthCheckResult.Degraded("The tus volume is below its free space threshold.")
                : HealthCheckResult.Healthy();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("The tus volume could not be verified.");
        }
    }
}
