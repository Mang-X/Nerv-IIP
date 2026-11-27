using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

internal sealed class TusVolumeHealthCheck(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILocalTusFileStoreAccessor stores) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rootPath = configuration["FileStorage:Tus:RootPath"];
        if (!Directory.Exists(rootPath) || !stores.TryGet(out var store))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("The tus volume is unavailable."));
        }

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(rootPath!).GetEnumerator();
            _ = entries.MoveNext();
            store.VerifyVolume(required: !environment.IsDevelopment());
            var minimumFreeBytes = configuration.GetValue("FileStorage:Tus:MinimumFreeBytes", 256L * 1024 * 1024);
            var drivePath = OperatingSystem.IsWindows() ? Path.GetPathRoot(rootPath!)! : rootPath!;
            var freeBytes = new DriveInfo(drivePath).AvailableFreeSpace;
            return Task.FromResult(freeBytes < minimumFreeBytes
                ? HealthCheckResult.Degraded("The tus volume is below its free space threshold.")
                : HealthCheckResult.Healthy());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("The tus volume could not be verified.", error));
        }
    }
}
