using Microsoft.AspNetCore.Http;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

internal static class TusUploadCompletionValidator
{
    public static async Task<TusUploadCompletionValidationResult> ValidateAsync(
        string provider,
        string uploadSessionId,
        long expectedSizeBytes,
        string? sessionChecksum,
        CompleteUploadSessionRequest request,
        ILocalTusFileStoreAccessor? tusStoreAccessor,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(provider, TusUploadProvider.Name, StringComparison.Ordinal))
        {
            return TusUploadCompletionValidationResult.Valid;
        }

        if (tusStoreAccessor is null || !tusStoreAccessor.TryGet(out var store))
        {
            return TusUploadCompletionValidationResult.ServiceUnavailable("Tus upload store is unavailable.");
        }

        var actualSize = store.GetOffset(uploadSessionId);
        if (actualSize != expectedSizeBytes || (request.SizeBytes is not null && request.SizeBytes != actualSize))
        {
            return TusUploadCompletionValidationResult.BadRequest("Tus upload size does not match the upload session.");
        }

        var expectedChecksum = request.Checksum ?? sessionChecksum;
        if (!string.IsNullOrWhiteSpace(expectedChecksum))
        {
            var actualChecksum = await store.ComputeSha256HexAsync(uploadSessionId, cancellationToken);
            if (!ChecksumMatchesSha256Hex(expectedChecksum, actualChecksum))
            {
                return TusUploadCompletionValidationResult.BadRequest("Tus upload checksum does not match the upload session.");
            }
        }

        return TusUploadCompletionValidationResult.Valid;
    }

    private static bool ChecksumMatchesSha256Hex(string expectedChecksum, string actualSha256Hex)
    {
        var normalized = expectedChecksum.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? expectedChecksum["sha256:".Length..]
            : expectedChecksum;

        return string.Equals(normalized, actualSha256Hex, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record TusUploadCompletionValidationResult(bool IsValid, int StatusCode, string Message)
{
    public static readonly TusUploadCompletionValidationResult Valid = new(true, StatusCodes.Status200OK, string.Empty);

    public static TusUploadCompletionValidationResult BadRequest(string message)
    {
        return new(false, StatusCodes.Status400BadRequest, message);
    }

    public static TusUploadCompletionValidationResult ServiceUnavailable(string message)
    {
        return new(false, StatusCodes.Status503ServiceUnavailable, message);
    }
}
