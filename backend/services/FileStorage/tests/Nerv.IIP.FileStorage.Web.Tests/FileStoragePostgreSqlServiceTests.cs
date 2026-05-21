using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStoragePostgreSqlServiceTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateUploadSession_PersistsUploadSessionRecord()
    {
        await using var dbContext = CreateDbContext();
        var service = new PostgreSqlFileStorageService(dbContext);

        var result = service.CreateUploadSession(CreateUploadRequest());

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
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

        var result = service.CreateUploadSession(request);

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
        var created = service.CreateUploadSession(CreateUploadRequest()).Value!;

        var result = service.CompleteUploadSession(
            created.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", "sha256:test", 4096));

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
        Assert.Equal("pending", storedFile.ScanStatus);
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

        var result = service.GetFileMetadata("file_123");

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
            "pending",
            "available",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var result = service.CreateDownloadGrant("file_123", new CreateDownloadGrantRequest("org-001", "prod"));

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
