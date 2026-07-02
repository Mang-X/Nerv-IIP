using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.Testing;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStoragePostgreSqlServiceTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateUploadSession_PersistsUploadSessionRecord()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);

        var result = await service.CreateUploadSessionAsync(CreateUploadRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Empty(GuidVersionAssertions.Version7GuidSuffixFailures(result.Value.FileId, "file_"));
        var record = await dbContext.UploadSessions.SingleAsync();
        Assert.Equal(result.Value.UploadSessionId, record.UploadSessionId);
        Assert.Equal(result.Value.FileId, record.FileId);
        Assert.Equal("org-001", record.OrganizationId);
        Assert.Equal("prod", record.EnvironmentId);
        Assert.Equal("AppHub", record.OwnerService);
        Assert.Equal("ApplicationPackage", record.OwnerType);
        Assert.Equal("app-42", record.OwnerId);
        Assert.Equal("application-package", record.FilePurpose);
        Assert.Equal("demo.zip", record.FileName);
        Assert.Equal("application/zip", record.ContentType);
        Assert.Equal(4096, record.ExpectedSizeBytes);
        Assert.Equal("sha256:test", record.Checksum);
        Assert.Equal("server-proxy", record.Provider);
        Assert.False(record.Completed);
        Assert.Null(record.CompletedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(record.ObjectKey));
        AssertObjectKeyIsNotExposed(result.Value);
    }

    [Fact]
    public async Task CreateUploadSession_OverLengthInput_ReturnsBadRequestWithoutPersisting()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        var request = CreateUploadRequest() with
        {
            ContentType = new string('a', 257)
        };

        var result = await service.CreateUploadSessionAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Empty(await dbContext.UploadSessions.ToListAsync());
    }

    [Fact]
    public async Task CompleteUploadSession_MarksSessionCompletedAndInsertsStoredFileRecord()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        var created = (await service.CreateUploadSessionAsync(CreateUploadRequest(), CancellationToken.None)).Value!;

        var result = await service.CompleteUploadSessionAsync(
            created.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", "sha256:test", 4096),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        var session = await dbContext.UploadSessions.SingleAsync();
        Assert.True(session.Completed);
        Assert.NotNull(session.CompletedAtUtc);
        var storedFile = await dbContext.StoredFiles.SingleAsync();
        Assert.Equal(created.FileId, storedFile.FileId);
        Assert.Equal("org-001", storedFile.OrganizationId);
        Assert.Equal("prod", storedFile.EnvironmentId);
        Assert.Equal("AppHub", storedFile.OwnerService);
        Assert.Equal("ApplicationPackage", storedFile.OwnerType);
        Assert.Equal("app-42", storedFile.OwnerId);
        Assert.Equal("application-package", storedFile.FilePurpose);
        Assert.Equal("demo.zip", storedFile.FileName);
        Assert.Equal("application/zip", storedFile.ContentType);
        Assert.Equal(4096, storedFile.SizeBytes);
        Assert.Equal("sha256:test", storedFile.Checksum);
        Assert.Equal(session.ObjectKey, storedFile.ObjectKey);
        Assert.Equal("clean", storedFile.ScanStatus);
        Assert.Equal("available", storedFile.Status);
        AssertObjectKeyIsNotExposed(result.Value);
    }

    [Fact]
    public async Task GetFileMetadata_ReadsStoredFileRecord()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        dbContext.StoredFiles.Add(StoredFileRecord.Create(
            "file_123",
            "org-001",
            "prod",
            "AppHub",
            "ApplicationPackage",
            "app-42",
            "application-package",
            "demo.zip",
            "application/zip",
            4096,
            "sha256:test",
            "org-001/file_123",
            "pending",
            "available",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var result = await service.GetFileMetadataAsync("file_123", CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("file_123", result.Value.FileId);
        Assert.Equal("org-001", result.Value.OrganizationId);
        Assert.Equal("prod", result.Value.EnvironmentId);
        Assert.Equal("AppHub", result.Value.Owner.OwnerService);
        Assert.Equal("ApplicationPackage", result.Value.Owner.OwnerType);
        Assert.Equal("app-42", result.Value.Owner.OwnerId);
        Assert.Equal("application-package", result.Value.FilePurpose);
        Assert.Equal("demo.zip", result.Value.FileName);
        Assert.Equal("application/zip", result.Value.ContentType);
        Assert.Equal(4096, result.Value.SizeBytes);
        Assert.Equal("sha256:test", result.Value.Checksum);
        Assert.Equal("pending", result.Value.ScanStatus);
        Assert.Equal("available", result.Value.Status);
        AssertObjectKeyIsNotExposed(result.Value);
    }

    [Fact]
    public async Task ListFiles_FiltersByPurposeUploaderTimeAndStatusAndReturnsTotal()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        var now = DateTimeOffset.UtcNow;
        dbContext.StoredFiles.AddRange(
            StoredFileRecord.Create(
                "file_old",
                "org-001",
                "prod",
                "Notification",
                "Message",
                "user-001",
                "notification-attachment",
                "old.csv",
                "text/csv",
                10,
                null,
                "org-001/file_old",
                "pending",
                "available",
                now.AddHours(-4),
                now.AddHours(-4)),
            StoredFileRecord.Create(
                "file_match_1",
                "org-001",
                "prod",
                "Notification",
                "Message",
                "user-001",
                "notification-attachment",
                "first.csv",
                "text/csv",
                11,
                null,
                "org-001/file_match_1",
                "pending",
                "available",
                now.AddHours(-2),
                now.AddHours(-2)),
            StoredFileRecord.Create(
                "file_match_2",
                "org-001",
                "prod",
                "Notification",
                "Message",
                "user-001",
                "notification-attachment",
                "second.csv",
                "text/csv",
                12,
                null,
                "org-001/file_match_2",
                "pending",
                "available",
                now.AddHours(-1),
                now.AddHours(-1)),
            StoredFileRecord.Create(
                "file_different_uploader",
                "org-001",
                "prod",
                "Notification",
                "Message",
                "user-002",
                "notification-attachment",
                "other.csv",
                "text/csv",
                13,
                null,
                "org-001/file_different_uploader",
                "pending",
                "available",
                now.AddMinutes(-30),
                now.AddMinutes(-30)),
            StoredFileRecord.Create(
                "file_other_tenant",
                "org-002",
                "prod",
                "Notification",
                "Message",
                "user-001",
                "notification-attachment",
                "other-tenant.csv",
                "text/csv",
                15,
                null,
                "org-002/file_other_tenant",
                "pending",
                "available",
                now.AddMinutes(-25),
                now.AddMinutes(-25)),
            StoredFileRecord.Create(
                "file_archived",
                "org-001",
                "prod",
                "Notification",
                "Message",
                "user-001",
                "notification-attachment",
                "archived.csv",
                "text/csv",
                14,
                null,
                "org-001/file_archived",
                "pending",
                "archived",
                now.AddMinutes(-20),
                now.AddMinutes(-20)));
        await dbContext.SaveChangesAsync();

        var result = await service.ListFilesAsync(
            new ListFilesRequest(
                "org-001",
                "prod",
                "notification-attachment",
                "user-001",
                now.AddHours(-3),
                now,
                "available",
                Skip: 1,
                Take: 1),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Total);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal("file_match_1", item.FileId);
        Assert.Equal("user-001", item.Owner.OwnerId);
        AssertObjectKeyIsNotExposed(result.Value);
    }

    [Fact]
    public async Task InMemoryListFiles_FiltersByTenantBeforeOtherFilters()
    {
        var service = new InMemoryFileStorageService();
        var orgFile = (await service.CreateUploadSessionAsync(CreateUploadRequest(), CancellationToken.None)).Value!;
        var otherTenantFile = (await service.CreateUploadSessionAsync(
            CreateUploadRequest() with { OrganizationId = "org-002" },
            CancellationToken.None)).Value!;
        Assert.Equal(StatusCodes.Status200OK, (await service.CompleteUploadSessionAsync(
            orgFile.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", "sha256:test", 4096),
            CancellationToken.None)).StatusCode);
        Assert.Equal(StatusCodes.Status200OK, (await service.CompleteUploadSessionAsync(
            otherTenantFile.UploadSessionId,
            new CompleteUploadSessionRequest("org-002", "prod", "application-package", "sha256:test", 4096),
            CancellationToken.None)).StatusCode);

        var result = await service.ListFilesAsync(
            new ListFilesRequest("org-001", "prod", "application-package", null, null, null, "available"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(orgFile.FileId, item.FileId);
        Assert.Equal("org-001", item.OrganizationId);
    }

    [Fact]
    public async Task InMemoryCreateUploadSession_UsesVersion7FileId()
    {
        var service = new InMemoryFileStorageService();

        var result = await service.CreateUploadSessionAsync(CreateUploadRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Empty(GuidVersionAssertions.Version7GuidSuffixFailures(result.Value.FileId, "file_"));
    }

    [Fact]
    public async Task TryGetUploadSessionIdForDownloadGrant_ExpiredGrant_ReturnsFalse()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        var created = (await service.CreateUploadSessionAsync(CreateUploadRequest(), CancellationToken.None)).Value!;
        var complete = await service.CompleteUploadSessionAsync(
            created.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", "sha256:test", 4096),
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, complete.StatusCode);
        var now = DateTimeOffset.UtcNow;
        dbContext.DownloadGrants.Add(DownloadGrantRecord.Create(
            "dgr_expired",
            created.FileId,
            "org-001",
            "prod",
            "server-proxy",
            now.AddMinutes(-20),
            now.AddMinutes(-10)));
        await dbContext.SaveChangesAsync();

        var uploadSessionId = await service.GetUploadSessionIdForDownloadGrantAsync(
            "dgr_expired",
            "org-001",
            "prod",
            CancellationToken.None);

        Assert.Null(uploadSessionId);
    }

    [Fact]
    public async Task GetUploadSessionIdForDownloadGrant_TenantMismatch_DoesNotRedeemGrant()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        AddCompletedTusFileWithGrant(dbContext, "file_123", "ups_123", scanStatus: "clean", grantId: "dgr_123");
        await dbContext.SaveChangesAsync();

        var uploadSessionId = await service.GetUploadSessionIdForDownloadGrantAsync(
            "dgr_123",
            "org-other",
            "prod",
            CancellationToken.None);

        Assert.Null(uploadSessionId);
        Assert.Equal(1, await dbContext.DownloadGrants.CountAsync());
    }

    [Fact]
    public async Task GetUploadSessionIdForDownloadGrant_CleanFile_ConsumesGrantOnce()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        AddCompletedTusFileWithGrant(dbContext, "file_123", "ups_123", scanStatus: "clean", grantId: "dgr_123");
        await dbContext.SaveChangesAsync();

        var first = await service.GetUploadSessionIdForDownloadGrantAsync(
            "dgr_123",
            "org-001",
            "prod",
            CancellationToken.None);
        var second = await service.GetUploadSessionIdForDownloadGrantAsync(
            "dgr_123",
            "org-001",
            "prod",
            CancellationToken.None);

        Assert.Equal("ups_123", first);
        Assert.Null(second);
        Assert.Equal(0, await dbContext.DownloadGrants.CountAsync());
    }

    [Fact]
    public async Task GetUploadSessionIdForDownloadGrant_NonCleanScanStatus_DoesNotRedeemGrant()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        AddCompletedTusFileWithGrant(dbContext, "file_pending", "ups_pending", scanStatus: "pending", grantId: "dgr_pending");
        await dbContext.SaveChangesAsync();

        var uploadSessionId = await service.GetUploadSessionIdForDownloadGrantAsync(
            "dgr_pending",
            "org-001",
            "prod",
            CancellationToken.None);

        Assert.Null(uploadSessionId);
        Assert.Equal(1, await dbContext.DownloadGrants.CountAsync());
    }

    [Fact]
    public async Task GarbageCollector_RemovesExpiredSessionsExpiredGrantsAndOrphanTusBytes()
    {
        var rootPath = CreateTempDirectory();
        try
        {
            await using var dbContext = CreateDbContext();
            var now = DateTimeOffset.UtcNow;
            AddUploadSession(dbContext, "ups_expired", "file_expired", now.AddMinutes(-30), now.AddMinutes(-10), completed: false);
            AddUploadSession(dbContext, "ups_active", "file_active", now.AddMinutes(-1), now.AddMinutes(10), completed: false);
            AddUploadSession(dbContext, "ups_completed", "file_completed", now.AddMinutes(-30), now.AddMinutes(-10), completed: true);
            dbContext.StoredFiles.Add(StoredFileRecord.Create(
                "file_completed",
                "org-001",
                "prod",
                "AppHub",
                "ApplicationPackage",
                "app-42",
                "application-package",
                "completed.zip",
                "application/zip",
                5,
                null,
                "org-001/file_completed",
                "clean",
                "available",
                now.AddMinutes(-30),
                now.AddMinutes(-20)));
            dbContext.DownloadGrants.AddRange(
                DownloadGrantRecord.Create("dgr_expired", "file_completed", "org-001", "prod", "server-proxy", now.AddMinutes(-20), now.AddMinutes(-1)),
                DownloadGrantRecord.Create("dgr_active", "file_completed", "org-001", "prod", "server-proxy", now.AddMinutes(-1), now.AddMinutes(10)));
            await dbContext.SaveChangesAsync();

            var store = CreateTusStore(rootPath);
            await WriteTusBytesAsync(store, "ups_expired");
            await WriteTusBytesAsync(store, "ups_active");
            await WriteTusBytesAsync(store, "ups_completed");
            await WriteTusBytesAsync(store, "ups_orphan");
            foreach (var path in Directory.EnumerateFiles(rootPath))
            {
                File.SetLastWriteTimeUtc(path, now.AddMinutes(-10).UtcDateTime);
            }

            var collector = new PostgreSqlFileStorageGarbageCollector(
                dbContext,
                new TestTusStoreAccessor(store),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["FileStorage:GarbageCollection:OrphanTusFileGraceSeconds"] = "60"
                    })
                    .Build());

            var result = await collector.CollectAsync(CancellationToken.None);

            Assert.Equal(1, result.ExpiredUploadSessionsRemoved);
            Assert.Equal(1, result.ExpiredDownloadGrantsRemoved);
            Assert.Equal(2, result.LocalTusFilesRemoved);
            Assert.False(store.Exists("ups_expired"));
            Assert.False(store.Exists("ups_orphan"));
            Assert.True(store.Exists("ups_active"));
            Assert.True(store.Exists("ups_completed"));
            Assert.Equal(["ups_active", "ups_completed"], await dbContext.UploadSessions.OrderBy(x => x.UploadSessionId).Select(x => x.UploadSessionId).ToArrayAsync());
            Assert.Equal(["dgr_active"], await dbContext.DownloadGrants.Select(x => x.DownloadGrantId).ToArrayAsync());
        }
        finally
        {
            DeleteTempDirectory(rootPath);
        }
    }

    [Fact]
    public async Task CreateDownloadGrant_InsertsDownloadGrantRecord()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);
        dbContext.StoredFiles.Add(StoredFileRecord.Create(
            "file_123",
            "org-001",
            "prod",
            "AppHub",
            "ApplicationPackage",
            "app-42",
            "application-package",
            "demo.zip",
            "application/zip",
            4096,
            "sha256:test",
            "org-001/file_123",
            "clean",
            "available",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var result = await service.CreateDownloadGrantAsync(
            "file_123",
            new CreateDownloadGrantRequest("org-001", "prod"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("file_123", result.Value.FileId);
        Assert.Matches("^/api/files/v1/download-grants/[^/]+/content$", result.Value.Download.Url);
        var grant = await dbContext.DownloadGrants.SingleAsync();
        Assert.Equal("file_123", grant.FileId);
        Assert.Equal("org-001", grant.OrganizationId);
        Assert.Equal("prod", grant.EnvironmentId);
        Assert.Equal("server-proxy", grant.Provider);
        AssertObjectKeyIsNotExposed(result.Value);
    }

    private static void AddCompletedTusFileWithGrant(
        ApplicationDbContext dbContext,
        string fileId,
        string uploadSessionId,
        string scanStatus,
        string grantId)
    {
        var now = DateTimeOffset.UtcNow;
        AddUploadSession(dbContext, uploadSessionId, fileId, now.AddMinutes(-5), now.AddMinutes(5), completed: true);
        dbContext.StoredFiles.Add(StoredFileRecord.Create(
            fileId,
            "org-001",
            "prod",
            "AppHub",
            "ApplicationPackage",
            "app-42",
            "application-package",
            "demo.zip",
            "application/zip",
            4096,
            "sha256:test",
            $"org-001/{fileId}",
            scanStatus,
            "available",
            now.AddMinutes(-5),
            now));
        dbContext.DownloadGrants.Add(DownloadGrantRecord.Create(
            grantId,
            fileId,
            "org-001",
            "prod",
            "server-proxy",
            now,
            now.AddMinutes(10)));
    }

    private static void AddUploadSession(
        ApplicationDbContext dbContext,
        string uploadSessionId,
        string fileId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        bool completed)
    {
        var session = UploadSessionRecord.Create(
            uploadSessionId,
            fileId,
            "org-001",
            "prod",
            "AppHub",
            "ApplicationPackage",
            "app-42",
            "application-package",
            "demo.zip",
            "application/zip",
            5,
            null,
            $"org-001/{fileId}",
            "tus",
            createdAtUtc,
            expiresAtUtc);
        if (completed)
        {
            session.MarkCompleted(createdAtUtc.AddMinutes(1));
        }

        dbContext.UploadSessions.Add(session);
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

    private static async Task WriteTusBytesAsync(LocalTusFileStore store, string uploadSessionId)
    {
        await using var stream = new MemoryStream("hello"u8.ToArray());
        await store.AppendAsync(uploadSessionId, 0, stream, CancellationToken.None);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nerv-filestorage-gc-{Guid.NewGuid():N}");
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

    private sealed class TestTusStoreAccessor(LocalTusFileStore localStore) : ILocalTusFileStoreAccessor
    {
        public bool TryGet(out LocalTusFileStore store)
        {
            store = localStore;
            return true;
        }
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

    private static void AssertObjectKeyIsNotExposed<T>(T response)
    {
        var json = JsonSerializer.Serialize(response, WebJsonOptions);
        Assert.DoesNotContain("objectKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("object_key", json, StringComparison.OrdinalIgnoreCase);
    }

}
