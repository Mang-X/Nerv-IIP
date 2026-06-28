using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed record FileStorageGarbageCollectionResult(
    int ExpiredUploadSessionsRemoved,
    int ExpiredDownloadGrantsRemoved,
    int LocalTusFilesRemoved);

public sealed class PostgreSqlFileStorageGarbageCollector(
    ApplicationDbContext dbContext,
    ILocalTusFileStoreAccessor tusStoreAccessor)
{
    public async Task<FileStorageGarbageCollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredUploadSessions = await dbContext.UploadSessions
            .Where(x => !x.Completed && x.ExpiresAtUtc <= now)
            .ToArrayAsync(cancellationToken);
        var expiredDownloadGrants = await dbContext.DownloadGrants
            .Where(x => x.ExpiresAtUtc <= now)
            .ToArrayAsync(cancellationToken);

        var localTusFilesRemoved = 0;
        if (tusStoreAccessor.TryGet(out var store))
        {
            foreach (var session in expiredUploadSessions)
            {
                if (store.Delete(session.UploadSessionId))
                {
                    localTusFilesRemoved++;
                }
            }

            var retainedTusUploadSessionIds = await dbContext.UploadSessions
                .Where(x => x.Completed || x.ExpiresAtUtc > now)
                .Select(x => x.UploadSessionId)
                .ToArrayAsync(cancellationToken);
            localTusFilesRemoved += store.DeleteFilesExcept(retainedTusUploadSessionIds);
        }

        dbContext.UploadSessions.RemoveRange(expiredUploadSessions);
        dbContext.DownloadGrants.RemoveRange(expiredDownloadGrants);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new FileStorageGarbageCollectionResult(
            expiredUploadSessions.Length,
            expiredDownloadGrants.Length,
            localTusFilesRemoved);
    }
}

public sealed class FileStorageGarbageCollectionHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<FileStorageGarbageCollectionHostedService> logger,
    IConfiguration configuration) : BackgroundService
{
    private readonly TimeSpan interval = ResolveInterval(configuration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CollectOnceAsync(stoppingToken);
        }
    }

    private async Task CollectOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var collector = scope.ServiceProvider.GetRequiredService<PostgreSqlFileStorageGarbageCollector>();
            var result = await collector.CollectAsync(cancellationToken);
            if (result is { ExpiredUploadSessionsRemoved: 0, ExpiredDownloadGrantsRemoved: 0, LocalTusFilesRemoved: 0 })
            {
                return;
            }

            logger.LogInformation(
                "FileStorage garbage collection removed {UploadSessions} expired upload sessions, {DownloadGrants} expired download grants, and {LocalTusFiles} local tus files.",
                result.ExpiredUploadSessionsRemoved,
                result.ExpiredDownloadGrantsRemoved,
                result.LocalTusFilesRemoved);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FileStorage garbage collection failed.");
        }
    }

    private static TimeSpan ResolveInterval(IConfiguration configuration)
    {
        var seconds = configuration.GetValue<double?>("FileStorage:GarbageCollection:IntervalSeconds");
        return seconds is > 0
            ? TimeSpan.FromSeconds(seconds.Value)
            : TimeSpan.FromMinutes(5);
    }
}
