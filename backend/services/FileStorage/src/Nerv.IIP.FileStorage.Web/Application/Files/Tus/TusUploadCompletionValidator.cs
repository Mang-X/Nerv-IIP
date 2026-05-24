using Microsoft.AspNetCore.Http;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

internal static class TusUploadCompletionValidator
{
    public static async Task<TusUploadCompletionValidationFailure?> ValidateAsync(
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
            return null;
        }

        if (tusStoreAccessor is null || !tusStoreAccessor.TryGet(out var store))
        {
            return TusUploadCompletionValidationFailure.ServiceUnavailable("Tus upload store is unavailable.");
        }

        var actualSize = store.GetOffset(uploadSessionId);
        if (actualSize != expectedSizeBytes || (request.SizeBytes is not null && request.SizeBytes != actualSize))
        {
            return TusUploadCompletionValidationFailure.BadRequest("Tus upload size does not match the upload session.");
        }

        var expectedChecksum = request.Checksum ?? sessionChecksum;
        if (!string.IsNullOrWhiteSpace(expectedChecksum))
        {
            var actualChecksum = await store.ComputeSha256HexAsync(uploadSessionId, cancellationToken);
            if (!ChecksumMatchesSha256Hex(expectedChecksum, actualChecksum))
            {
                return TusUploadCompletionValidationFailure.BadRequest("Tus upload checksum does not match the upload session.");
            }
        }

        return null;
    }

    private static bool ChecksumMatchesSha256Hex(string expectedChecksum, string actualSha256Hex)
    {
        var normalized = expectedChecksum.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? expectedChecksum["sha256:".Length..]
            : expectedChecksum;

        return string.Equals(normalized, actualSha256Hex, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record TusUploadCompletionValidationFailure(int StatusCode, string Message)
{
    public static TusUploadCompletionValidationFailure BadRequest(string message)
    {
        return new(StatusCodes.Status400BadRequest, message);
    }

    public static TusUploadCompletionValidationFailure ServiceUnavailable(string message)
    {
        return new(StatusCodes.Status503ServiceUnavailable, message);
    }
}
