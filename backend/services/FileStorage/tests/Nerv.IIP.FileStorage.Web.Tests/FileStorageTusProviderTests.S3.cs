using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    [InlineData("ReadIdleTimeoutSeconds", "0")]
    [InlineData("ReadIdleTimeoutSeconds", "3601")]
    [InlineData("ReadIdleTimeoutSeconds", "invalid")]
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

    [Fact]
    public void Startup_rejects_prefix_without_room_for_a_generated_key()
    {
        Startup_rejects_invalid_S3_configuration("KeyPrefix", new string('a', 1024));
    }

    [Theory]
    [InlineData(true, "internal-storage.test:9443", "/files-bucket/production/files/org/a%20b%2B%E4%B8%AD%3F%23",
        "973151c3134363497cf04705c1c0b67a1f010564494358b737afcdfaa9d9bbeb")]
    [InlineData(false, "files-bucket.internal-storage.test:9443", "/production/files/org/a%20b%2B%E4%B8%AD%3F%23",
        "b27925c13d7fdde262fd0911fe03993eaa0885939f88bc10b31905e617094179")]
    public async Task S3_signs_escaped_keys_and_port_in_both_addressing_styles(
        bool pathStyle, string expectedHost, string expectedPath, string expectedSignature)
    {
        // 固定向量由 AWS SDK for Python 的 botocore.auth.S3SigV4Auth 计算。
        using var s3 = new TestS3Handler();
        await using var factory = new FileStorageWebApplicationFactory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-27T02:03:04Z"));
        await using var host = ConfigureStorage(factory, "s3", s3, clock: clock, change: values =>
        {
            values["FileStorage:S3:Endpoint"] = "https://internal-storage.test:9443";
            values["FileStorage:S3:ForcePathStyle"] = pathStyle.ToString();
        });

        var store = host.Services.GetRequiredService<S3FinalFileStore>();
        Assert.Null(await store.TryGetAsync("org/a b+中?#", CancellationToken.None));
        Assert.Equal(expectedHost, s3.LastHost);
        Assert.Equal(expectedPath, s3.LastPath);
        Assert.Equal("AWS4-HMAC-SHA256 Credential=test-access-key/20261127/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;x-amz-content-sha256;x-amz-date, Signature=" + expectedSignature, s3.LastAuthorization);
    }

    [Theory]
    [InlineData(5368709120L, true)]
    [InlineData(5368709121L, false)]
    public async Task S3_upload_session_enforces_single_put_size_limit(long bytes, bool allowed)
    {
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = ConfigureStorage(factory, "s3");
        using var client = CreateInternalServiceClient(host);
        using var response = await client.PostAsJsonAsync("/api/files/v1/upload-sessions",
            CreateTextAttachmentRequest() with { ExpectedSizeBytes = bytes });
        if (allowed)
        {
            response.EnsureSuccessStatusCode();
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("超出 S3 存储支持范围", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("healthy", "0", true, HttpStatusCode.OK, "Healthy")]
    [InlineData("healthy", "0", false, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    [InlineData("missing-marker", "0", true, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    [InlineData("healthy", "9223372036854775807", true, HttpStatusCode.OK, "Degraded")]
    [InlineData("healthy", "9223372036854775807", false, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task S3_readiness_combines_staging_volume_and_bucket(
        string volume, string minimumFreeBytes, bool bucketAvailable, HttpStatusCode status, string body)
    {
        var root = CreateTempDirectory();
        try
        {
            var marker = Path.Combine(root, ".filestorage-volume-v1");
            await File.WriteAllTextAsync(marker, "s3-staging-volume");
            using var s3 = new TestS3Handler { BucketAvailable = bucketAvailable };
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", s3, root, change: values =>
            {
                values["FileStorage:Tus:VolumeIdentity"] = "s3-staging-volume";
                values["FileStorage:Tus:MinimumFreeBytes"] = minimumFreeBytes;
            });
            using var client = CreateInternalServiceClient(host);
            if (volume == "missing-marker")
            {
                File.Delete(marker);
            }

            using var ready = await client.GetAsync("/health/tus-volume");
            Assert.Equal(status, ready.StatusCode);
            Assert.Equal(body, await ready.Content.ReadAsStringAsync());
            Assert.Equal("/files-bucket", s3.LastHead!.AbsolutePath);

            using var live = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());

            using var reconcile = await client.GetAsync("/internal/file-storage/v1/diagnostics/reconcile");
            Assert.Equal(HttpStatusCode.Conflict, reconcile.StatusCode);
            Assert.Contains("unavailable in S3 mode", await reconcile.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteTempDirectory(root);
        }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S3_commit_after_precondition_failure_requires_matching_readback(bool conflict)
    {
        var root = CreateTempDirectory();
        try
        {
            using var s3 = new TestS3Handler { ConcurrentPut = true, CorruptPut = conflict };
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", s3, root);
            using var client = CreateInternalServiceClient(host);
            var bytes = "hello"u8.ToArray();
            var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest());
            using var request = CreateTusPatchRequest(upload.Upload.Url, 0, bytes);
            using var response = await client.SendAsync(request);

            Assert.Equal([HttpMethod.Get, HttpMethod.Put, HttpMethod.Get], s3.Requests);
            var file = await ReadFileAsync(host, upload.FileId);
            if (conflict)
            {
                Assert.False(response.IsSuccessStatusCode);
                Assert.Null(file);
                Assert.True(CreateTusStore(root).Exists(upload.UploadSessionId));
            }
            else
            {
                response.EnsureSuccessStatusCode();
                Assert.Equal("sha256:2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", file!.Checksum);
                Assert.False(CreateTusStore(root).Exists(upload.UploadSessionId));
            }
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
    [InlineData("deleted")]
    [InlineData("missing")]
    [InlineData("forbidden")]
    [InlineData("stalled")]
    public async Task S3_GC_removes_metadata_only_after_confirmed_remote_delete(string outcome)
    {
        using var stalled = new StalledS3Stream();
        using var s3 = new TestS3Handler
        {
            DeleteStatus = outcome switch
            {
                "missing" => HttpStatusCode.NotFound,
                "forbidden" => HttpStatusCode.Forbidden,
                "stalled" => HttpStatusCode.OK,
                _ => HttpStatusCode.NoContent
            },
            StalledDelete = outcome == "stalled" ? stalled : null
        };
        var clock = new FakeTimeProvider();
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = ConfigureStorage(factory, "s3", s3, clock: clock);
        const string key = "/files-bucket/production/files/org-001/file_expired";
        if (outcome != "missing")
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

        var collector = scope.ServiceProvider.GetRequiredService<PostgreSqlFileStorageGarbageCollector>();
        if (outcome is "forbidden" or "stalled")
        {
            if (outcome == "stalled")
            {
                await Assert.ThrowsAsync<IOException>(() =>
                    ExpireStalledReadAsync(stalled, clock, collector.CollectAsync));
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => collector.CollectAsync(CancellationToken.None));
                Assert.True(s3.Objects.ContainsKey(key));
            }
            Assert.Equal(key, s3.LastDeletedPath);
            Assert.NotNull(await ReadFileAsync(host, "file_expired"));

            s3.DeleteStatus = HttpStatusCode.NotFound;
            s3.StalledDelete = null;
            using var retryScope = host.Services.CreateScope();
            var retry = await retryScope.ServiceProvider.GetRequiredService<PostgreSqlFileStorageGarbageCollector>()
                .CollectAsync(CancellationToken.None);
            Assert.Equal(1, retry.FormalFilesPhysicallyDeleted);
            Assert.Null(await ReadFileAsync(host, "file_expired"));
            return;
        }

        var result = await collector.CollectAsync(CancellationToken.None);
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

    private static async Task<HttpResponseMessage> DownloadAsync(HttpClient client, DownloadGrantResponse grant, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, grant.Download.Url);
        AddTransferHeaders(request, grant.Download.Headers);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static WebApplicationFactory<Program> ConfigureStorage(
        WebApplicationFactory<Program> factory,
        string? provider,
        TestS3Handler? handler = null,
        string? root = null,
        TimeProvider? clock = null,
        Action<Dictionary<string, string?>>? change = null,
        ILoggerProvider? logProvider = null) =>
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
                if (logProvider is not null)
                {
                    values["Logging:Serilog:Enabled"] = "false";
                }
                change?.Invoke(values);
                config.AddInMemoryCollection(values);
            });
            builder.ConfigureServices(services =>
            {
                if (logProvider is not null)
                {
                    services.AddSingleton(logProvider);
                }
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
        Assert.DoesNotContain("files-bucket", text);
        Assert.DoesNotContain("test-access-key", text);
        Assert.DoesNotContain("test-secret-key", text);
    }

    private sealed class TestS3Handler : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public bool BucketAvailable { get; set; } = true;
        public bool LosePutResponse { get; init; }
        public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.NoContent;
        public StalledS3Stream? StalledGet { get; set; }
        public StalledS3Stream? StalledDelete { get; set; }
        public bool ConcurrentPut { get; init; }
        public Action? OnGet { get; set; }
        public List<HttpMethod> Requests { get; } = [];
        public string? LastHost { get; private set; }
        public string? LastPath { get; private set; }
        public bool FailGet { get; set; }
        public bool CorruptPut { get; init; }
        public Uri? LastHead { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastDeletedPath { get; private set; }
        public int PutCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string sensitiveResponse = "internal-storage.test files-bucket test-access-key test-secret-key";
            LastAuthorization = Assert.Single(request.Headers.GetValues("Authorization"));
            Assert.StartsWith("AWS4-HMAC-SHA256 Credential=test-access-key/", LastAuthorization);
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            Requests.Add(request.Method);
            LastHost = request.Headers.Host;
            LastPath = path;
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
                if (!Objects.TryAdd(path, CorruptPut ? new byte[content.Length] : content) || ConcurrentPut)
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
                if (DeleteStatus != HttpStatusCode.Forbidden)
                {
                    Objects.Remove(path);
                }
                return StalledDelete is null ? new(DeleteStatus) : StalledResponse(StalledDelete, 1);
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            OnGet?.Invoke();
            if (FailGet)
            {
                return new(HttpStatusCode.Forbidden) { Content = new StringContent(sensitiveResponse) };
            }
            if (!Objects.TryGetValue(path, out var bytes))
            {
                return new(HttpStatusCode.NotFound) { Content = new StringContent(sensitiveResponse) };
            }
            return StalledGet is null
                ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : StalledResponse(StalledGet, bytes.Length);
        }
    }
}
