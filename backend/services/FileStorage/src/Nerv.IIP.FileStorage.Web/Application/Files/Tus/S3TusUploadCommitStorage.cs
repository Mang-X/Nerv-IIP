namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

internal sealed class S3TusUploadCommitStorage(ILocalTusFileStoreAccessor accessor, S3FinalFileStore s3)
    : IUploadCommitStorage
{
    public Task<UploadCommitStorageResult> CommitAsync(
        UploadCommitIntent intent, CancellationToken cancellationToken) =>
        accessor.TryGet(out var staging)
            ? s3.CommitAsync(staging, intent, cancellationToken)
            : Task.FromResult(UploadCommitStorageResult.ProvenNoFinalActionStarted());
}
