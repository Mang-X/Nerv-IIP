using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

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
        var client = factory.CreateClient();

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
    public async Task TusUploadEndpoint_HeadAndPatch_TracksOffset()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = factory.CreateClient();
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
            var client = factory.CreateClient();
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
            var client = factory.CreateClient();
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
    public async Task TusUploadEndpoint_ServerProxySession_ReturnsNotFound()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var createdResponse = await client.PostAsJsonAsync("/api/files/v1/upload-sessions", CreateUploadRequest());
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateUploadSessionResponse>();
        Assert.NotNull(created);
        Assert.Equal("server-proxy", created.Provider);

        var response = await SendTusHeadAsync(client, $"/api/files/v1/tus/{created.UploadSessionId}");

        Assert.Equal(StatusCodes.Status404NotFound, (int)response.StatusCode);
    }

    [Fact]
    public async Task TusUploadEndpoint_CompleteAndDownload_ReturnsUploadedBytes()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(rootPath);
            var client = factory.CreateClient();
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

            var downloadResponse = await client.GetAsync(grant.Download.Url);

            downloadResponse.EnsureSuccessStatusCode();
            Assert.Equal(uploadedBytes, await downloadResponse.Content.ReadAsByteArrayAsync());
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
            var client = factory.CreateClient();
            var created = await CreateTusUploadSessionAsync(client);

            var completeResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-001", "prod", "application-package", null, 0));
            completeResponse.EnsureSuccessStatusCode();

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
            var client = factory.CreateClient();

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

        var result = service.CreateUploadSession(CreateUploadRequest());

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("tus", result.Value.Provider);
        Assert.Equal("tus", result.Value.UploadMode);
        Assert.Equal($"/api/files/v1/tus/{result.Value.UploadSessionId}", result.Value.Upload.Url);
        var record = await dbContext.UploadSessions.SingleAsync();
        Assert.Equal("tus", record.Provider);
        AssertObjectKeyIsNotExposed(result.Value);
    }

    private static WebApplicationFactory<Program> CreateFactoryWithTusProvider(string? rootPath = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["FileStorage:UploadProvider"] = "tus",
                        ["FileStorage:Tus:RootPath"] = rootPath
                    });
                });
            });
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
        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new ByteArrayContent(bytes)
        };
        patchRequest.Headers.Add("Tus-Resumable", "1.0.0");
        patchRequest.Headers.Add("Upload-Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        patchRequest.Content.Headers.ContentType = new("application/offset+octet-stream");
        var response = await client.SendAsync(patchRequest);
        response.EnsureSuccessStatusCode();
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
}
