using Nerv.IIP.FileStorage.Infrastructure;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed class UploadCommitExecutionLostException : Exception
{
    public UploadCommitExecutionLostException()
        : base("上传提交执行所有权已丢失。")
    {
    }
}

public sealed class UploadCommitExecutionLeaseManager(
    UploadCommitExecutionLeaseStore leaseStore,
    TimeProvider timeProvider,
    ILogger<UploadCommitExecutionLeaseManager> logger)
{
    public static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(1);

    public Task<bool> TryClaimAsync(
        string uploadSessionId,
        string executionOwnerId,
        CancellationToken cancellationToken) =>
        leaseStore.TryClaimAsync(uploadSessionId, executionOwnerId, cancellationToken);

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
        return await leaseStore.StillOwnsAsync(uploadSessionId, executionOwnerId, cancellationToken);
    }

    private async Task RenewUntilCancelledAsync(
        string uploadSessionId,
        string executionOwnerId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(RenewalInterval, timeProvider, cancellationToken);
            if (!await leaseStore.TryRenewAsync(uploadSessionId, executionOwnerId, cancellationToken))
            {
                logger.LogWarning(
                    "FileStorage 上传提交执行租约续租失败；UploadSessionId={UploadSessionId}，ErrorCode={ErrorCode}。",
                    uploadSessionId,
                    "commit-execution-lease-lost");
                throw new UploadCommitExecutionLostException();
            }
        }
    }

}
