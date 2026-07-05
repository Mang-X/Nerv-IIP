using System.Globalization;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

internal static class FileStoragePurposePolicies
{
    public static FileStoragePolicyValidationResult ValidateDeclaredType(
        string filePurpose,
        string fileName,
        string contentType,
        IConfiguration? configuration)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var blockedExtensions = GetStringSet(configuration, $"FileStorage:PurposePolicies:{filePurpose}:BlockedExtensions");
        if (blockedExtensions.Contains(extension))
        {
            return FileStoragePolicyValidationResult.Rejected($"File type is not allowed for purpose '{filePurpose}'.");
        }

        var allowedExtensions = GetStringSet(configuration, $"FileStorage:PurposePolicies:{filePurpose}:AllowedExtensions");
        if (allowedExtensions.Count > 0 && !allowedExtensions.Contains(extension))
        {
            return FileStoragePolicyValidationResult.Rejected($"File type is not allowed for purpose '{filePurpose}'.");
        }

        var allowedContentTypes = GetStringSet(configuration, $"FileStorage:PurposePolicies:{filePurpose}:AllowedContentTypes");
        if (allowedContentTypes.Count > 0 && !allowedContentTypes.Contains(contentType.ToLowerInvariant()))
        {
            return FileStoragePolicyValidationResult.Rejected($"File type is not allowed for purpose '{filePurpose}'.");
        }

        return FileStoragePolicyValidationResult.Accepted;
    }

    public static async Task<bool> MatchesDeclaredContentAsync(
        string fileName,
        string contentType,
        string provider,
        string uploadSessionId,
        ILocalTusFileStoreAccessor? tusStoreAccessor,
        CancellationToken cancellationToken)
    {
        var signature = ResolveExpectedSignature(fileName, contentType);
        if (signature is null)
        {
            return true;
        }

        if (!string.Equals(provider, UploadProviders.TusUploadProvider.Name, StringComparison.Ordinal)
            || tusStoreAccessor is null
            || !tusStoreAccessor.TryGet(out var store)
            || !store.Exists(uploadSessionId))
        {
            return true;
        }

        await using var stream = store.OpenRead(uploadSessionId);
        var buffer = new byte[signature.MaxHeaderBytes];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        return signature.Matches(buffer.AsSpan(0, read));
    }

    public static long? ResolveRetentionSeconds(string filePurpose, IConfiguration? configuration)
    {
        return configuration?.GetValue<long?>($"FileStorage:PurposePolicies:{filePurpose}:RetentionSeconds");
    }

    public static TimeSpan ResolvePhysicalDeleteGrace(IConfiguration? configuration)
    {
        var seconds = configuration?.GetValue<double?>("FileStorage:GarbageCollection:PhysicalDeleteGraceSeconds");
        return seconds is >= 0
            ? TimeSpan.FromSeconds(seconds.Value)
            : TimeSpan.FromDays(7);
    }

    public static FileStorageQuotaDecision CheckQuota(
        string organizationId,
        string environmentId,
        string filePurpose,
        long requestedBytes,
        long usedBytes,
        IConfiguration? configuration)
    {
        var maxBytes = configuration?.GetValue<long?>($"FileStorage:Quotas:OrganizationPurpose:{organizationId}:{environmentId}:{filePurpose}:MaxBytes")
            ?? configuration?.GetValue<long?>($"FileStorage:Quotas:Organization:{organizationId}:{environmentId}:MaxBytes")
            ?? configuration?.GetValue<long?>($"FileStorage:PurposePolicies:{filePurpose}:QuotaBytes");
        if (maxBytes is null)
        {
            return FileStorageQuotaDecision.Allowed(maxBytes, usedBytes);
        }

        return usedBytes + requestedBytes <= maxBytes.Value
            ? FileStorageQuotaDecision.Allowed(maxBytes, usedBytes)
            : FileStorageQuotaDecision.Rejected(maxBytes, usedBytes);
    }

    private static HashSet<string> GetStringSet(IConfiguration? configuration, string key)
    {
        if (configuration is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = configuration.GetSection(key)
            .GetChildren()
            .Select(child => child.Value)
            .Append(configuration[key])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.ToLower(CultureInfo.InvariantCulture));
        return values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static FileSignature? ResolveExpectedSignature(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var normalizedContentType = contentType.ToLowerInvariant();
        if (extension == ".zip" || normalizedContentType is "application/zip" or "application/x-zip-compressed")
        {
            return FileSignature.Zip;
        }

        if (extension == ".png" || normalizedContentType == "image/png")
        {
            return FileSignature.Png;
        }

        if (extension == ".pdf" || normalizedContentType == "application/pdf")
        {
            return FileSignature.Pdf;
        }

        if (extension is ".jpg" or ".jpeg" || normalizedContentType == "image/jpeg")
        {
            return FileSignature.Jpeg;
        }

        return null;
    }
}

internal sealed record FileStoragePolicyValidationResult(bool IsAllowed, string? Message)
{
    public static readonly FileStoragePolicyValidationResult Accepted = new(true, null);
    public static FileStoragePolicyValidationResult Rejected(string message) => new(false, message);
}

internal sealed record FileStorageQuotaDecision(bool IsAllowed, long? MaxBytes, long UsedBytes)
{
    public static FileStorageQuotaDecision Allowed(long? maxBytes, long usedBytes) => new(true, maxBytes, usedBytes);
    public static FileStorageQuotaDecision Rejected(long? maxBytes, long usedBytes) => new(false, maxBytes, usedBytes);
}

internal sealed class FileSignature
{
    public static readonly FileSignature Zip = new(4, bytes =>
        bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 })
        || bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x05, 0x06 })
        || bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x07, 0x08 }));

    public static readonly FileSignature Png = new(8, bytes =>
        bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));

    public static readonly FileSignature Pdf = new(5, bytes => bytes.StartsWith("%PDF-"u8));

    public static readonly FileSignature Jpeg = new(3, bytes =>
        bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }));

    private readonly SignatureMatcher matcher;

    private FileSignature(int maxHeaderBytes, SignatureMatcher matcher)
    {
        MaxHeaderBytes = maxHeaderBytes;
        this.matcher = matcher;
    }

    public int MaxHeaderBytes { get; }

    public bool Matches(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= MaxHeaderBytes && matcher(bytes);
    }

    private delegate bool SignatureMatcher(ReadOnlySpan<byte> bytes);
}
