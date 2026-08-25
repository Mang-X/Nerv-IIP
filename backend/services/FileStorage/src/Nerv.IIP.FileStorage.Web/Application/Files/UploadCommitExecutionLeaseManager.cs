using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed class UploadCommitExecutionLostException : Exception
{
    public UploadCommitExecutionLostException()
        : base("上传提交执行所有权已丢失。")
    {
    }
}

public sealed class UploadCommitExecutionLeaseManager(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    TimeProvider timeProvider,
    ILogger<UploadCommitExecutionLeaseManager> logger)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(1);

    public async Task<UploadCommitStorageResult> ExecuteWithRenewalAsync(
        string uploadSessionId,
        string executionOwnerId,
        UploadCommitIntent intent,
        IUploadCommitStorage storage,
        CancellationToken cancellationToken)
    {
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var storageTask = storage.CommitAsync(intent, executionCancellation.Token);
        var renewalTask = RenewUntilCancelledAsync(
            uploadSessionId,
            executionOwnerId,
            executionCancellation.Token);
        var completed = await Task.WhenAny(storageTask, renewalTask);
        if (completed == renewalTask)
        {
            executionCancellation.Cancel();
            await renewalTask;
            throw new UploadCommitExecutionLostException();
        }

        executionCancellation.Cancel();
        try
        {
            await renewalTask;
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
        }

        return await storageTask;
    }

    public async Task<bool> StillOwnsAsync(
        string uploadSessionId,
        string executionOwnerId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.UploadSessions
            .AsNoTracking()
            .AnyAsync(
                x => x.UploadSessionId == uploadSessionId
                    && x.State == UploadSessionState.Committing
                    && x.ExecutionOwnerId == executionOwnerId
                    && x.ExecutionLeaseUntilUtc > timeProvider.GetUtcNow(),
                cancellationToken);
    }

    private async Task RenewUntilCancelledAsync(
        string uploadSessionId,
        string executionOwnerId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(RenewalInterval, timeProvider, cancellationToken);
            if (!await TryRenewAsync(uploadSessionId, executionOwnerId, cancellationToken))
            {
                logger.LogWarning(
                    "FileStorage 上传提交执行租约续租失败；UploadSessionId={UploadSessionId}，ErrorCode={ErrorCode}。",
                    uploadSessionId,
                    "commit-execution-lease-lost");
                throw new UploadCommitExecutionLostException();
            }
        }
    }

    private async Task<bool> TryRenewAsync(
        string uploadSessionId,
        string executionOwnerId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.Add(LeaseDuration);
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.UploadSessions
                .Where(x => x.UploadSessionId == uploadSessionId
                    && x.State == UploadSessionState.Committing
                    && x.ExecutionOwnerId == executionOwnerId
                    && x.ExecutionLeaseUntilUtc > now)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.ExecutionLeaseUntilUtc, leaseUntil),
                    cancellationToken) == 1;
        }

        var session = await dbContext.UploadSessions.SingleOrDefaultAsync(
            x => x.UploadSessionId == uploadSessionId,
            cancellationToken);
        if (session is null
            || !string.Equals(session.State, UploadSessionState.Committing, StringComparison.Ordinal)
            || !string.Equals(session.ExecutionOwnerId, executionOwnerId, StringComparison.Ordinal)
            || session.ExecutionLeaseUntilUtc <= now)
        {
            return false;
        }

        session.RenewExecutionLease(leaseUntil);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
