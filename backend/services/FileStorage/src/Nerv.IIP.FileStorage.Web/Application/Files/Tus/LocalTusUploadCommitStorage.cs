namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public sealed class LocalTusUploadCommitStorage(ILocalTusFileStoreAccessor accessor) : IUploadCommitStorage
{
    public Task<UploadCommitStorageResult> CommitAsync(
        UploadCommitIntent intent,
        CancellationToken cancellationToken) =>
        accessor.TryGet(out var store)
            ? store.PromoteAsync(intent.UploadSessionId, intent.ObjectKey, intent.ExpectedSizeBytes,
                intent.ExpectedChecksum, cancellationToken)
            : Task.FromResult(UploadCommitStorageResult.ProvenNoFinalActionStarted());
}
