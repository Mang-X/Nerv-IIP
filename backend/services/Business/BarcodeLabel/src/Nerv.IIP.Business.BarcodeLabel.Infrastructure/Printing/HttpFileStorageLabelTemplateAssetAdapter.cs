using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Sdk.FileStorage;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

public sealed class HttpFileStorageLabelTemplateAssetAdapter : ILabelTemplateAssetPort, IDisposable
{
    public const int MaximumAssetBytes = 65536;

    private const string RequiredOwnerService = "business-barcode-label";
    private const string RequiredOwnerType = "label-template";
    private const string RequiredPurpose = "barcode-label-template";
    private const string RequiredContentType = "application/vnd.nerv-iip.label-template+json";
    private const string RequiredStatus = "available";
    private const string Sha256Prefix = "sha256:";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IFileStorageClient fileStorageClient;
    private readonly HttpClient downloadClient;
    private readonly TimeSpan downloadTimeout;

    public HttpFileStorageLabelTemplateAssetAdapter(
        IFileStorageClient fileStorageClient,
        HttpClient downloadClient,
        TimeSpan downloadTimeout)
    {
        ArgumentNullException.ThrowIfNull(fileStorageClient);
        ArgumentNullException.ThrowIfNull(downloadClient);
        if (downloadClient.BaseAddress is not { IsAbsoluteUri: true } baseAddress
            || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "The FileStorage download client must have an absolute HTTP(S) base address.",
                nameof(downloadClient));
        }

        if (downloadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(downloadTimeout),
                "The FileStorage download timeout must be positive.");
        }

        this.fileStorageClient = fileStorageClient;
        this.downloadClient = downloadClient;
        this.downloadTimeout = downloadTimeout;
    }

    public async Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(
        LabelTemplateAssetReference reference,
        CancellationToken cancellationToken)
    {
        ValidateReference(reference);

        var metadata = await ReadMetadataAsync(reference.FileId, cancellationToken);
        var expectedDigest = ValidateMetadata(metadata, reference);
        var grant = await CreateGrantAsync(reference, cancellationToken);

        using var request = CreateDownloadRequest(grant, reference.FileId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(downloadTimeout);

        HttpResponseMessage response;
        try
        {
            response = await downloadClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure("FileStorage template download timed out.");
        }
        catch (HttpRequestException)
        {
            throw Failure("FileStorage template download failed.");
        }

        using (response)
        {
            if (IsRedirect(response.StatusCode))
            {
                throw Failure("FileStorage template download redirects are not allowed.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Failure($"FileStorage template download returned HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength is > MaximumAssetBytes)
            {
                throw Failure($"FileStorage template download exceeds {MaximumAssetBytes} bytes.");
            }

            var bytes = await ReadBoundedAsync(response.Content, timeout.Token, cancellationToken);
            if (bytes.Length == 0)
            {
                throw Failure("FileStorage template download was empty.");
            }

            if (bytes.LongLength != metadata.SizeBytes)
            {
                throw Failure("FileStorage template download size does not match metadata.");
            }

            if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            {
                throw Failure("FileStorage template must be UTF-8 without BOM.");
            }

            string json;
            try
            {
                json = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw Failure("FileStorage template contains invalid UTF-8.");
            }

            var actualDigest = SHA256.HashData(bytes);
            if (!CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest))
            {
                throw Failure("FileStorage template checksum does not match metadata.");
            }

            return new VerifiedLabelTemplateAsset(
                metadata.FileId,
                $"{Sha256Prefix}{Convert.ToHexString(actualDigest).ToLowerInvariant()}",
                json);
        }
    }

    public void Dispose() => downloadClient.Dispose();

    private async Task<FileMetadataResponse> ReadMetadataAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await fileStorageClient.GetFileMetadataAsync(fileId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure("FileStorage template metadata request timed out.");
        }
        catch (Exception exception) when (IsFileStorageRequestFailure(exception))
        {
            throw Failure("FileStorage template metadata could not be read.");
        }
    }

    private async Task<DownloadGrantResponse> CreateGrantAsync(
        LabelTemplateAssetReference reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await fileStorageClient.CreateDownloadGrantAsync(
                reference.FileId,
                new CreateDownloadGrantRequest(reference.OrganizationId, reference.EnvironmentId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure("FileStorage template download grant request timed out.");
        }
        catch (Exception exception) when (IsFileStorageRequestFailure(exception))
        {
            throw Failure("FileStorage template download grant could not be created.");
        }
    }

    private static bool IsFileStorageRequestFailure(Exception exception) =>
        exception is HttpRequestException or JsonException or InvalidOperationException or NotSupportedException;

    private static void ValidateReference(LabelTemplateAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.FileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.OrganizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.EnvironmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.TemplateCode);
    }

    private static byte[] ValidateMetadata(
        FileMetadataResponse metadata,
        LabelTemplateAssetReference reference)
    {
        if (metadata.Owner is not { } owner
            || !string.Equals(metadata.FileId, reference.FileId, StringComparison.Ordinal)
            || !string.Equals(metadata.OrganizationId, reference.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(metadata.EnvironmentId, reference.EnvironmentId, StringComparison.Ordinal)
            || !string.Equals(owner.OwnerService, RequiredOwnerService, StringComparison.Ordinal)
            || !string.Equals(owner.OwnerType, RequiredOwnerType, StringComparison.Ordinal)
            || !string.Equals(owner.OwnerId, reference.TemplateCode, StringComparison.Ordinal)
            || !string.Equals(metadata.FilePurpose, RequiredPurpose, StringComparison.Ordinal)
            || !string.Equals(metadata.ContentType, RequiredContentType, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(metadata.FileName), ".json", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(metadata.Status, RequiredStatus, StringComparison.Ordinal)
            || metadata.SizeBytes is <= 0 or > MaximumAssetBytes)
        {
            throw Failure("FileStorage template metadata does not match the required asset contract.");
        }

        return ParseSha256(metadata.Checksum);
    }

    private static byte[] ParseSha256(string? checksum)
    {
        if (checksum is null
            || !checksum.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase)
            || checksum.Length != Sha256Prefix.Length + 64)
        {
            throw Failure("FileStorage template metadata does not contain a valid SHA-256 checksum.");
        }

        try
        {
            return Convert.FromHexString(checksum[Sha256Prefix.Length..]);
        }
        catch (FormatException)
        {
            throw Failure("FileStorage template metadata does not contain a valid SHA-256 checksum.");
        }
    }

    private static HttpRequestMessage CreateDownloadRequest(
        DownloadGrantResponse grant,
        string expectedFileId)
    {
        var transfer = grant.Download;
        var url = transfer?.Url;
        if (transfer is null
            || !string.Equals(grant.FileId, expectedFileId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(url)
            || !url.StartsWith("/", StringComparison.Ordinal)
            || url.StartsWith("//", StringComparison.Ordinal)
            || url.Contains('\\')
            || transfer.Headers is null)
        {
            throw Failure("FileStorage returned an invalid template download grant.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        foreach (var header in transfer.Headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key)
                || header.Value is null
                || !request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Dispose();
                throw Failure("FileStorage returned invalid template download grant headers.");
            }
        }

        return request;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken downloadCancellationToken,
        CancellationToken callerCancellationToken)
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync(downloadCancellationToken);
            using var buffer = new MemoryStream(capacity: MaximumAssetBytes);
            var chunk = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, downloadCancellationToken);
                if (read == 0)
                {
                    return buffer.ToArray();
                }

                if (buffer.Length + read > MaximumAssetBytes)
                {
                    throw Failure($"FileStorage template download exceeds {MaximumAssetBytes} bytes.");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), downloadCancellationToken);
            }
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure("FileStorage template download timed out.");
        }
        catch (IOException)
        {
            throw Failure("FileStorage template download failed.");
        }
        catch (HttpRequestException)
        {
            throw Failure("FileStorage template download failed.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        (int)statusCode is >= 300 and <= 399;

    private static InvalidDataException Failure(string message) => new(message);
}
