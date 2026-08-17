using Npgsql;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.FileStorage.Web.Application.Files;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageRestartPersistenceTests
{
    [FileStorageRealPostgresFact]
    public async Task Metadata_usage_and_download_grant_survive_web_host_restart()
    {
        await ResetFileStorageSchemaAsync();
        await SeedLegacyScanStatesAsync();
        string fileId;
        string uploadSessionId;
        string grantId;

        await using (var firstFactory = CreateFactory(LaneConnectionString, autoMigrate: true))
        {
            using var client = CreateClient(firstFactory);
            using (var migrationScope = firstFactory.Services.CreateScope())
            {
                var contentIndex = Assert.IsAssignableFrom<ILocalFileContentIndex>(
                    migrationScope.ServiceProvider.GetRequiredService<IFileStorageService>());
                Assert.Equal(
                    "legacy-upload-clean",
                    await contentIndex.GetUploadSessionIdForDownloadGrantAsync(
                        "legacy-grant-clean",
                        "org-legacy-scan",
                        "production",
                        CancellationToken.None));

                foreach (var blockedState in new[] { "malware", "pending", "failed" })
                {
                    Assert.Null(await contentIndex.GetUploadSessionIdForDownloadGrantAsync(
                        $"legacy-grant-{blockedState}",
                        "org-legacy-scan",
                        "production",
                        CancellationToken.None));
                }

                var migrationDb = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var migratedFiles = await migrationDb.StoredFiles
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == "org-legacy-scan")
                    .OrderBy(x => x.FileId)
                    .ToArrayAsync();
                Assert.Equal("available", migratedFiles.Single(x => x.FileId == "legacy-file-clean").Status);
                foreach (var blockedState in new[] { "malware", "pending", "failed" })
                {
                    var migrated = migratedFiles.Single(x => x.FileId == $"legacy-file-{blockedState}");
                    Assert.Equal("deleted", migrated.Status);
                    Assert.NotNull(migrated.DeletedAtUtc);
                    Assert.NotNull(migrated.PhysicalDeleteAfterUtc);
                    Assert.StartsWith($"scan-removal:{blockedState}", migrated.DeletionReason, StringComparison.Ordinal);
                }

                Assert.Equal(
                    new[] { "legacy-grant-failed", "legacy-grant-malware", "legacy-grant-pending" },
                    await migrationDb.DownloadGrants
                        .AsNoTracking()
                        .Where(x => x.OrganizationId == "org-legacy-scan")
                        .OrderBy(x => x.DownloadGrantId)
                        .Select(x => x.DownloadGrantId)
                        .ToArrayAsync());
            }

            var createdResponse = await client.PostAsJsonAsync(
                "/api/files/v1/upload-sessions",
                new CreateUploadSessionRequest(
                    "org-restart",
                    "production",
                    new OwnerReference("AppHub", "ApplicationPackage", "app-restart"),
                    "application-package",
                    "restart.zip",
                    "application/zip",
                    4096,
                    "sha256:restart"));
            createdResponse.EnsureSuccessStatusCode();
            var created = await createdResponse.Content.ReadFromJsonAsync<CreateUploadSessionResponse>();
            Assert.NotNull(created);
            fileId = created.FileId;
            uploadSessionId = created.UploadSessionId;

            var completedResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest(
                    "org-restart",
                    "production",
                    "application-package",
                    "sha256:restart",
                    4096));
            completedResponse.EnsureSuccessStatusCode();

            var grantResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/files/{fileId}/download-grants",
                new CreateDownloadGrantRequest("org-restart", "production"));
            grantResponse.EnsureSuccessStatusCode();
            var grant = await grantResponse.Content.ReadFromJsonAsync<DownloadGrantResponse>();
            Assert.NotNull(grant);
            var grantUrlSegments = grant.Download.Url.Split('/', StringSplitOptions.RemoveEmptyEntries);
            grantId = grantUrlSegments[^2];
        }

        await using (var restartedFactory = CreateFactory(LaneConnectionString, autoMigrate: false))
        {
            using var client = CreateClient(restartedFactory);
            var metadata = await client.GetFromJsonAsync<FileMetadataResponse>($"/api/files/v1/files/{fileId}");
            var usage = await client.GetFromJsonAsync<FileStorageUsageResponse>(
                "/api/files/v1/usage?organizationId=org-restart&environmentId=production&filePurpose=application-package");
            using var scope = restartedFactory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persistedUploadSession = await dbContext.UploadSessions
                .AsNoTracking()
                .SingleAsync(x => x.UploadSessionId == uploadSessionId);
            var persistedGrant = await dbContext.DownloadGrants
                .AsNoTracking()
                .SingleAsync(x => x.DownloadGrantId == grantId);

            Assert.NotNull(metadata);
            Assert.Equal(fileId, metadata.FileId);
            Assert.Equal("restart.zip", metadata.FileName);
            Assert.NotNull(usage);
            Assert.Equal(4096, usage.UsedBytes);
            Assert.True(persistedUploadSession.Completed);
            Assert.Equal(fileId, persistedUploadSession.FileId);
            Assert.Equal(fileId, persistedGrant.FileId);
            Assert.Equal("org-restart", persistedGrant.OrganizationId);
            Assert.Equal("production", persistedGrant.EnvironmentId);
        }
    }

    // NERV-688 拆解③：FileStorage 的重启持久化冒烟使用 lane runner 注入的成员数据库
    // （NERV_IIP_TEST_POSTGRES），不再自建内层数据库——内层数据库外层既读不到失败诊断，也证明不了清理。
    private static string LaneConnectionString =>
        Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
        ?? throw new InvalidOperationException("NERV_IIP_TEST_POSTGRES must be set for the FileStorage restart persistence smoke.");

    private static async Task ResetFileStorageSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(LaneConnectionString);
        await connection.OpenAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier("filestorage");
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedLegacyScanStatesAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                LaneConnectionString,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "filestorage"))
            .Options;
        await using (var dbContext = new ApplicationDbContext(options))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260705065020_AddFileStorageSecurityHardening");
        }

        await using var connection = new NpgsqlConnection(LaneConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO filestorage.stored_files (
                file_id, organization_id, environment_id, owner_service, owner_type, owner_id,
                file_purpose, file_name, content_type, size_bytes, object_key, scan_status, status,
                created_at_utc, completed_at_utc, deletion_reason)
            SELECT
                'legacy-file-' || scan_state,
                'org-legacy-scan',
                'production',
                'legacy-test',
                'migration',
                scan_state,
                'attachment',
                scan_state || '.txt',
                'text/plain',
                1,
                'org-legacy-scan/legacy-file-' || scan_state,
                scan_state,
                'available',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP,
                CASE WHEN scan_state = 'failed' THEN repeat('x', 256) ELSE NULL END
            FROM (VALUES ('clean'), ('malware'), ('pending'), ('failed')) AS states(scan_state);

            INSERT INTO filestorage.upload_sessions (
                upload_session_id, file_id, organization_id, environment_id, owner_service, owner_type,
                owner_id, file_purpose, file_name, content_type, expected_size_bytes, object_key, provider,
                created_at_utc, expires_at_utc, completed, completed_at_utc)
            SELECT
                'legacy-upload-' || scan_state,
                'legacy-file-' || scan_state,
                'org-legacy-scan',
                'production',
                'legacy-test',
                'migration',
                scan_state,
                'attachment',
                scan_state || '.txt',
                'text/plain',
                1,
                'org-legacy-scan/legacy-file-' || scan_state,
                'server-proxy',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP + INTERVAL '1 hour',
                TRUE,
                CURRENT_TIMESTAMP
            FROM (VALUES ('clean'), ('malware'), ('pending'), ('failed')) AS states(scan_state);

            INSERT INTO filestorage.download_grants (
                download_grant_id, file_id, organization_id, environment_id, provider,
                created_at_utc, expires_at_utc)
            SELECT
                'legacy-grant-' || scan_state,
                'legacy-file-' || scan_state,
                'org-legacy-scan',
                'production',
                'server-proxy',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP + INTERVAL '1 hour'
            FROM (VALUES ('clean'), ('malware'), ('pending'), ('failed')) AS states(scan_state);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, bool autoMigrate)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Persistence:Provider", "PostgreSQL");
                builder.UseSetting("Persistence:AutoMigrate", autoMigrate.ToString());
                builder.UseSetting("ConnectionStrings:FileStorageDb", connectionString);
                builder.UseSetting("FileStorage:GarbageCollection:IntervalSeconds", "3600");
            });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        return client;
    }

}

internal sealed class FileStorageRealPostgresFactAttribute : FactAttribute
{
    public FileStorageRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the FileStorage restart persistence smoke.";
        }
    }
}
