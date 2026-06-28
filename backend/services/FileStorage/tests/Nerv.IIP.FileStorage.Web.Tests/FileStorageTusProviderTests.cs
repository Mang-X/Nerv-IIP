using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageTusProviderTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TusUploadProvider_CreateUploadInstructions_ReturnsTusShapeWithoutObjectKey()
    {
        IFileStorageUploadProvider provider = new TusUploadProvider();

        var upload = provider.CreateUploadInstructions("ups_123", "file_456");

        Assert.Equal("tus", provider.Provider);
        Assert.Equal("tus", provider.UploadMode);
        Assert.Equal("/api/files/v1/tus/ups_123", upload.Url);
        Assert.Equal("tus", upload.Headers["x-nerv-upload-mode"]);
        Assert.DoesNotContain(upload.Headers, header => header.Key.Contains("object", StringComparison.OrdinalIgnoreCase));
        AssertObjectKeyIsNotExposed(upload);
    }

    [Fact]
    public async Task CreateUploadSession_WithTusConfiguration_ReturnsTusUploadInstructions()
    {
        await using var factory = CreateFactoryWithTusProvider();
        var client = CreateInternalServiceClient(factory);

        var response = await client.PostAsJsonAsync("/api/files/v1/upload-sessions", CreateUploadRequest());

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateUploadSessionResponse>();
        Assert.NotNull(created);
        Assert.Equal("tus", created.Provider);
        Assert.Equal("tus", created.UploadMode);
        Assert.Equal($"/api/files/v1/tus/{created.UploadSessionId}", created.Upload.Url);
        Assert.Equal("tus", created.Upload.Headers["x-nerv-upload-mode"]);
        Assert.DoesNotContain(created.Upload.Headers, header => header.Key.Contains("object", StringComparison.OrdinalIgnoreCase));
        AssertObjectKeyIsNotExposed(created);
    }

    [Fact]
    public async Task CompleteUploadSession_TusStoreUnavailable_ReturnsServiceUnavailable()
    {
        var service = new InMemoryFileStorageService(new TusUploadProvider());
        var created = (await service.CreateUploadSessionAsync(CreateUploadRequest(), CancellationToken.None)).Value!;

        var result = await service.CompleteUploadSessionAsync(
            created.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", null, 4096),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Null(result.Value);
        Assert.Equal("Tus upload store is unavailable.", result.Error?.Message);
    }

    [Fact]
    public async Task TusUploadEndpoint_HeadAndPatch_TracksOffset()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client);

            var headBefore = await SendTusHeadAsync(client, created.Upload.Url);

            headBefore.EnsureSuccessStatusCode();
            Assert.Equal(0, GetUploadOffset(headBefore));

            using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, created.Upload.Url)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"))
            };
            patchRequest.Headers.Add("Tus-Resumable", "1.0.0");
            patchRequest.Headers.Add("Upload-Offset", "0");
            patchRequest.Content.Headers.ContentType = new("application/offset+octet-stream");

            var patchResponse = await client.SendAsync(patchRequest);

            Assert.Equal(StatusCodes.Status204NoContent, (int)patchResponse.StatusCode);
            Assert.Equal(5, GetUploadOffset(patchResponse));
            var headAfter = await SendTusHeadAsync(client, created.Upload.Url);
            Assert.Equal(5, GetUploadOffset(headAfter));
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_MissingTusResumableHeader_ReturnsPreconditionFailed()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client);
            using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, created.Upload.Url)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"))
            };
            patchRequest.Headers.Add("Upload-Offset", "0");
            patchRequest.Content.Headers.ContentType = new("application/offset+octet-stream");

            var response = await client.SendAsync(patchRequest);

            Assert.Equal(StatusCodes.Status412PreconditionFailed, (int)response.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_InvalidContentType_ReturnsUnsupportedMediaType()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client);
            using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, created.Upload.Url)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"))
            };
            patchRequest.Headers.Add("Tus-Resumable", "1.0.0");
            patchRequest.Headers.Add("Upload-Offset", "0");
            patchRequest.Content.Headers.ContentType = new("application/octet-stream");

            var response = await client.SendAsync(patchRequest);

            Assert.Equal(StatusCodes.Status415UnsupportedMediaType, (int)response.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_OversizedPatch_ReturnsPayloadTooLargeWithoutAdvancingOffset()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: 5);
            using var patchRequest = CreateTusPatchRequest(created.Upload.Url, offset: 0, Encoding.UTF8.GetBytes("toolarge"));

            var response = await client.SendAsync(patchRequest);

            Assert.Equal(StatusCodes.Status413PayloadTooLarge, (int)response.StatusCode);
            var headAfter = await SendTusHeadAsync(client, created.Upload.Url);
            Assert.Equal(0, GetUploadOffset(headAfter));
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_InvalidOffset_ReturnsConflictWithCurrentOffset()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: 10);
            await PatchTusBytesAsync(client, created.Upload.Url, offset: 0, Encoding.UTF8.GetBytes("hello"));
            using var patchRequest = CreateTusPatchRequest(created.Upload.Url, offset: 0, Encoding.UTF8.GetBytes("!"));

            var response = await client.SendAsync(patchRequest);

            Assert.Equal(StatusCodes.Status409Conflict, (int)response.StatusCode);
            Assert.Equal(5, GetUploadOffset(response));
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_ChecksumMismatch_ReturnsChecksumMismatchWithoutWritingBytes()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var bytes = Encoding.UTF8.GetBytes("hello");
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: bytes.Length);
            using var patchRequest = CreateTusPatchRequest(created.Upload.Url, offset: 0, bytes);
            patchRequest.Headers.Add("Upload-Checksum", $"sha256 {Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("other")))}");

            var response = await client.SendAsync(patchRequest);

            Assert.Equal(460, (int)response.StatusCode);
            var headAfter = await SendTusHeadAsync(client, created.Upload.Url);
            Assert.Equal(0, GetUploadOffset(headAfter));
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_ChecksumMatch_AppendsBytes()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var bytes = Encoding.UTF8.GetBytes("hello");
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: bytes.Length);
            using var patchRequest = CreateTusPatchRequest(created.Upload.Url, offset: 0, bytes);
            patchRequest.Headers.Add("Upload-Checksum", $"sha256 {Convert.ToBase64String(SHA256.HashData(bytes))}");

            var response = await client.SendAsync(patchRequest);

            Assert.Equal(StatusCodes.Status204NoContent, (int)response.StatusCode);
            Assert.Equal(bytes.Length, GetUploadOffset(response));
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_ExpiredIncompleteUpload_ReturnsNotFoundAndCleansLocalBytes()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath, uploadSessionTtlSeconds: 2);
            var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: 5);
            await PatchTusBytesAsync(client, created.Upload.Url, offset: 0, Encoding.UTF8.GetBytes("hello"));
            await Task.Delay(TimeSpan.FromMilliseconds(2500));

            var response = await SendTusHeadAsync(client, created.Upload.Url);

            Assert.Equal(StatusCodes.Status404NotFound, (int)response.StatusCode);
            Assert.Empty(Directory.EnumerateFiles(rootPath));
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_ServerProxySession_ReturnsNotFound()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = CreateInternalServiceClient(factory);
        var createdResponse = await client.PostAsJsonAsync("/api/files/v1/upload-sessions", CreateUploadRequest());
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateUploadSessionResponse>();
        Assert.NotNull(created);
        Assert.Equal("server-proxy", created.Provider);

        var response = await SendTusHeadAsync(client, $"/api/files/v1/tus/{created.UploadSessionId}");

        Assert.Equal(StatusCodes.Status404NotFound, (int)response.StatusCode);
    }

    [Fact]
    public async Task TusUploadEndpoint_CompleteAndDownload_PendingScanStatusReturnsNotFound()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var uploadedBytes = Encoding.UTF8.GetBytes("hello");
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: uploadedBytes.Length);
            await PatchTusBytesAsync(client, created.Upload.Url, offset: 0, uploadedBytes);

            var completeResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-001", "prod", "application-package", null, uploadedBytes.Length));
            completeResponse.EnsureSuccessStatusCode();

            var grantResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/files/{created.FileId}/download-grants",
                new CreateDownloadGrantRequest("org-001", "prod"));
            grantResponse.EnsureSuccessStatusCode();
            var grant = await grantResponse.Content.ReadFromJsonAsync<DownloadGrantResponse>();
            Assert.NotNull(grant);

            using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, grant.Download.Url);
            AddTransferHeaders(downloadRequest, grant.Download.Headers);
            var downloadResponse = await client.SendAsync(downloadRequest);

            Assert.Equal(StatusCodes.Status404NotFound, (int)downloadResponse.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task DownloadGrantContentEndpoint_CleanGrantWithTenantHeaders_ReturnsUploadedBytesOnce()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            var uploadedBytes = Encoding.UTF8.GetBytes("hello");
            var store = CreateTusStore(rootPath);
            await using (var stream = new MemoryStream(uploadedBytes))
            {
                await store.AppendAsync("ups_clean", 0, stream, CancellationToken.None);
            }

            var fileStorage = new CleanDownloadGrantFileStorageService("dgr_clean", "ups_clean");
            await using var factory = CreateFactoryWithTusProvider(rootPath, fileStorageService: fileStorage);
            var client = CreateInternalServiceClient(factory);
            using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/api/files/v1/download-grants/dgr_clean/content");
            firstRequest.Headers.Add("X-Organization-Id", "org-001");
            firstRequest.Headers.Add("X-Environment-Id", "prod");

            var first = await client.SendAsync(firstRequest);

            first.EnsureSuccessStatusCode();
            Assert.Equal(uploadedBytes, await first.Content.ReadAsByteArrayAsync());

            using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/api/files/v1/download-grants/dgr_clean/content");
            secondRequest.Headers.Add("X-Organization-Id", "org-001");
            secondRequest.Headers.Add("X-Environment-Id", "prod");
            var second = await client.SendAsync(secondRequest);

            Assert.Equal(StatusCodes.Status404NotFound, (int)second.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_CompleteBeforeExpectedSize_ReturnsBadRequest()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: 5);
            await PatchTusBytesAsync(client, created.Upload.Url, offset: 0, Encoding.UTF8.GetBytes("he"));

            var completeResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-001", "prod", "application-package", null, 5));

            Assert.Equal(StatusCodes.Status400BadRequest, (int)completeResponse.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_CompleteWithChecksumMismatch_ReturnsBadRequest()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var bytes = Encoding.UTF8.GetBytes("hello");
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: bytes.Length);
            await PatchTusBytesAsync(client, created.Upload.Url, offset: 0, bytes);

            var completeResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-001", "prod", "application-package", "sha256:bad", bytes.Length));

            Assert.Equal(StatusCodes.Status400BadRequest, (int)completeResponse.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task DownloadGrantContentEndpoint_MissingLocalBytes_ReturnsNotFound()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);
            var uploadedBytes = Encoding.UTF8.GetBytes("hello");
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: uploadedBytes.Length);
            await PatchTusBytesAsync(client, created.Upload.Url, offset: 0, uploadedBytes);

            var completeResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-001", "prod", "application-package", null, uploadedBytes.Length));
            completeResponse.EnsureSuccessStatusCode();
            foreach (var path in Directory.EnumerateFiles(rootPath))
            {
                File.Delete(path);
            }

            var grantResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/files/{created.FileId}/download-grants",
                new CreateDownloadGrantRequest("org-001", "prod"));
            grantResponse.EnsureSuccessStatusCode();
            var grant = await grantResponse.Content.ReadFromJsonAsync<DownloadGrantResponse>();
            Assert.NotNull(grant);

            var downloadResponse = await client.GetAsync(grant.Download.Url);

            Assert.Equal(StatusCodes.Status404NotFound, (int)downloadResponse.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task TusUploadEndpoint_UnknownUploadSession_ReturnsNotFound()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = CreateInternalServiceClient(factory);

            var response = await SendTusHeadAsync(client, "/api/files/v1/tus/ups_missing");

            Assert.Equal(StatusCodes.Status404NotFound, (int)response.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task PostgreSqlCreateUploadSession_WithTusProvider_PersistsTusProvider()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext, new TusUploadProvider());

        var result = await service.CreateUploadSessionAsync(CreateUploadRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("tus", result.Value.Provider);
        Assert.Equal("tus", result.Value.UploadMode);
        Assert.Equal($"/api/files/v1/tus/{result.Value.UploadSessionId}", result.Value.Upload.Url);
        var record = await dbContext.UploadSessions.SingleAsync();
        Assert.Equal("tus", record.Provider);
        AssertObjectKeyIsNotExposed(result.Value);
    }

    private static WebApplicationFactory<Program> CreateFactoryWithTusProvider(
        string? rootPath = null,
        double? uploadSessionTtlSeconds = null,
        IFileStorageService? fileStorageService = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["FileStorage:UploadProvider"] = "tus",
                        ["FileStorage:Tus:RootPath"] = rootPath,
                        ["FileStorage:UploadSessionTtlSeconds"] = uploadSessionTtlSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
                });
                if (fileStorageService is not null)
                {
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<IFileStorageService>();
                        services.AddSingleton(fileStorageService);
                    });
                }
            });
    }

    private static HttpClient CreateInternalServiceClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        return client;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"filestorage-{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static CreateUploadSessionRequest CreateUploadRequest()
    {
        return new CreateUploadSessionRequest(
            "org-001",
            "prod",
            new OwnerReference("AppHub", "ApplicationPackage", "app-42"),
            "application-package",
            "demo.zip",
            "application/zip",
            4096,
            "sha256:test");
    }

    private static async Task<CreateUploadSessionResponse> CreateTusUploadSessionAsync(HttpClient client, long expectedSizeBytes = 4096)
    {
        var response = await client.PostAsJsonAsync(
            "/api/files/v1/upload-sessions",
            CreateUploadRequest() with { ExpectedSizeBytes = expectedSizeBytes, Checksum = null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateUploadSessionResponse>())!;
    }

    private static async Task PatchTusBytesAsync(HttpClient client, string url, long offset, byte[] bytes)
    {
        using var patchRequest = CreateTusPatchRequest(url, offset, bytes);
        var response = await client.SendAsync(patchRequest);
        response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage CreateTusPatchRequest(string url, long offset, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Content.Headers.ContentType = new("application/offset+octet-stream");
        return request;
    }

    private static void AddTransferHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static LocalTusFileStore CreateTusStore(string rootPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Tus:RootPath"] = rootPath
            })
            .Build();
        return new LocalTusFileStore(configuration);
    }

    private static Task<HttpResponseMessage> SendTusHeadAsync(HttpClient client, string url)
    {
        return client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
    }

    private static long GetUploadOffset(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Upload-Offset", out var values));
        return long.Parse(values.Single(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nerv-filestorage-tus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void AssertObjectKeyIsNotExposed<T>(T response)
    {
        var json = JsonSerializer.Serialize(response, WebJsonOptions);
        Assert.DoesNotContain("objectKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("object_key", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CleanDownloadGrantFileStorageService(string grantId, string uploadSessionId)
        : IFileStorageService, ILocalFileContentIndex
    {
        private bool consumed;

        public Task<FileStorageResult<CreateUploadSessionResponse>> CreateUploadSessionAsync(
            CreateUploadSessionRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileStorageResult<FileMetadataResponse>> CompleteUploadSessionAsync(
            string uploadSessionId,
            CompleteUploadSessionRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileStorageResult<FileMetadataResponse>> GetFileMetadataAsync(
            string fileId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileStorageResult<FileListResponse>> ListFilesAsync(
            ListFilesRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileStorageResult<DownloadGrantResponse>> CreateDownloadGrantAsync(
            string fileId,
            CreateDownloadGrantRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> GetUploadSessionIdForDownloadGrantAsync(
            string downloadGrantId,
            string organizationId,
            string environmentId,
            CancellationToken cancellationToken)
        {
            if (consumed
                || !string.Equals(downloadGrantId, grantId, StringComparison.Ordinal)
                || !string.Equals(organizationId, "org-001", StringComparison.Ordinal)
                || !string.Equals(environmentId, "prod", StringComparison.Ordinal))
            {
                return Task.FromResult<string?>(null);
            }

            consumed = true;
            return Task.FromResult<string?>(uploadSessionId);
        }
    }
}
