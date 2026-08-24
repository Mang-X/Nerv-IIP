using System.Net;
using System.Security.Cryptography;
using System.Text;
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
        Uri downloadBaseAddress,
        TimeSpan downloadTimeout)
        : this(
            fileStorageClient,
            CreateDownloadHandler(downloadTimeout),
            downloadBaseAddress,
            downloadTimeout)
    {
    }

    internal HttpFileStorageLabelTemplateAssetAdapter(
        IFileStorageClient fileStorageClient,
        HttpMessageHandler downloadHandler,
        Uri downloadBaseAddress,
        TimeSpan downloadTimeout)
    {
        ArgumentNullException.ThrowIfNull(fileStorageClient);
        ArgumentNullException.ThrowIfNull(downloadHandler);
        ArgumentNullException.ThrowIfNull(downloadBaseAddress);
        if (!downloadBaseAddress.IsAbsoluteUri
            || (!string.Equals(downloadBaseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                && !string.Equals(downloadBaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The FileStorage download base address must be an absolute HTTP(S) URI.", nameof(downloadBaseAddress));
        }

        if (downloadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(downloadTimeout), "The FileStorage download timeout must be positive.");
        }

        this.fileStorageClient = fileStorageClient;
        this.downloadTimeout = downloadTimeout;
        downloadClient = new HttpClient(downloadHandler, disposeHandler: true)
        {
            BaseAddress = downloadBaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public async Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(
        LabelTemplateAssetReference reference,
        CancellationToken cancellationToken)
    {
        ValidateReference(reference);

        FileMetadataResponse metadata;
        try
        {
            metadata = await fileStorageClient.GetFileMetadataAsync(reference.FileId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw Failure("FileStorage template metadata could not be read.");
        }

        var expectedDigest = ValidateMetadata(metadata, reference);

        DownloadGrantResponse grant;
        try
        {
            grant = await fileStorageClient.CreateDownloadGrantAsync(
                reference.FileId,
                new CreateDownloadGrantRequest(reference.OrganizationId, reference.EnvironmentId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw Failure("FileStorage template download grant could not be created.");
        }

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
        catch
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

    private static SocketsHttpHandler CreateDownloadHandler(TimeSpan downloadTimeout)
    {
        if (downloadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(downloadTimeout), "The FileStorage download timeout must be positive.");
        }

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = downloadTimeout,
        };
    }

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
        if (metadata is null
            || metadata.Owner is null
            || !string.Equals(metadata.FileId, reference.FileId, StringComparison.Ordinal)
            || !string.Equals(metadata.OrganizationId, reference.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(metadata.EnvironmentId, reference.EnvironmentId, StringComparison.Ordinal)
            || !string.Equals(metadata.Owner.OwnerService, RequiredOwnerService, StringComparison.Ordinal)
            || !string.Equals(metadata.Owner.OwnerType, RequiredOwnerType, StringComparison.Ordinal)
            || !string.Equals(metadata.Owner.OwnerId, reference.TemplateCode, StringComparison.Ordinal)
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
        if (grant is null
            || grant.Download is null
            || !string.Equals(grant.FileId, expectedFileId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(grant.Download.Url)
            || !grant.Download.Url.StartsWith("/", StringComparison.Ordinal)
            || grant.Download.Url.StartsWith("//", StringComparison.Ordinal)
            || grant.Download.Headers is null)
        {
            throw Failure("FileStorage returned an invalid template download grant.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(grant.Download.Url, UriKind.Relative));
        foreach (var header in grant.Download.Headers)
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
        catch (InvalidDataException)
        {
            throw;
        }
        catch
        {
            throw Failure("FileStorage template download failed.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        (int)statusCode is >= 300 and <= 399;

    private static InvalidDataException Failure(string message) => new(message);
}
