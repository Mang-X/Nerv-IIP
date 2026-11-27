using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Domain;
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

/// <remarks>
/// The collector reads exactly the columns <see cref="PostgreSqlFileStorageService"/> writes
/// (<c>ExpiresAtUtc</c> on upload sessions and download grants, <c>CompletedAtUtc</c> and
/// <c>PhysicalDeleteAfterUtc</c> on stored files), so it must read them through the <b>same</b>
/// <see cref="TimeProvider"/> that wrote them. Two clocks over one set of columns is not a rounding
/// difference: a test that advances a fake clock past a TTL would leave the collector blind to that
/// advance, and a fake clock anchored in the past would make every freshly created session already expired
/// from the collector's point of view.
/// </remarks>
public sealed class PostgreSqlFileStorageGarbageCollector(
    ApplicationDbContext dbContext,
    ILocalTusFileStoreAccessor tusStoreAccessor,
    IConfiguration? configuration = null,
    TimeProvider? timeProvider = null,
    S3FinalFileStore? s3 = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<FileStorageGarbageCollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var expiredUploadSessions = await dbContext.UploadSessions
            .Where(x => !x.LegacyCompleted
                && x.State == UploadSessionState.Open
                && x.ExpiresAtUtc <= now)
            .ToArrayAsync(cancellationToken);
        var expiredDownloadGrants = await dbContext.DownloadGrants
            .Where(x => x.ExpiresAtUtc <= now)
            .ToArrayAsync(cancellationToken);

        dbContext.UploadSessions.RemoveRange(expiredUploadSessions);
        dbContext.DownloadGrants.RemoveRange(expiredDownloadGrants);
        var physicalDeleteGrace = FileStoragePurposePolicies.ResolvePhysicalDeleteGrace(configuration);
        var softDeleted = 0;
        var activeFiles = await dbContext.StoredFiles
            .Where(x => x.Status == FileStorageFileStatus.Available)
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
            .Where(x => x.Status == FileStorageFileStatus.Deleted
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
        var localTusFilesRemoved = 0;
        var hasLocalStore = tusStoreAccessor.TryGet(out var store);
        if (s3 is { IsEnabled: true })
        {
            foreach (var file in physicalDeleteFiles)
            {
                await s3.DeleteAsync(file.ObjectKey, cancellationToken);
            }
        }
        else if (hasLocalStore)
        {
            // 仅清理已经软删除的文件。失败时保留元数据，以便下一轮按 ObjectKey 重试。
            foreach (var file in physicalDeleteFiles)
            {
                if (store.DeleteFinal(file.ObjectKey))
                {
                    localTusFilesRemoved++;
                }
            }
        }

        dbContext.StoredFiles.RemoveRange(physicalDeleteFiles);
        dbContext.UploadSessions.RemoveRange(physicalDeleteUploadSessions);
        dbContext.DownloadGrants.RemoveRange(physicalDeleteDownloadGrants);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (store is not null)
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

            if (s3 is { IsEnabled: true })
            {
                localTusFilesRemoved += await CollectCommittedS3StagingAsync(store, s3, cancellationToken);
            }

            var retainedTusUploadSessionIds = await dbContext.UploadSessions
                .Where(x => x.LegacyCompleted
                    || x.State != UploadSessionState.Open
                    || x.ExpiresAtUtc > now)
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

    private async Task<int> CollectCommittedS3StagingAsync(
        LocalUploadByteStore store, S3FinalFileStore s3, CancellationToken cancellationToken)
    {
        var completed = await (
            from session in dbContext.UploadSessions
            join file in dbContext.StoredFiles on session.FileId equals file.FileId
            where session.State == UploadSessionState.Completed && file.Checksum != null
            select new { session.UploadSessionId, file.ObjectKey, file.SizeBytes, file.Checksum }
        ).ToArrayAsync(cancellationToken);

        var removed = 0;
        foreach (var file in completed)
        {
            if (!store.Exists(file.UploadSessionId) || !s3.AcceptsKey(file.ObjectKey))
            {
                continue;
            }

            // 崩溃可能发生在 Tx2 与清理之间。先验证远端仍有完整副本，再删除暂存。
            var evidence = await s3.ReadEvidenceAsync(file.ObjectKey, cancellationToken);
            if (evidence?.SizeBytes == file.SizeBytes && evidence.CanonicalChecksum == file.Checksum
                && store.Delete(file.UploadSessionId))
            {
                removed++;
            }
        }
        return removed;
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
    IConfiguration configuration,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly TimeSpan interval = ResolveInterval(configuration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, timeProvider);
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
