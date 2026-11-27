using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed class S3FinalFileStore(
    IConfiguration configuration,
    IHttpClientFactory clients,
    TimeProvider clock)
{
    public const string ClientName = "FileStorageFinalS3";
    internal const long MaximumObjectBytes = 5L * 1024 * 1024 * 1024;
    private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private readonly S3FileStorageOptions? options = S3FileStorageOptions.Load(configuration);

    public bool IsEnabled => options is not null;

    internal bool AcceptsKey(string objectKey) =>
        S3FileStorageOptions.IsSafeKey(WithPrefix(objectKey)) && S3FileStorageOptions.IsSafeKey(objectKey);

    internal async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Head, null, cancellationToken);
            return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : Unhealthy();
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unhealthy();
        }

        static HealthCheckResult Unhealthy() => HealthCheckResult.Unhealthy("The S3 bucket is unavailable.");
    }

    public async Task<HttpResponseMessage?> TryGetAsync(string objectKey, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, objectKey, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            response.Dispose();
            throw Unavailable();
        }
        return response;
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, objectKey, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw Unavailable();
        }
    }

    internal async Task<UploadCommitStorageResult> CommitAsync(
        LocalUploadByteStore staging, UploadCommitIntent intent, CancellationToken cancellationToken)
    {
        if (!AcceptsKey(intent.ObjectKey) || intent.ExpectedChecksum is null)
        {
            return UploadCommitStorageResult.ProvenNoFinalActionStarted();
        }

        var existing = await ReadEvidenceAsync(intent.ObjectKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (!staging.Exists(intent.UploadSessionId))
        {
            return UploadCommitStorageResult.ProvenNoFinalActionStarted();
        }

        await using var bytes = staging.OpenRead(intent.UploadSessionId);
        if (bytes.Length != intent.ExpectedSizeBytes || bytes.Length > MaximumObjectBytes)
        {
            return InvalidStaging();
        }
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(bytes, cancellationToken));
        if (!string.Equals($"sha256:{hash}", intent.ExpectedChecksum, StringComparison.Ordinal))
        {
            return InvalidStaging();
        }

        bytes.Position = 0;
        using var content = new StreamContent(bytes);
        content.Headers.ContentLength = bytes.Length;
        using var response = await SendAsync(HttpMethod.Put, intent.ObjectKey, cancellationToken, content, hash);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PreconditionFailed)
        {
            return UploadCommitStorageResult.RetryableUnavailable();
        }

        return await ReadEvidenceAsync(intent.ObjectKey, cancellationToken)
            ?? UploadCommitStorageResult.RetryableUnavailable();

        static UploadCommitStorageResult InvalidStaging() => UploadCommitStorageResult.ProvenNoFinalActionStarted() with
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ErrorCode = "invalid-staging-bytes",
            Message = "暂存字节与上传大小或 SHA-256 不匹配。"
        };
    }

    internal async Task<UploadCommitStorageResult?> ReadEvidenceAsync(string key, CancellationToken cancellationToken)
    {
        using var response = await TryGetAsync(key, cancellationToken);
        if (response is null)
        {
            return null;
        }
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long size = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            {
                hash.AppendData(buffer, 0, read);
                size += read;
            }
            return UploadCommitStorageResult.Verified(size, $"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}");
        }
        catch (Exception error) when (IsTransportFailure(error, cancellationToken))
        {
            throw Unavailable();
        }
    }

    private string WithPrefix(string key) => options?.KeyPrefix is { } prefix ? $"{prefix}/{key}" : key;

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string? key, CancellationToken cancellationToken,
        HttpContent? content = null, string payloadHash = EmptyHash)
    {
        var settings = options ?? throw new InvalidOperationException("S3 storage is disabled.");
        if (key is not null && !AcceptsKey(key))
        {
            throw new IOException("The S3 object key is invalid.");
        }

        var endpoint = new UriBuilder(settings.Endpoint);
        var path = "/";
        if (settings.ForcePathStyle)
        {
            path += settings.Bucket + "/";
        }
        else
        {
            endpoint.Host = $"{settings.Bucket}.{endpoint.Host}";
        }
        if (key is not null)
        {
            path += string.Join('/', WithPrefix(key).Split('/').Select(Uri.EscapeDataString));
        }
        else if (settings.ForcePathStyle)
        {
            path = path.TrimEnd('/');
        }

        using var request = new HttpRequestMessage(method, new Uri(endpoint.Uri.GetLeftPart(UriPartial.Authority) + path))
        {
            Content = content
        };
        if (method == HttpMethod.Put)
        {
            request.Headers.IfNoneMatch.Add(System.Net.Http.Headers.EntityTagHeaderValue.Any);
        }
        Sign(request, payloadHash, settings);
        using var client = clients.CreateClient(ClientName);
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception error) when (IsTransportFailure(error, cancellationToken))
        {
            throw Unavailable();
        }
    }

    private void Sign(HttpRequestMessage request, string payloadHash, S3FileStorageOptions settings)
    {
        var uri = request.RequestUri!;
        var host = uri.Authority;
        var timestamp = clock.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = timestamp[..8];
        var scope = $"{date}/{settings.Region}/s3/aws4_request";
        var canonicalHeaders = $"host:{host}\n";
        var signedHeaders = "host";
        if (request.Method == HttpMethod.Put)
        {
            canonicalHeaders += "if-none-match:*\n";
            signedHeaders += ";if-none-match";
        }
        canonicalHeaders += $"x-amz-content-sha256:{payloadHash}\nx-amz-date:{timestamp}\n";
        signedHeaders += ";x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"{request.Method}\n{uri.AbsolutePath}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
        var signingKey = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + settings.SecretKey), date),
            settings.Region), "s3"), "aws4_request");
        var signature = Convert.ToHexStringLower(Hmac(signingKey, $"AWS4-HMAC-SHA256\n{timestamp}\n{scope}\n{requestHash}"));

        request.Headers.Host = host;
        request.Headers.Add("x-amz-content-sha256", payloadHash);
        request.Headers.Add("x-amz-date", timestamp);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"AWS4-HMAC-SHA256 Credential={settings.AccessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static byte[] Hmac(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));

    private static bool IsTransportFailure(Exception error, CancellationToken ct) =>
        !ct.IsCancellationRequested && error is HttpRequestException or IOException or OperationCanceledException;

    private static IOException Unavailable() => new("The S3 object store is unavailable.");
}
