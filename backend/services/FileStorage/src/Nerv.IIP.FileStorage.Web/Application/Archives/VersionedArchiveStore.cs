using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Minio;
using Minio.DataModel.Args;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.FileStorage.Web.Application.Archives;

public interface IVersionedObjectStore
{
    Task<bool> IsVersioningEnabledAsync(CancellationToken cancellationToken);
    Task<string> PutAsync(
        string objectKey,
        ReadOnlyMemory<byte> content,
        string contentType,
        string sha256,
        CancellationToken cancellationToken);
    Task<byte[]> GetAsync(string objectKey, string versionId, CancellationToken cancellationToken);
    Task SetLegalHoldAsync(string objectKey, string versionId, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, string versionId, CancellationToken cancellationToken);
}

public sealed class UnavailableVersionedObjectStore : IVersionedObjectStore
{
    public Task<bool> IsVersioningEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<string> PutAsync(string objectKey, ReadOnlyMemory<byte> content, string contentType, string sha256, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Versioned object storage is not configured.");
    public Task<byte[]> GetAsync(string objectKey, string versionId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Versioned object storage is not configured.");
    public Task SetLegalHoldAsync(string objectKey, string versionId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Versioned object storage is not configured.");
    public Task DeleteAsync(string objectKey, string versionId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Versioned object storage is not configured.");
}

public sealed class MinioVersionedObjectStore(IMinioClient client, string bucket) : IVersionedObjectStore
{
    public async Task<bool> IsVersioningEnabledAsync(CancellationToken cancellationToken)
    {
        if (!await client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucket), cancellationToken))
        {
            return false;
        }

        var configuration = await client.GetVersioningAsync(
            new GetVersioningArgs().WithBucket(bucket), cancellationToken);
        return string.Equals(configuration.Status, "Enabled", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> PutAsync(
        string objectKey,
        ReadOnlyMemory<byte> content,
        string contentType,
        string sha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(content.ToArray(), writable: false);
        await client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(content.Length)
                .WithContentType(contentType)
                .WithHeaders(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x-amz-meta-sha256"] = sha256,
                }),
            cancellationToken);
        var stat = await client.StatObjectAsync(
            new StatObjectArgs().WithBucket(bucket).WithObject(objectKey), cancellationToken);
        return stat.VersionId;
    }

    public async Task<byte[]> GetAsync(
        string objectKey,
        string versionId,
        CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        await client.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithVersionId(versionId)
                .WithCallbackStream((stream, token) => stream.CopyToAsync(output, token)),
            cancellationToken);
        return output.ToArray();
    }

    public Task SetLegalHoldAsync(
        string objectKey,
        string versionId,
        CancellationToken cancellationToken) =>
        client.SetObjectLegalHoldAsync(
            new SetObjectLegalHoldArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithVersionId(versionId)
                .WithLegalHold(true),
            cancellationToken);

    public Task DeleteAsync(
        string objectKey,
        string versionId,
        CancellationToken cancellationToken) =>
        client.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithVersionId(versionId),
            cancellationToken);
}

public sealed partial class VersionedArchiveService(
    IVersionedObjectStore objectStore,
    TimeProvider timeProvider)
{
    public async Task<VersionedArchiveEvidence> PutAsync(
        PutVersionedArchiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSegment(request.OrganizationId, nameof(request.OrganizationId));
        ValidateSegment(request.EnvironmentId, nameof(request.EnvironmentId));
        ValidateSegment(request.ArchiveKind, nameof(request.ArchiveKind));
        ValidateSegment(request.BatchId, nameof(request.BatchId));
        if (!await objectStore.IsVersioningEnabledAsync(cancellationToken))
        {
            throw new InvalidOperationException("Versioned archive storage is unavailable because bucket versioning is not enabled.");
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Archive content is not valid base64.", nameof(request), exception);
        }

        var sha256 = Hash(content);
        if (!string.Equals(sha256, request.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Archive content checksum does not match the declared SHA-256.", nameof(request));
        }

        var objectKey = $"compliance-archives/{request.OrganizationId}/{request.EnvironmentId}/{request.ArchiveKind}/{request.BatchId}.json";
        var versionId = await objectStore.PutAsync(
            objectKey, content, request.ContentType, sha256, cancellationToken);
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new InvalidOperationException("Versioned archive storage did not return an object version id.");
        }

        var readBack = await objectStore.GetAsync(objectKey, versionId, cancellationToken);
        if (readBack.LongLength != content.LongLength || !string.Equals(Hash(readBack), sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Versioned archive read-back evidence is incomplete or does not match the uploaded content.");
        }
        if (request.LegalHold)
        {
            await objectStore.SetLegalHoldAsync(objectKey, versionId, cancellationToken);
        }

        return new VersionedArchiveEvidence(objectKey, versionId, sha256, content.LongLength, timeProvider.GetUtcNow());
    }

    public async Task<GetVersionedArchiveResponse> GetAsync(
        GetVersionedArchiveRequest request,
        CancellationToken cancellationToken)
    {
        ValidateScopeKey(request.OrganizationId, request.EnvironmentId, request.ObjectKey);
        var content = await objectStore.GetAsync(request.ObjectKey, request.VersionId, cancellationToken);
        if (content.LongLength != request.SizeBytes || !string.Equals(Hash(content), request.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Archived object evidence does not match the requested version.");
        }
        var evidence = new VersionedArchiveEvidence(
            request.ObjectKey, request.VersionId, request.Sha256.ToLowerInvariant(), request.SizeBytes, timeProvider.GetUtcNow());
        return new GetVersionedArchiveResponse(evidence, Convert.ToBase64String(content));
    }

    public Task DeleteAsync(DeleteVersionedArchiveRequest request, CancellationToken cancellationToken)
    {
        ValidateScopeKey(request.OrganizationId, request.EnvironmentId, request.ObjectKey);
        if (string.IsNullOrWhiteSpace(request.AuthorizationReference) ||
            string.IsNullOrWhiteSpace(request.Actor) ||
            string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("Exact-version archive deletion requires an authorization reference, actor, and reason.", nameof(request));
        }
        return objectStore.DeleteAsync(request.ObjectKey, request.VersionId, cancellationToken);
    }

    private static void ValidateScopeKey(string organizationId, string environmentId, string objectKey)
    {
        ValidateSegment(organizationId, nameof(organizationId));
        ValidateSegment(environmentId, nameof(environmentId));
        var prefix = $"compliance-archives/{organizationId}/{environmentId}/";
        if (!objectKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Archive object key does not belong to the requested organization/environment scope.", nameof(objectKey));
        }
    }

    private static void ValidateSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !ArchiveSegmentRegex().IsMatch(value))
        {
            throw new ArgumentException("Archive key segments must contain 1-128 letters, digits, dots, underscores, or hyphens.", parameterName);
        }
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveSegmentRegex();
}
