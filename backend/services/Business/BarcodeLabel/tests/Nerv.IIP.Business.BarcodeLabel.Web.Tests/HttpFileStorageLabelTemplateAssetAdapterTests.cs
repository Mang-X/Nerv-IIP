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
    private const int MaximumAssetBytes = 65536;
    private const string TemplateJson = """
        {"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}
        """;

    [Fact]
    public async Task GetVerifiedAsync_valid_asset_returns_canonical_digest_and_json()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes) with
        {
            Checksum = Sha256(bytes).ToUpperInvariant()
        });
        var download = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("org-001", request.Headers.GetValues("x-nerv-organization-id").Single());
            Assert.Equal("prod", request.Headers.GetValues("x-nerv-environment-id").Single());
            return Response(HttpStatusCode.OK, new UnknownLengthContent(bytes));
        });
        using var adapter = CreateAdapter(fileStorage, download);

        var asset = await adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None);

        Assert.Equal("file-template-001", asset.FileId);
        Assert.Equal(Sha256(bytes), asset.Sha256);
        Assert.Equal(TemplateJson, asset.Json);
        Assert.Equal(1, fileStorage.MetadataCalls);
        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(1, download.Calls);
        Assert.Equal(new CreateDownloadGrantRequest("org-001", "prod"), fileStorage.LastGrantRequest);
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
    public async Task GetVerifiedAsync_invalid_metadata_fails_before_grant_and_download(
        Func<FileMetadataResponse, FileMetadataResponse> mutate)
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(mutate(CreateMetadata(bytes)));
        var download = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, bytes));
        using var adapter = CreateAdapter(fileStorage, download);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(0, fileStorage.GrantCalls);
        Assert.Equal(0, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_grant_failure_does_not_download_or_retry()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(
            CreateMetadata(bytes),
            grantFailure: new HttpRequestException("grant-secret-url"));
        var download = new RecordingHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("Download must not run."));
        using var adapter = CreateAdapter(fileStorage, download);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.DoesNotContain("grant-secret-url", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(0, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_metadata_failure_does_not_request_grant_or_leak_diagnostics()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(
            CreateMetadata(bytes),
            metadataFailure: new HttpRequestException("metadata-sensitive-url"));
        var download = new RecordingHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("Download must not run."));
        using var adapter = CreateAdapter(fileStorage, download);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.DoesNotContain("metadata-sensitive-url", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, fileStorage.MetadataCalls);
        Assert.Equal(0, fileStorage.GrantCalls);
        Assert.Equal(0, download.Calls);
    }

    [Theory]
    [InlineData("https://untrusted.invalid/api/files/v1/download-grants/grant-secret/content")]
    [InlineData("api/files/v1/download-grants/grant-secret/content")]
    [InlineData("//untrusted.invalid/api/files/v1/download-grants/grant-secret/content")]
    public async Task GetVerifiedAsync_untrusted_grant_url_is_rejected_before_download(string grantUrl)
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes), grantUrl: grantUrl);
        var download = new RecordingHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("Download must not run."));
        using var adapter = CreateAdapter(fileStorage, download);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.DoesNotContain("grant-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_missing_grant_headers_is_rejected_before_download()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes), nullGrantHeaders: true);
        var download = new RecordingHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("Download must not run."));
        using var adapter = CreateAdapter(fileStorage, download);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal("FileStorage returned an invalid template download grant.", exception.Message);
        Assert.Equal(0, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_redirect_is_rejected_without_following_or_retrying()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var download = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://redirect.invalid/template.json") }
        });
        using var adapter = CreateAdapter(fileStorage, download);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(1, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_managed_handler_does_not_follow_redirect_on_loopback()
    {
        await TestTimeout.RunAsync(
            "FileStorage redirect loopback including server cleanup",
            async testCancellationToken =>
            {
                var bytes = Encoding.UTF8.GetBytes(TemplateJson);
                var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var baseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
                using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(testCancellationToken);
                var server = ServeRedirectAndPossibleFollowAsync(listener, bytes, serverCancellation.Token);
                using var adapter = CreateAdapter(
                    fileStorage,
                    new SocketsHttpHandler
                    {
                        AllowAutoRedirect = false,
                        ConnectTimeout = TimeSpan.FromSeconds(2),
                    },
                    TimeSpan.FromSeconds(2),
                    baseAddress);

                var acceptedRequests = 0;
                try
                {
                    await Assert.ThrowsAsync<InvalidDataException>(() =>
                        adapter.GetVerifiedAsync(CreateReference(), testCancellationToken));
                }
                finally
                {
                    serverCancellation.Cancel();
                    acceptedRequests = await server.WaitAsync(testCancellationToken);
                }

                Assert.Equal(1, acceptedRequests);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetVerifiedAsync_non_success_response_is_rejected_without_retrying()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var download = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.ServiceUnavailable, bytes));
        using var adapter = CreateAdapter(fileStorage, download);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal(1, fileStorage.GrantCalls);
        Assert.Equal(1, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_download_timeout_is_rejected_without_retrying()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var download = new RecordingHttpMessageHandler(async (_, cancellationToken) =>
        {
            await PendingOperation.UntilCanceledAsync(cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        using var adapter = CreateAdapter(fileStorage, download, TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal("FileStorage template download timed out.", exception.Message);
        Assert.Equal(1, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_caller_cancellation_is_propagated()
    {
        var bytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(bytes));
        var download = new RecordingHttpMessageHandler(async (_, cancellationToken) =>
        {
            await PendingOperation.UntilCanceledAsync(cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        using var adapter = CreateAdapter(fileStorage, download, TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), cancellation.Token));

        Assert.Equal(1, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_content_length_above_limit_is_rejected_before_reading()
    {
        var metadataBytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(metadataBytes));
        var content = new ThrowOnReadContent(MaximumAssetBytes + 1L);
        var download = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, content));
        using var adapter = CreateAdapter(fileStorage, download);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.False(content.WasRead);
    }

    [Fact]
    public async Task GetVerifiedAsync_unknown_length_body_above_limit_is_rejected()
    {
        var metadataBytes = Encoding.UTF8.GetBytes(TemplateJson);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(metadataBytes));
        var overflow = Enumerable.Repeat((byte)'a', MaximumAssetBytes + 1).ToArray();
        var download = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, new UnknownLengthContent(overflow)));
        using var adapter = CreateAdapter(fileStorage, download);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None));

        Assert.Equal($"FileStorage template download exceeds {MaximumAssetBytes} bytes.", exception.Message);
        Assert.Equal(1, download.Calls);
    }

    [Fact]
    public async Task GetVerifiedAsync_body_at_exact_limit_is_accepted()
    {
        var template = Encoding.UTF8.GetBytes(TemplateJson);
        var body = new byte[MaximumAssetBytes];
        template.CopyTo(body, 0);
        Array.Fill(body, (byte)' ', template.Length, body.Length - template.Length);
        var fileStorage = new RecordingFileStorageClient(CreateMetadata(body));
        var download = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, body));
        using var adapter = CreateAdapter(fileStorage, download);

        var asset = await adapter.GetVerifiedAsync(CreateReference(), CancellationToken.None);

        Assert.Equal(MaximumAssetBytes, Encoding.UTF8.GetByteCount(asset.Json));
    }

    [Fact]
    public async Task GetVerifiedAsync_empty_body_is_rejected()
    {
        var metadata = CreateMetadata(Encoding.UTF8.GetBytes(TemplateJson)) with { SizeBytes = 1 };
        await AssertRejectedBodyAsync(metadata, []);
    }

    [Fact]
    public async Task GetVerifiedAsync_utf8_bom_is_rejected_after_size_and_digest_match()
    {
        var body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(TemplateJson)).ToArray();
        await AssertRejectedBodyAsync(CreateMetadata(body), body);
    }

    [Fact]
    public async Task GetVerifiedAsync_invalid_utf8_is_rejected_after_size_and_digest_match()
    {
        byte[] body = [0xc3, 0x28];
        await AssertRejectedBodyAsync(CreateMetadata(body), body);
    }

    [Fact]
    public async Task GetVerifiedAsync_size_mismatch_is_rejected()
    {
        var body = Encoding.UTF8.GetBytes(TemplateJson);
        await AssertRejectedBodyAsync(CreateMetadata(body) with { SizeBytes = body.Length - 1 }, body);
    }

    [Fact]
    public async Task GetVerifiedAsync_checksum_mismatch_is_rejected()
    {
        var body = Encoding.UTF8.GetBytes(TemplateJson);
        var downloaded = body.ToArray();
        downloaded[^1] ^= 1;
        await AssertRejectedBodyAsync(CreateMetadata(body), downloaded);
    }

    private static async Task AssertRejectedBodyAsync(FileMetadataResponse metadata, byte[] body)
    {
        var fileStorage = new RecordingFileStorageClient(metadata);
        var download = new RecordingHttpMessageHandler(_ => Response(HttpStatusCode.OK, body));
        using var adapter = CreateAdapter(fileStorage, download);

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
                await first.GetStream().WriteAsync(
                    "HTTP/1.1 302 Found\r\nLocation: /followed.json\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(),
                    cancellationToken);
            }

            using var second = await listener.AcceptTcpClientAsync(cancellationToken);
            accepted++;
            await ReadRequestHeadersAsync(second, cancellationToken);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {successBytes.Length}\r\nConnection: close\r\n\r\n");
            await second.GetStream().WriteAsync(headers, cancellationToken);
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

    private static HttpFileStorageLabelTemplateAssetAdapter CreateAdapter(
        RecordingFileStorageClient fileStorage,
        HttpMessageHandler handler,
        TimeSpan? timeout = null,
        Uri? baseAddress = null)
    {
        var downloadClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseAddress ?? new Uri("https://file-storage.invalid/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new HttpFileStorageLabelTemplateAssetAdapter(
            fileStorage,
            downloadClient,
            timeout ?? TimeSpan.FromSeconds(1));
    }

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
        string grantUrl = "/api/files/v1/download-grants/grant-secret/content",
        bool nullGrantHeaders = false) : IFileStorageClient
    {
        public int MetadataCalls { get; private set; }
        public int GrantCalls { get; private set; }
        public CreateDownloadGrantRequest? LastGrantRequest { get; private set; }

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
            LastGrantRequest = request;
            return grantFailure is null
                ? Task.FromResult(new DownloadGrantResponse(
                    fileId,
                    DateTimeOffset.Parse("2026-08-24T00:10:00Z"),
                    new TransferInstructions(
                        grantUrl,
                        nullGrantHeaders
                            ? null!
                            : new Dictionary<string, string>
                            {
                                ["x-nerv-organization-id"] = request.OrganizationId,
                                ["x-nerv-environment-id"] = request.EnvironmentId
                            })))
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
