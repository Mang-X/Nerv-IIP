namespace Nerv.IIP.FileStorage.Web.Application.Files;

public enum UploadCommitFailureDisposition
{
    FinalMayExist,
    ProvenNoFinalActionStarted
}

public sealed record UploadCommitIntent(
    string CommitId,
    string UploadSessionId,
    string FileId,
    string OrganizationId,
    string EnvironmentId,
    string FilePurpose,
    string ObjectKey,
    long ExpectedSizeBytes,
    string? ExpectedChecksum);

public sealed record UploadCommitStorageResult(
    bool IsVerified,
    long SizeBytes,
    string? CanonicalChecksum,
    int StatusCode,
    string ErrorCode,
    string Message)
{
    public UploadCommitFailureDisposition FailureDisposition { get; init; } =
        UploadCommitFailureDisposition.FinalMayExist;

    public static UploadCommitStorageResult Verified(long sizeBytes, string canonicalChecksum) =>
        new(true, sizeBytes, canonicalChecksum, StatusCodes.Status200OK, string.Empty, string.Empty);

    public static UploadCommitStorageResult RetryableUnavailable() =>
        new(
            false,
            0,
            null,
            StatusCodes.Status503ServiceUnavailable,
            "final-storage-not-ready",
            "Final storage commit is not available yet; retry this upload completion later.");

    public static UploadCommitStorageResult ProvenNoFinalActionStarted() =>
        RetryableUnavailable() with
        {
            FailureDisposition = UploadCommitFailureDisposition.ProvenNoFinalActionStarted
        };
}

public interface IUploadCommitStorage
{
    Task<UploadCommitStorageResult> CommitAsync(
        UploadCommitIntent intent,
        CancellationToken cancellationToken);
}

public sealed class UnavailableUploadCommitStorage : IUploadCommitStorage
{
    public Task<UploadCommitStorageResult> CommitAsync(
        UploadCommitIntent intent,
        CancellationToken cancellationToken) =>
        Task.FromResult(UploadCommitStorageResult.ProvenNoFinalActionStarted());
}
