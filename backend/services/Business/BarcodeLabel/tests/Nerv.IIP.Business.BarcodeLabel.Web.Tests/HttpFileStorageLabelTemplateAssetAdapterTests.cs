using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Sdk.FileStorage;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class HttpFileStorageLabelTemplateAssetAdapterTests
{
    private const int MaximumAssetBytes = HttpFileStorageLabelTemplateAssetAdapter.MaximumAssetBytes;

    [Fact]
    public void Maximum_asset_size_matches_the_approved_sixty_four_kibibyte_contract()
    {
        Assert.Equal(65536, HttpFileStorageLabelTemplateAssetAdapter.MaximumAssetBytes);
    }

    private const string TemplateJson = """
        {"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}
        """;

    [Fact]
    public async Task GetVerifiedAsync_ValidMetadataAndBodyWithoutContentLength_ReturnsCanonicalVerifiedAsset()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var metadata = CreateMetadata(bytes);
        var fileStorage = new RecordingFileStorageClient(
            metadata with { Checksum = metadata.Checksum!.ToUpperInvariant() });
        var http = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("grant-value", request.Headers.GetValues("x-nerv-download-grant").Single());
            return Response(HttpStatusCode.OK, new UnknownLengthContent(bytes));
        });
        using var adapter = CreateAdapter(fileStorage, http);

        var asset = await adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None);

        Assert.Equal("file-template-001", asset.FileId);
        Assert.Equal(Sha256(bytes).ToLowerInvariant(), asset.Sha256);
        Assert.Equal(TemplateJson, asset.Json);
        Assert.Equal(1, fileStorage.MetadataCalls);
        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(1, http.Calls);
    }

    public static TheoryData<Func<FileMetadataResponse, FileMetadataResponse>> InvalidMetadataCases => new()
    {
        metadata => metadata with { OrganizationId = "other-org" },
        metadata => metadata with { EnvironmentId = "other-env" },
        metadata => metadata with { FileId = "other-file" },
        metadata => metadata with { Owner = metadata.Owner with { OwnerService = "other-service" } },
        metadata => metadata with { Owner = metadata.Owner with { OwnerType = "other-owner" } },
        metadata => metadata with { Owner = metadata.Owner with { OwnerId = "OTHER-TEMPLATE" } },
        metadata => metadata with { FilePurpose = "attachment" },
        metadata => metadata with { ContentType = "application/json" },
        metadata => metadata with { FileName = "template.txt" },
        metadata => metadata with { Status = "pending" },
        metadata => metadata with { SizeBytes = 0 },
        metadata => metadata with { SizeBytes = MaximumAssetBytes + 1L },
        metadata => metadata with { Checksum = null },
        metadata => metadata with { Checksum = "" },
        metadata => metadata with { Checksum = "sha256:not-a-digest" },
        metadata => metadata with { Checksum = $"sha256:{new string('g', 64)}" },
    };

    [Theory]
    [MemberData(nameof(InvalidMetadataCases))]
    public async Task GetVerifiedAsync_InvalidMetadata_FailsBeforeGrantOrDownload(
        Func<FileMetadataResponse, FileMetadataResponse> mutate)
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(mutate(CreateMetadata(bytes)));
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, bytes));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, fileStorage.MetadataCalls);
        Assert.Equal(0, fileStorage.GrantCalls);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_MetadataFailure_DoesNotRequestGrantOrDownload()
    {
        var fileStorage = new RecordingFileStorageClient(
            CreateMetadata(Encoding.UTF8.GetBytes(TemplateJson)),
            metadataFailure: new HttpRequestException("metadata unavailable"));
        var http = new RecordingHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("HTTP must not be called."));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, fileStorage.MetadataCalls);
        Assert.Equal(0, fileStorage.GrantCalls);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_GrantFailure_DoesNotDownloadOrRetryGrant()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(
            CreateMetadata(bytes),
            grantFailure: new HttpRequestException("grant unavailable"));
        var http = new RecordingHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("HTTP must not be called."));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_AbsoluteGrantUrl_IsRejectedBeforeDownload()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(
            CreateMetadata(bytes),
            grantUrl: "https://untrusted.invalid/api/files/v1/download-grants/grant-secret/content");
        var http = new RecordingHttpMessageHandler(_ =>
            throw new Xunit.Sdk.XunitException("HTTP must not be called."));
        using var adapter = CreateAdapter(fileStorage, http);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal("FileStorage returned an invalid template download grant.", exception.Message);
        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(0, http.Calls);
    }

    [Theory]
    [InlineData("api/files/v1/download-grants/grant-secret/content")]
    [InlineData("//untrusted.invalid/api/files/v1/download-grants/grant-secret/content")]
    public async Task GetVerifiedAsync_NonRootedOrNetworkPathGrantUrl_IsRejectedBeforeDownload(string grantUrl)
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes), grantUrl: grantUrl);
        var http = new RecordingHttpMessageHandler(_ =>
            throw new Xunit.Sdk.XunitException("HTTP must not be called."));
        using var adapter = CreateAdapter(fileStorage, http);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal("FileStorage returned an invalid template download grant.", exception.Message);
        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_ChecksumMismatch_RejectsDownloadedBytesWithoutRetry()
    {
        var declaredBytes = Encoding.UTF8.GetBytes(TemplateJson);
        var downloadedBytes = Encoding.UTF8.GetBytes(TemplateJson + " ");
        var fileStorage = new RecordingFileStorageClient(
            CreateMetadata(declaredBytes) with { SizeBytes = downloadedBytes.Length });
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, downloadedBytes));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(1, http.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_MetadataSizeDoesNotMatchBody_RejectsDownloadedBytes()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes) with { SizeBytes = bytes.Length - 1 });
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, bytes));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, http.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_Utf8Bom_IsRejected()
    {
        var body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(TemplateJson)).ToArray();
        await AssertRejectedBodyAsync(body, body);
    }

    [Fact]
    public async Task GetVerifiedAsync_InvalidUtf8_IsRejected()
    {
        byte[] body = [0xc3, 0x28];
        await AssertRejectedBodyAsync(body, body);
    }

    [Fact]
    public async Task GetVerifiedAsync_EmptyBody_IsRejected()
    {
        var metadataBytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(metadataBytes) with { SizeBytes = 1 });
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, []));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));
    }

    [Fact]
    public async Task GetVerifiedAsync_Redirect_IsRejectedWithoutFollowingOrRetrying()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var http = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://redirect.invalid/template.json") }
        });
        using var adapter = CreateAdapter(fileStorage, http);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal("FileStorage template download redirects are not allowed.", exception.Message);
        Assert.Equal(1, http.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_DefaultDownloadClient_DoesNotFollowRedirectOnLoopback()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var baseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        using var serverCancellation = new CancellationTokenSource();
        var server = ServeRedirectAndPossibleFollowAsync(listener, bytes, serverCancellation.Token);
        using var adapter = new HttpFileStorageLabelTemplateAssetAdapter(
            fileStorage,
            baseAddress,
            TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        serverCancellation.Cancel();
        var acceptedRequests = await server;
        Assert.Equal(1, acceptedRequests);
    }

    [Fact]
    public async Task GetVerifiedAsync_NonSuccessResponse_IsRejectedWithoutRetrying()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.ServiceUnavailable, bytes));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, http.Calls);
        Assert.Equal(1, fileStorage.GrantCalls);
    }

    [Fact]
    public async Task GetVerifiedAsync_Timeout_IsRejectedWithoutRetrying()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var http = new RecordingHttpMessageHandler(async (_, cancellationToken) =>
        {
            await PendingOperation.UntilCanceledAsync(cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        using var adapter = CreateAdapter(fileStorage, http, TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, http.Calls);
        Assert.Equal(1, fileStorage.GrantCalls);
    }

    [Fact]
    public async Task GetVerifiedAsync_CallerCancellation_IsPropagatedWithoutRetrying()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var http = new RecordingHttpMessageHandler(async (_, cancellationToken) =>
        {
            await PendingOperation.UntilCanceledAsync(cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        using var adapter = CreateAdapter(fileStorage, http, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), cancellation.Token));

        Assert.Equal(1, http.Calls);
        Assert.Equal(1, fileStorage.GrantCalls);
    }

    [Fact]
    public async Task GetVerifiedAsync_StreamWithoutContentLengthAboveSixtyFourKiB_IsRejected()
    {
        var metadataBytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(metadataBytes));
        var overflow = Enumerable.Repeat((byte)'a', MaximumAssetBytes + 1).ToArray();
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, new UnknownLengthContent(overflow)));
        using var adapter = CreateAdapter(fileStorage, http);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal($"FileStorage template download exceeds {MaximumAssetBytes} bytes.", exception.Message);
        Assert.Equal(1, http.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_ContentLengthAboveSixtyFourKiB_IsRejectedBeforeReadingBody()
    {
        var metadataBytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(metadataBytes));
        var content = new ThrowOnReadContent(MaximumAssetBytes + 1L);
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, content));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.False(content.WasRead);
    }

    [Fact]
    public async Task GetVerifiedAsync_ExactlySixtyFourKiB_IsAccepted()
    {
        var prefix = Encoding.UTF8.GetBytes(TemplateJson);
        var bytes = new byte[MaximumAssetBytes];
        prefix.CopyTo(bytes, 0);
        Array.Fill(bytes, (byte)' ', prefix.Length, bytes.Length - prefix.Length);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, bytes));
        using var adapter = CreateAdapter(fileStorage, http);

        var asset = await adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None);

        Assert.Equal(MaximumAssetBytes, Encoding.UTF8.GetByteCount(asset.Json));
    }

    private static async Task AssertRejectedBodyAsync(byte[] metadataBytes, byte[] downloadedBytes)
    {
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(metadataBytes));
        var http = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, downloadedBytes));
        using var adapter = CreateAdapter(fileStorage, http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));
    }

    private static async Task<int> ServeRedirectAndPossibleFollowAsync(
        TcpListener listener,
        byte[] successBytes,
        CancellationToken cancellationToken)
    {
        var accepted = 0;
        try
        {
            using (var first = await listener.AcceptTcpClientAsync(cancellationToken))
            {
                accepted++;
                await ReadRequestHeadersAsync(first, cancellationToken);
                await WriteResponseAsync(
                    first,
                    "HTTP/1.1 302 Found\r\nLocation: /followed.json\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                    cancellationToken);
            }

            using var second = await listener.AcceptTcpClientAsync(cancellationToken);
            accepted++;
            await ReadRequestHeadersAsync(second, cancellationToken);
            var headers = $"HTTP/1.1 200 OK\r\nContent-Length: {successBytes.Length}\r\nConnection: close\r\n\r\n";
            await WriteResponseAsync(second, headers, cancellationToken);
            await second.GetStream().WriteAsync(successBytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return accepted;
    }

    private static async Task ReadRequestHeadersAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        var bytes = new byte[4096];
        var received = new List<byte>();
        while (received.Count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(0, bytes.Length - received.Count), cancellationToken);
            if (read == 0)
            {
                return;
            }

            received.AddRange(bytes.AsSpan(0, read).ToArray());
            if (received.Count >= 4
                && received[^4] == '\r'
                && received[^3] == '\n'
                && received[^2] == '\r'
                && received[^1] == '\n')
            {
                return;
            }
        }
    }

    private static Task WriteResponseAsync(
        TcpClient client,
        string response,
        CancellationToken cancellationToken) =>
        client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).AsTask();

    private static HttpFileStorageLabelTemplateAssetAdapter CreateAdapter(
        RecordingFileStorageClient fileStorage,
        HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        new(
            fileStorage,
            handler,
            new Uri("https://file-storage.invalid/"),
            timeout ?? TimeSpan.FromSeconds(1));

    private static LabelTemplateAssetReference CreateReference() =>
        new("file-template-001", "org-001", "prod", "TPL-001");

    private static FileMetadataResponse CreateMetadata(byte[] bytes) =>
        new(
            "file-template-001",
            "org-001",
            "prod",
            new OwnerReference("business-barcode-label", "label-template", "TPL-001"),
            "barcode-label-template",
            "template.json",
            "application/vnd.nerv-iip.label-template+json",
            bytes.Length,
            Sha256(bytes),
            "available",
            DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-24T00:01:00Z"));

    private static string Sha256(byte[] bytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";

    private static HttpResponseMessage Response(HttpStatusCode statusCode, byte[] bytes) =>
        Response(statusCode, new ByteArrayContent(bytes));

    private static HttpResponseMessage Response(HttpStatusCode statusCode, HttpContent content) =>
        new(statusCode) { Content = content };

    private sealed class RecordingFileStorageClient(
        FileMetadataResponse metadata,
        Exception? metadataFailure = null,
        Exception? grantFailure = null,
        string grantUrl = "/api/files/v1/download-grants/grant-secret/content") : IFileStorageClient
    {
        public int MetadataCalls { get; private set; }
        public int GrantCalls { get; private set; }

        public Task<FileMetadataResponse> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default)
        {
            MetadataCalls++;
            return metadataFailure is null
                ? Task.FromResult(metadata)
                : Task.FromException<FileMetadataResponse>(metadataFailure);
        }

        public Task<DownloadGrantResponse> CreateDownloadGrantAsync(
            string fileId,
            CreateDownloadGrantRequest request,
            CancellationToken cancellationToken = default)
        {
            GrantCalls++;
            return grantFailure is null
                ? Task.FromResult(new DownloadGrantResponse(
                    fileId,
                    DateTimeOffset.Parse("2026-08-24T00:10:00Z"),
                    new TransferInstructions(
                        grantUrl,
                        new Dictionary<string, string> { ["x-nerv-download-grant"] = "grant-value" })))
                : Task.FromException<DownloadGrantResponse>(grantFailure);
        }

        public Task<CreateUploadSessionResponse> CreateUploadSessionAsync(CreateUploadSessionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileMetadataResponse> CompleteUploadSessionAsync(string uploadSessionId, CompleteUploadSessionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileStorageUsageResponse> GetUsageAsync(FileStorageUsageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this((request, _) => Task.FromResult(send(request)))
        {
        }

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            this.send = send;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ThrowOnReadContent : HttpContent
    {
        public ThrowOnReadContent(long contentLength)
        {
            Headers.ContentLength = contentLength;
        }

        public bool WasRead { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            WasRead = true;
            throw new Xunit.Sdk.XunitException("Body must not be read.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength!.Value;
            return true;
        }
    }
}
