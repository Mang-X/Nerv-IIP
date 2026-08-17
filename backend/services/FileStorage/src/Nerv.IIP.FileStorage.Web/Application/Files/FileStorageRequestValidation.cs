using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

internal static class FileStorageRequestValidation
{
    public static bool IsValidCreateUploadSessionRequest(CreateUploadSessionRequest request)
    {
        return request.Owner is not null
            && !HasBlankRequiredValue(
                request.OrganizationId,
                request.EnvironmentId,
                request.Owner.OwnerService,
                request.Owner.OwnerType,
                request.Owner.OwnerId,
                request.FileName,
                request.ContentType)
            && IsWithinMaxLength(request.OrganizationId, 128)
            && IsWithinMaxLength(request.EnvironmentId, 128)
            && IsWithinMaxLength(request.Owner.OwnerService, 128)
            && IsWithinMaxLength(request.Owner.OwnerType, 128)
            && IsWithinMaxLength(request.Owner.OwnerId, 128)
            && IsWithinMaxLength(request.FilePurpose, 128)
            && IsWithinMaxLength(request.FileName, 512)
            && IsWithinMaxLength(request.ContentType, 256)
            && IsWithinMaxLength(request.Checksum, 256)
            && request.ExpectedSizeBytes >= 0;
    }

    public static int NormalizeSkip(int? skip) => skip is > 0 ? skip.Value : 0;

    public static int NormalizeTake(int? take) => take is > 0 ? Math.Min(take.Value, 200) : 50;

    private static bool HasBlankRequiredValue(params string[] values)
    {
        return values.Any(string.IsNullOrWhiteSpace);
    }

    private static bool IsWithinMaxLength(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength;
    }
}
