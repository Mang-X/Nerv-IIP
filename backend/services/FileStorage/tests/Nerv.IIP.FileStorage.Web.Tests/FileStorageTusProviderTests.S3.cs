using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed partial class FileStorageTusProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("local")]
    public void Local_storage_starts_without_S3_credentials(string? provider)
    {
        using var factory = new FileStorageWebApplicationFactory();
        using var host = ConfigureStorage(factory, provider);
        Assert.IsType<Application.Files.Tus.LocalTusUploadCommitStorage>(
            host.Services.GetRequiredService<IUploadCommitStorage>());
    }

    [Theory]
    [InlineData("LOCAL")]
    [InlineData("S3")]
    [InlineData(" s3")]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData("unknown")]
    public void Startup_rejects_unknown_storage_provider_values(string provider)
    {
        using var factory = new FileStorageWebApplicationFactory();
        using var host = ConfigureStorage(factory, provider);
        Assert.Contains("FileStorage:StorageProvider", Assert.Throws<InvalidOperationException>(() => host.CreateClient()).Message);
    }

    [Theory]
    [InlineData("Endpoint", null)]
    [InlineData("Endpoint", "ftp://internal-storage.test")]
    [InlineData("Endpoint", "http://user:password@internal-storage.test")]
    [InlineData("Bucket", null)]
    [InlineData("Bucket", "invalid/name")]
    [InlineData("AccessKey", null)]
    [InlineData("SecretKey", null)]
    [InlineData("SecretKey", " ")]
    [InlineData("Region", "bad/region")]
    [InlineData("ForcePathStyle", "maybe")]
    [InlineData("KeyPrefix", "data/../private")]
    [InlineData("KeyPrefix", "data/%2e%2e/private")]
    [InlineData("KeyPrefix", "data\\private")]
    [InlineData("KeyPrefix", "data/pri\nvate")]
    public void Startup_rejects_invalid_S3_configuration(string name, string? value)
    {
        using var factory = new FileStorageWebApplicationFactory();
        using var host = ConfigureStorage(factory, "s3", change: values => values[$"FileStorage:S3:{name}"] = value);
        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains($"FileStorage:S3:{name}", error.Message);
        AssertSanitized(error.Message);
    }

    [Theory]
    [InlineData(null, "internal-storage.test", "/files-bucket")]
    [InlineData("false", "files-bucket.internal-storage.test", "/")]
    public async Task S3_health_probes_bucket_without_affecting_liveness(
        string? forcePathStyle, string expectedHost, string expectedPath)
    {
        using var s3 = new TestS3Handler();
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = ConfigureStorage(factory, "s3", s3, change: values =>
        {
            values["FileStorage:S3:ForcePathStyle"] = forcePathStyle;
            values["FileStorage:S3:Region"] = forcePathStyle is null ? null : "us-west-2";
        });
        using var client = CreateInternalServiceClient(host);

        using var ready = await client.GetAsync("/health/tus-volume");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
        Assert.Equal(expectedHost, s3.LastHead!.Host);
        Assert.Equal(expectedPath, s3.LastHead.AbsolutePath);
        Assert.Contains(forcePathStyle is null ? "/us-east-1/s3/" : "/us-west-2/s3/", s3.LastAuthorization);

        s3.BucketAvailable = false;
        using var unavailable = await client.GetAsync("/health/tus-volume");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("Unhealthy", await unavailable.Content.ReadAsStringAsync());

        using var live = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());

        using var reconcile = await client.GetAsync("/internal/file-storage/v1/diagnostics/reconcile");
        Assert.Equal(HttpStatusCode.Conflict, reconcile.StatusCode);
        Assert.Contains("unavailable in S3 mode", await reconcile.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S3_completion_recovers_lost_put_response_and_downloads_once(bool losePutResponse)
    {
        var root = CreateTempDirectory();
        try
        {
            using var s3 = new TestS3Handler { LosePutResponse = losePutResponse };
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-27T02:03:04Z"));
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", s3, root, clock);
            using var client = CreateInternalServiceClient(host);
            var bytes = Encoding.UTF8.GetBytes("hello");
            const string checksum = "sha256:2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
            var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest() with { Checksum = checksum });
            var key = $"/files-bucket/production/files/org-001/{upload.FileId}";
            var staging = CreateTusStore(root);
            using var patchRequest = CreateTusPatchRequest(upload.Upload.Url, 0, bytes);
            using var patch = await client.SendAsync(patchRequest);
            if (losePutResponse)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, patch.StatusCode);
                AssertSanitized(await patch.Content.ReadAsStringAsync());
                Assert.True(staging.Exists(upload.UploadSessionId));
                Assert.Null(await ReadFileAsync(host, upload.FileId));
            }
            else
            {
                patch.EnsureSuccessStatusCode();
            }

            using var complete = await client.PostAsJsonAsync($"/api/files/v1/upload-sessions/{upload.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-001", "prod", "attachment", checksum, bytes.Length));
            complete.EnsureSuccessStatusCode();
            Assert.Equal(1, s3.PutCount);
            Assert.Equal(bytes, s3.Objects[key]);
            Assert.False(staging.Exists(upload.UploadSessionId));
            Assert.Empty(Directory.EnumerateFiles(root, "final-*"));
            Assert.Equal(checksum, (await ReadFileAsync(host, upload.FileId))!.Checksum);

            var grant = await IssueDownloadGrantAsync(client, upload.FileId);
            using (var wrongScope = new HttpRequestMessage(HttpMethod.Get, grant.Download.Url))
            {
                AddTransferHeaders(wrongScope, grant.Download.Headers);
                wrongScope.Headers.Remove(FileStorageTransferHeaders.OrganizationId);
                wrongScope.Headers.Add(FileStorageTransferHeaders.OrganizationId, "another-org");
                using var rejected = await client.SendAsync(wrongScope);
                Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
            }

            using var first = await DownloadAsync(client, grant);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal("attachment", first.Content.Headers.ContentDisposition!.DispositionType);
            Assert.True(first.Headers.CacheControl!.Private);
            Assert.True(first.Headers.CacheControl.NoStore);
            Assert.Equal("nosniff", Assert.Single(first.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal(bytes, await first.Content.ReadAsByteArrayAsync());
            using var repeated = await DownloadAsync(client, grant);
            Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);

            var missingGrant = await IssueDownloadGrantAsync(client, upload.FileId);
            s3.Objects.Remove(key);
            using var missing = await DownloadAsync(client, missingGrant);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            AssertSanitized(await missing.Content.ReadAsStringAsync());
            s3.Objects[key] = bytes;
            using var consumed = await DownloadAsync(client, missingGrant);
            Assert.Equal(HttpStatusCode.NotFound, consumed.StatusCode);

            var unavailableGrant = await IssueDownloadGrantAsync(client, upload.FileId);
            s3.FailGet = true;
            using var unavailable = await DownloadAsync(client, unavailableGrant);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            AssertSanitized(await unavailable.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S3_commit_rejects_checksum_mismatch_and_keeps_staging(bool corruptRemote)
    {
        var root = CreateTempDirectory();
        try
        {
            using var s3 = new TestS3Handler { CorruptPut = corruptRemote };
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", s3, root);
            using var client = CreateInternalServiceClient(host);
            var bytes = Encoding.UTF8.GetBytes("hello");
            var checksum = corruptRemote
                ? "sha256:2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
                : $"sha256:{new string('f', 64)}";
            var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest() with { Checksum = checksum });
            using var request = CreateTusPatchRequest(upload.Upload.Url, 0, bytes);
            using var response = await client.SendAsync(request);
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(corruptRemote ? 1 : 0, s3.PutCount);
            Assert.Null(await ReadFileAsync(host, upload.FileId));
            Assert.True(CreateTusStore(root).Exists(upload.UploadSessionId));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task S3_GC_recovers_post_commit_staging_only_after_verifying_the_object()
    {
        var root = CreateTempDirectory();
        try
        {
            using var s3 = new TestS3Handler();
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", s3, root);
            using var client = CreateInternalServiceClient(host);
            var bytes = Encoding.UTF8.GetBytes("hello");
            var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest());
            await PatchTusBytesAsync(client, upload.Upload.Url, 0, bytes);

            var key = $"/files-bucket/production/files/org-001/{upload.FileId}";
            var store = CreateTusStore(root);
            await using var staging = new MemoryStream(bytes);
            await store.AppendAsync(upload.UploadSessionId, 0, staging, CancellationToken.None);
            s3.Objects[key] = new byte[bytes.Length];

            using var scope = host.Services.CreateScope();
            var collector = scope.ServiceProvider.GetRequiredService<PostgreSqlFileStorageGarbageCollector>();
            var before = await collector.CollectAsync(CancellationToken.None);
            Assert.Equal(0, before.LocalTusFilesRemoved);
            Assert.True(store.Exists(upload.UploadSessionId));

            s3.Objects[key] = bytes;
            var after = await collector.CollectAsync(CancellationToken.None);
            Assert.Equal(1, after.LocalTusFilesRemoved);
            Assert.False(store.Exists(upload.UploadSessionId));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S3_GC_removes_expired_metadata_after_remote_delete_or_not_found(bool alreadyMissing)
    {
        using var s3 = new TestS3Handler { ReturnNotFoundOnDelete = alreadyMissing };
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = ConfigureStorage(factory, "s3", s3);
        const string key = "/files-bucket/production/files/org-001/file_expired";
        if (!alreadyMissing)
        {
            s3.Objects[key] = [1, 2, 3];
        }

        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var file = Infrastructure.Records.StoredFileRecord.Create(
            "file_expired", "org-001", "prod", "AppHub", "DiagnosticAttachment", "app-42",
            "attachment", "note.txt", "text/plain", 3, null, "org-001/file_expired", "available", now, now);
        file.MarkDeleted(now.AddMinutes(-10), "expired", TimeSpan.Zero);
        database.StoredFiles.Add(file);
        await database.SaveChangesAsync();

        var result = await scope.ServiceProvider.GetRequiredService<PostgreSqlFileStorageGarbageCollector>()
            .CollectAsync(CancellationToken.None);

        Assert.Equal(1, result.FormalFilesPhysicallyDeleted);
        Assert.False(s3.Objects.ContainsKey(key));
        Assert.Equal(key, s3.LastDeletedPath);
        Assert.Empty(await database.StoredFiles.ToListAsync());
    }

    private static async Task<Infrastructure.Records.StoredFileRecord?> ReadFileAsync(
        WebApplicationFactory<Program> host, string fileId)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().StoredFiles
            .SingleOrDefaultAsync(file => file.FileId == fileId);
    }

    private static async Task<DownloadGrantResponse> IssueDownloadGrantAsync(HttpClient client, string fileId)
    {
        using var response = await client.PostAsJsonAsync($"/api/files/v1/files/{fileId}/download-grants",
            new CreateDownloadGrantRequest("org-001", "prod"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DownloadGrantResponse>())!;
    }

    private static async Task<HttpResponseMessage> DownloadAsync(HttpClient client, DownloadGrantResponse grant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, grant.Download.Url);
        AddTransferHeaders(request, grant.Download.Headers);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static WebApplicationFactory<Program> ConfigureStorage(
        WebApplicationFactory<Program> factory,
        string? provider,
        TestS3Handler? handler = null,
        string? root = null,
        TimeProvider? clock = null,
        Action<Dictionary<string, string?>>? change = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>();
                if (provider is not null)
                {
                    values["FileStorage:StorageProvider"] = provider;
                }
                if (provider == "s3")
                {
                    values["FileStorage:S3:Endpoint"] = "https://internal-storage.test";
                    values["FileStorage:S3:Bucket"] = "files-bucket";
                    values["FileStorage:S3:AccessKey"] = "test-access-key";
                    values["FileStorage:S3:SecretKey"] = "test-secret-key";
                    values["FileStorage:S3:KeyPrefix"] = "production/files";
                }
                if (root is not null)
                {
                    values["FileStorage:Tus:RootPath"] = root;
                }
                change?.Invoke(values);
                config.AddInMemoryCollection(values);
            });
            builder.ConfigureServices(services =>
            {
                if (handler is not null)
                {
                    services.AddHttpClient(S3FinalFileStore.ClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => handler);
                }
                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }
            });
        });

    private static void AssertSanitized(string text)
    {
        Assert.DoesNotContain("internal-storage.test", text);
        Assert.DoesNotContain("test-access-key", text);
        Assert.DoesNotContain("test-secret-key", text);
    }

    private sealed class TestS3Handler : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public bool BucketAvailable { get; set; } = true;
        public bool LosePutResponse { get; init; }
        public bool ReturnNotFoundOnDelete { get; init; }
        public bool FailGet { get; set; }
        public bool CorruptPut { get; init; }
        public Uri? LastHead { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastDeletedPath { get; private set; }
        public int PutCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string sensitiveResponse = "internal-storage.test test-access-key test-secret-key";
            LastAuthorization = Assert.Single(request.Headers.GetValues("Authorization"));
            Assert.StartsWith("AWS4-HMAC-SHA256 Credential=test-access-key/", LastAuthorization);
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            if (request.Method == HttpMethod.Head)
            {
                LastHead = uri;
                return new(BucketAvailable ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
            }
            if (request.Method == HttpMethod.Put)
            {
                PutCount++;
                Assert.Equal("*", Assert.Single(request.Headers.IfNoneMatch).Tag);
                var content = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)),
                    Assert.Single(request.Headers.GetValues("x-amz-content-sha256")));
                if (!Objects.TryAdd(path, CorruptPut ? new byte[content.Length] : content))
                {
                    return new(HttpStatusCode.PreconditionFailed);
                }
                if (LosePutResponse)
                {
                    throw new HttpRequestException(sensitiveResponse);
                }
                return new(HttpStatusCode.OK);
            }
            if (request.Method == HttpMethod.Delete)
            {
                LastDeletedPath = path;
                Objects.Remove(path);
                return new(ReturnNotFoundOnDelete ? HttpStatusCode.NotFound : HttpStatusCode.NoContent);
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            if (FailGet)
            {
                return new(HttpStatusCode.Forbidden) { Content = new StringContent(sensitiveResponse) };
            }
            return Objects.TryGetValue(path, out var bytes)
                ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new(HttpStatusCode.NotFound) { Content = new StringContent(sensitiveResponse) };
        }
    }
}
