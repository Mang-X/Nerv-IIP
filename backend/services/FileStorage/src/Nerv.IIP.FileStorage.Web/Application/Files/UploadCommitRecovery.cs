using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed record UploadCommitRecoveryResult(int Examined, int Completed, int Deferred);

public sealed class UploadCommitRecoveryProcessor(
    ApplicationDbContext dbContext,
    IFileStorageService fileStorageService,
    TimeProvider timeProvider,
    ILogger<UploadCommitRecoveryProcessor> logger)
{
    public async Task<UploadCommitRecoveryResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var sessions = await dbContext.UploadSessions
            .AsNoTracking()
            .Where(x => x.State == UploadSessionState.Committing
                && (x.NextRecoveryAtUtc == null || x.NextRecoveryAtUtc <= now))
            .OrderBy(x => x.NextRecoveryAtUtc)
            .ThenBy(x => x.CommittingAtUtc)
            .Take(25)
            .Select(x => new
            {
                x.UploadSessionId,
                x.OrganizationId,
                x.EnvironmentId,
                x.FilePurpose,
                Checksum = x.CommitChecksum ?? x.Checksum,
                x.ExpectedSizeBytes
            })
            .ToArrayAsync(cancellationToken);

        var completed = 0;
        foreach (var session in sessions)
        {
            var result = await fileStorageService.CompleteUploadSessionAsync(
                session.UploadSessionId,
                new CompleteUploadSessionRequest(
                    session.OrganizationId,
                    session.EnvironmentId,
                    session.FilePurpose,
                    session.Checksum,
                    session.ExpectedSizeBytes),
                cancellationToken);
            if (result.StatusCode == StatusCodes.Status200OK)
            {
                completed++;
            }
        }

        if (sessions.Length > 0)
        {
            logger.LogInformation(
                "FileStorage 提交恢复本轮检查了 {ExaminedCount} 个意图；完成 {CompletedCount} 个；延后 {DeferredCount} 个。",
                sessions.Length,
                completed,
                sessions.Length - completed);
        }

        return new UploadCommitRecoveryResult(sessions.Length, completed, sessions.Length - completed);
    }
}

public sealed class UploadCommitRecoveryHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<UploadCommitRecoveryHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<UploadCommitRecoveryProcessor>()
                    .RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "FileStorage 提交恢复本轮在报告有界结果前失败。");
            }
        }
    }
}
