using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed record FileStorageGarbageCollectionResult(
    int ExpiredUploadSessionsRemoved,
    int ExpiredDownloadGrantsRemoved,
    int LocalTusFilesRemoved,
    int FormalFilesSoftDeleted = 0,
    int FormalFilesPhysicallyDeleted = 0);

public sealed class PostgreSqlFileStorageGarbageCollector(
    ApplicationDbContext dbContext,
    ILocalTusFileStoreAccessor tusStoreAccessor,
    IConfiguration? configuration = null)
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

        dbContext.UploadSessions.RemoveRange(expiredUploadSessions);
        dbContext.DownloadGrants.RemoveRange(expiredDownloadGrants);
        var physicalDeleteGrace = FileStoragePurposePolicies.ResolvePhysicalDeleteGrace(configuration);
        var softDeleted = 0;
        var activeFiles = await dbContext.StoredFiles
            .Where(x => x.Status == FileStorageScanPolicy.Available)
            .ToArrayAsync(cancellationToken);
        foreach (var file in activeFiles)
        {
            var retentionSeconds = FileStoragePurposePolicies.ResolveRetentionSeconds(file.FilePurpose, configuration);
            if (retentionSeconds is null || retentionSeconds.Value < 0)
            {
                continue;
            }

            if (file.CompletedAtUtc.AddSeconds(retentionSeconds.Value) <= now)
            {
                file.MarkDeleted(now, "retention-expired", physicalDeleteGrace);
                softDeleted++;
            }
        }

        var physicalDeleteFiles = await dbContext.StoredFiles
            .Where(x => x.Status == "deleted"
                && x.PhysicalDeleteAfterUtc != null
                && x.PhysicalDeleteAfterUtc <= now)
            .ToArrayAsync(cancellationToken);
        var physicalDeleteFileIds = physicalDeleteFiles
            .Select(x => x.FileId)
            .ToArray();
        var physicalDeleteUploadSessions = physicalDeleteFileIds.Length == 0
            ? Array.Empty<UploadSessionRecord>()
            : await dbContext.UploadSessions
                .Where(x => physicalDeleteFileIds.Contains(x.FileId))
                .ToArrayAsync(cancellationToken);
        var physicalDeleteDownloadGrants = physicalDeleteFileIds.Length == 0
            ? Array.Empty<DownloadGrantRecord>()
            : await dbContext.DownloadGrants
                .Where(x => physicalDeleteFileIds.Contains(x.FileId))
                .ToArrayAsync(cancellationToken);
        dbContext.StoredFiles.RemoveRange(physicalDeleteFiles);
        dbContext.UploadSessions.RemoveRange(physicalDeleteUploadSessions);
        dbContext.DownloadGrants.RemoveRange(physicalDeleteDownloadGrants);
        await dbContext.SaveChangesAsync(cancellationToken);

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

            foreach (var session in physicalDeleteUploadSessions)
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
            localTusFilesRemoved += store.DeleteFilesExcept(
                retainedTusUploadSessionIds,
                now - ResolveOrphanGracePeriod(configuration));
        }

        return new FileStorageGarbageCollectionResult(
            expiredUploadSessions.Length,
            expiredDownloadGrants.Length,
            localTusFilesRemoved,
            softDeleted,
            physicalDeleteFiles.Length);
    }

    private static TimeSpan ResolveOrphanGracePeriod(IConfiguration? configuration)
    {
        var seconds = configuration?.GetValue<double?>("FileStorage:GarbageCollection:OrphanTusFileGraceSeconds");
        return seconds is >= 0
            ? TimeSpan.FromSeconds(seconds.Value)
            : TimeSpan.FromMinutes(5);
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
            if (result is
                {
                    ExpiredUploadSessionsRemoved: 0,
                    ExpiredDownloadGrantsRemoved: 0,
                    LocalTusFilesRemoved: 0,
                    FormalFilesSoftDeleted: 0,
                    FormalFilesPhysicallyDeleted: 0
                })
            {
                return;
            }

            logger.LogInformation(
                "FileStorage garbage collection removed {UploadSessions} expired upload sessions, {DownloadGrants} expired download grants, {LocalTusFiles} local tus files, soft-deleted {SoftDeletedFiles} formal files, and physically removed {PhysicalDeletedFiles} formal files.",
                result.ExpiredUploadSessionsRemoved,
                result.ExpiredDownloadGrantsRemoved,
                result.LocalTusFilesRemoved,
                result.FormalFilesSoftDeleted,
                result.FormalFilesPhysicallyDeleted);
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
