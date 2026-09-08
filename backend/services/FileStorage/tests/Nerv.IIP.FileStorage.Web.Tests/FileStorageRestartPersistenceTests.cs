using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.Testing;
using static Nerv.IIP.FileStorage.Web.Tests.TemplateAssetRetirementProofTests;
using Npgsql;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed partial class FileStorageRestartPersistenceTests
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

                var migratedUploadStates = await migrationDb.UploadSessions
                    .AsNoTracking()
                    .Where(x => x.UploadSessionId == "legacy-upload-orphan" || x.UploadSessionId == "legacy-upload-open")
                    .OrderBy(x => x.UploadSessionId)
                    .ToArrayAsync();
                var migratedOpen = migratedUploadStates.Single(x => x.UploadSessionId == "legacy-upload-open");
                Assert.Equal(UploadSessionState.Open, migratedOpen.State);
                Assert.Null(migratedOpen.CommitId);
                var migratedOrphan = migratedUploadStates.Single(x => x.UploadSessionId == "legacy-upload-orphan");
                Assert.Equal(UploadSessionState.Committing, migratedOrphan.State);
                Assert.True(migratedOrphan.LegacyCompleted);
                Assert.False(migratedOrphan.Completed);
                Assert.StartsWith("legacy_", migratedOrphan.CommitId, StringComparison.Ordinal);
                Assert.Equal($"sha256:{new string('a', 64)}", migratedOrphan.CommitChecksum);
                Assert.Null(migratedOrphan.CompletedAtUtc);
                Assert.NotNull(migratedOrphan.StorageActionStartedAtUtc);

                var recoveryService = FileStorageServiceTestFactory.Create(
                    migrationDb,
                    new ServerProxyUploadProvider(),
                    configuration: migrationScope.ServiceProvider.GetRequiredService<IConfiguration>(),
                    timeProvider: migrationScope.ServiceProvider.GetRequiredService<TimeProvider>(),
                    commitStorage: new UnavailableUploadCommitStorage(),
                    gateRegistry: new UploadSessionGateRegistry(),
                    executionLeaseManager: migrationScope.ServiceProvider.GetRequiredService<UploadCommitExecutionLeaseManager>());
                var recovery = new UploadCommitRecoveryProcessor(
                    migrationDb,
                    FileStorageServiceTestFactory.CreateRecoveryScopeFactory(recoveryService),
                    migrationScope.ServiceProvider.GetRequiredService<TimeProvider>(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadCommitRecoveryProcessor>.Instance);
                Assert.Equal(new UploadCommitRecoveryResult(1, 0, 1), await recovery.RunOnceAsync(CancellationToken.None));
                migrationDb.ChangeTracker.Clear();
                var retainedOrphan = await migrationDb.UploadSessions
                    .SingleAsync(x => x.UploadSessionId == "legacy-upload-orphan");
                Assert.Equal(UploadSessionState.Committing, retainedOrphan.State);
                Assert.NotNull(retainedOrphan.StorageActionStartedAtUtc);
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

            var completeRequest = new CompleteUploadSessionRequest(
                "org-restart",
                "production",
                "application-package",
                "sha256:restart",
                4096);
            var completions = await Task.WhenAll(
                client.PostAsJsonAsync($"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete", completeRequest),
                client.PostAsJsonAsync($"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete", completeRequest));
            foreach (var completion in completions)
            {
                completion.EnsureSuccessStatusCode();
            }

            using (var evidenceScope = firstFactory.Services.CreateScope())
            {
                var storage = Assert.IsType<RealPostgresTestCommitStorage>(
                    evidenceScope.ServiceProvider.GetRequiredService<IUploadCommitStorage>());
                Assert.Equal(1, storage.Attempts);
            }

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
            Assert.True(persistedUploadSession.LegacyCompleted);
            Assert.Equal("completed", persistedUploadSession.State);
            Assert.False(string.IsNullOrWhiteSpace(persistedUploadSession.CommitId));
            Assert.NotNull(persistedUploadSession.CommittingAtUtc);
            Assert.NotNull(persistedUploadSession.StorageActionStartedAtUtc);
            Assert.Equal(fileId, persistedUploadSession.FileId);
            Assert.Equal(fileId, persistedGrant.FileId);
            Assert.Equal("org-restart", persistedGrant.OrganizationId);
            Assert.Equal("production", persistedGrant.EnvironmentId);
        }
    }

    [FileStorageRealPostgresFact]
    public async Task Independent_gate_registries_claim_one_database_owner_and_create_one_file_fact()
    {
        await ResetFileStorageSchemaAsync();
        await using var factory = CreateFactory(LaneConnectionString, autoMigrate: true);
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var storage = new BlockingRealPostgresCommitStorage();
        var configuration = firstScope.ServiceProvider.GetRequiredService<IConfiguration>();
        var clock = firstScope.ServiceProvider.GetRequiredService<TimeProvider>();
        var first = FileStorageServiceTestFactory.Create(
            firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new ServerProxyUploadProvider(),
            configuration: configuration,
            timeProvider: clock,
            commitStorage: storage,
            gateRegistry: new UploadSessionGateRegistry(),
            executionLeaseManager: firstScope.ServiceProvider.GetRequiredService<UploadCommitExecutionLeaseManager>());
        var second = FileStorageServiceTestFactory.Create(
            secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new ServerProxyUploadProvider(),
            configuration: configuration,
            timeProvider: clock,
            commitStorage: storage,
            gateRegistry: new UploadSessionGateRegistry(),
            executionLeaseManager: secondScope.ServiceProvider.GetRequiredService<UploadCommitExecutionLeaseManager>());
        var checksum = $"sha256:{new string('c', 64)}";
        var created = (await first.CreateUploadSessionAsync(
            new CreateUploadSessionRequest(
                "org-owner",
                "production",
                new OwnerReference("AppHub", "ApplicationPackage", "app-owner"),
                "application-package",
                "owner.zip",
                "application/zip",
                4096,
                checksum),
            CancellationToken.None)).Value!;
        var request = new CompleteUploadSessionRequest(
            "org-owner",
            "production",
            "application-package",
            checksum,
            4096);

        var firstCompletion = first.CompleteUploadSessionAsync(created.UploadSessionId, request, CancellationToken.None);
        await storage.Entered;
        var secondCompletion = await second.CompleteUploadSessionAsync(created.UploadSessionId, request, CancellationToken.None);
        storage.Release();
        var firstResult = await firstCompletion;

        Assert.Equal(StatusCodes.Status409Conflict, secondCompletion.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, firstResult.StatusCode);
        using var evidenceScope = factory.Services.CreateScope();
        var evidenceDb = evidenceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await evidenceDb.StoredFiles.Where(x => x.FileId == created.FileId).ToArrayAsync());
        var completed = await evidenceDb.UploadSessions.SingleAsync(x => x.UploadSessionId == created.UploadSessionId);
        Assert.Equal(UploadSessionState.Completed, completed.State);
    }

    [FileStorageRealPostgresFact]
    public async Task Expand_migration_keeps_legacy_completed_write_readable_by_new_protocol()
    {
        await ResetFileStorageSchemaAsync();
        await using var factory = CreateFactory(LaneConnectionString, autoMigrate: true);
        _ = factory.CreateClient();

        await using (var connection = new NpgsqlConnection(LaneConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO filestorage.stored_files (
                    file_id, organization_id, environment_id, owner_service, owner_type, owner_id,
                    file_purpose, file_name, content_type, size_bytes, checksum, object_key, status,
                    created_at_utc, completed_at_utc)
                VALUES (
                    'file_legacy_expand', 'org-expand', 'production', 'legacy', 'attachment', 'owner-expand',
                    'attachment', 'expand.txt', 'text/plain', 5, NULL,
                    'org-expand/file_legacy_expand', 'available', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

                INSERT INTO filestorage.upload_sessions (
                    upload_session_id, file_id, organization_id, environment_id, owner_service, owner_type,
                    owner_id, file_purpose, file_name, content_type, expected_size_bytes, checksum, object_key,
                    provider, created_at_utc, expires_at_utc, completed, completed_at_utc)
                VALUES (
                    'ups_legacy_expand', 'file_legacy_expand', 'org-expand', 'production', 'legacy', 'attachment',
                    'owner-expand', 'attachment', 'expand.txt', 'text/plain', 5, NULL,
                    'org-expand/file_legacy_expand', 'server-proxy', CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP + INTERVAL '1 hour', TRUE, CURRENT_TIMESTAMP);
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await db.UploadSessions.AsNoTracking().SingleAsync(x => x.UploadSessionId == "ups_legacy_expand");
        Assert.True(session.LegacyCompleted);
        Assert.True(session.Completed);
        Assert.Equal(UploadSessionState.Open, session.State);

        var result = await scope.ServiceProvider.GetRequiredService<IFileStorageService>().CompleteUploadSessionAsync(
            session.UploadSessionId,
            new CompleteUploadSessionRequest("org-expand", "production", "attachment", null, 5),
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        var mutationRan = false;
        var mutation = await scope.ServiceProvider.GetRequiredService<IUploadSessionMutationGate>()
            .ExecutePatchMutationAsync(
                session.UploadSessionId,
                _ =>
                {
                    mutationRan = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None);
        Assert.Equal(UploadSessionMutationResult.NotOpen, mutation);
        Assert.False(mutationRan);
    }

    [FileStorageRealPostgresFact]
    public async Task Recovery_batch_prioritizes_never_attempted_intent_before_due_retries()
    {
        await ResetFileStorageSchemaAsync();
        await using var factory = CreateFactory(LaneConnectionString, autoMigrate: true);
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        for (var index = 0; index < 25; index++)
        {
            var retry = CreateConstraintSession(
                $"ups_due_retry_{index:D2}",
                $"file_due_retry_{index:D2}",
                $"org-constraint/file_due_retry_{index:D2}");
            retry.BeginCommit($"cmt_due_retry_{index:D2}", null, now.AddMinutes(-2));
            retry.RecordRecoveryFailure("retryable", now.AddMinutes(-1));
            db.UploadSessions.Add(retry);
        }

        var fresh = CreateConstraintSession(
            "ups_never_attempted",
            "file_never_attempted",
            "org-constraint/file_never_attempted");
        fresh.BeginCommit("cmt_never_attempted", null, now.AddMinutes(-1));
        db.UploadSessions.Add(fresh);
        await db.SaveChangesAsync();

        var processed = new ConcurrentBag<string>();
        var services = new ServiceCollection();
        services.AddScoped<IFileStorageService>(_ => new RecordingRecoveryFileStorageService(processed));
        await using var recoveryProvider = services.BuildServiceProvider();
        var processor = new UploadCommitRecoveryProcessor(
            db,
            recoveryProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadCommitRecoveryProcessor>.Instance);

        var result = await processor.RunOnceAsync(CancellationToken.None);

        Assert.Equal(25, result.Examined);
        Assert.Contains("ups_never_attempted", processed);
    }

    [FileStorageRealPostgresFact]
    public async Task Database_executes_state_check_and_unique_commit_id_constraints()
    {
        await ResetFileStorageSchemaAsync();
        await using var factory = CreateFactory(LaneConnectionString, autoMigrate: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = CreateConstraintSession("ups_constraint_1", "file_constraint_1", "org-constraint/file-1");
        var second = CreateConstraintSession("ups_constraint_2", "file_constraint_2", "org-constraint/file-2");
        db.UploadSessions.AddRange(first, second);
        await db.SaveChangesAsync();
        first.BeginCommit("cmt_duplicate", null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        second.BeginCommit("cmt_duplicate", null, DateTimeOffset.UtcNow);
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.InnerException is PostgresException pg ? pg.SqlState : null);

        await using var connection = new NpgsqlConnection(LaneConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE filestorage.upload_sessions SET state = 'broken' WHERE upload_session_id = 'ups_constraint_1'";
        var invalidState = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidState.SqlState);
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

            INSERT INTO filestorage.upload_sessions (
                upload_session_id, file_id, organization_id, environment_id, owner_service, owner_type,
                owner_id, file_purpose, file_name, content_type, expected_size_bytes, checksum, object_key, provider,
                created_at_utc, expires_at_utc, completed, completed_at_utc)
            SELECT
                'legacy-upload-' || legacy_state,
                'legacy-file-' || legacy_state,
                'org-legacy-scan',
                'production',
                'legacy-test',
                'migration',
                legacy_state,
                'attachment',
                legacy_state || '.txt',
                'text/plain',
                1,
                CASE WHEN legacy_state = 'orphan' THEN 'SHA256:' || repeat('A', 64) ELSE NULL END,
                'org-legacy-scan/legacy-file-' || legacy_state,
                'server-proxy',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP + INTERVAL '1 hour',
                completed,
                CASE WHEN completed THEN CURRENT_TIMESTAMP ELSE NULL END
            FROM (VALUES ('orphan', TRUE), ('open', FALSE)) AS states(legacy_state, completed);

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
        return new FileStorageUnconfiguredWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Persistence:Provider", "PostgreSQL");
                builder.UseSetting("Persistence:AutoMigrate", autoMigrate.ToString());
                builder.UseSetting("ConnectionStrings:FileStorageDb", connectionString);
                builder.UseSetting("FileStorage:GarbageCollection:IntervalSeconds", "3600");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IUploadCommitStorage>();
                    services.AddSingleton<IUploadCommitStorage>(new RealPostgresTestCommitStorage());
                });
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

    private sealed class RealPostgresTestCommitStorage : IUploadCommitStorage
    {
        private int attempts;
        public int Attempts => Volatile.Read(ref attempts);

        public Task<UploadCommitStorageResult> CommitAsync(
            UploadCommitIntent intent,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);
            return Task.FromResult(UploadCommitStorageResult.Verified(
                intent.ExpectedSizeBytes,
                $"sha256:{new string('c', 64)}"));
        }
    }

    private sealed class BlockingRealPostgresCommitStorage : IUploadCommitStorage
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;

        public void Release() => release.TrySetResult();

        public async Task<UploadCommitStorageResult> CommitAsync(
            UploadCommitIntent intent,
            CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return UploadCommitStorageResult.Verified(
                intent.ExpectedSizeBytes,
                $"sha256:{new string('c', 64)}");
        }
    }

    private sealed class RecordingRecoveryFileStorageService(ConcurrentBag<string> processed) : IFileStorageService
    {
        public Task<FileStorageResult<FileMetadataResponse>> CompleteUploadSessionAsync(
            string uploadSessionId,
            CompleteUploadSessionRequest request,
            CancellationToken cancellationToken)
        {
            processed.Add(uploadSessionId);
            return Task.FromResult(FileStorageResult<FileMetadataResponse>.ServiceUnavailable("恢复延后探针。"));
        }

        public Task<FileStorageResult<CreateUploadSessionResponse>> CreateUploadSessionAsync(
            CreateUploadSessionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileStorageResult<FileMetadataResponse>> GetFileMetadataAsync(
            string fileId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileStorageResult<FileListResponse>> ListFilesAsync(
            ListFilesRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileStorageResult<FileStorageUsageResponse>> GetUsageAsync(
            FileStorageUsageRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileStorageResult<DownloadGrantResponse>> CreateDownloadGrantAsync(
            string fileId,
            CreateDownloadGrantRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static UploadSessionRecord CreateConstraintSession(
        string uploadSessionId,
        string fileId,
        string objectKey) =>
        UploadSessionRecord.Create(
            uploadSessionId,
            fileId,
            "org-constraint",
            "production",
            "test",
            "constraint",
            fileId,
            "attachment",
            $"{fileId}.txt",
            "text/plain",
            1,
            null,
            objectKey,
            "server-proxy",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

}

internal sealed class FileStorageRealPostgresFactAttribute : FactAttribute
{
    public FileStorageRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the FileStorage PostgreSQL profile tests.";
        }
    }
}

public sealed partial class FileStorageRestartPersistenceTests
{
    private const string RetirementRoute = "/internal/file-storage/v1/template-asset-retirements";

    [FileStorageRealPostgresFact]
    public async Task Retirement_acceptance_replays_frozen_horizon_and_holds_content_and_legacy_gc_on_postgres()
    {
        await ResetFileStorageSchemaAsync();
        var clock = new FakeTimeProvider(Epoch);
        var root = Directory.CreateTempSubdirectory("nerv-3044-retirement-");
        try
        {
            RetireTemplateAssetResponse accepted;
            await using (var factory = RetirementFactory(clock, root.FullName))
            {
                using var client = CreateClient(factory);
                await SeedRetirementAssetAsync(factory);
                using var scope = factory.Services.CreateScope();
                var files = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
                var oldGrant = await files.CreateDownloadGrantAsync("retirement-file", new("retirement-org", "retirement-env"), default);
                Assert.Equal(12, (await files.GetUsageAsync(new("retirement-org", "retirement-env", "barcode-label-template"), default)).Value!.UsedBytes);
                using var response = await client.PostAsJsonAsync(RetirementRoute, Sign(Fields()));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                accepted = (await response.Content.ReadFromJsonAsync<RetireTemplateAssetResponse>())!;
                Assert.Equal(2592000, accepted.ReplayHorizonSeconds);
                Assert.Equal(Epoch, accepted.QuotaReleasedAtUtc);
                Assert.Equal("physical-hold", accepted.Status);
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ChangeTracker.Clear();
                Assert.Equal(0, (await files.GetUsageAsync(new("retirement-org", "retirement-env", "barcode-label-template"), default)).Value!.UsedBytes);
                var newGrant = await files.CreateDownloadGrantAsync("retirement-file", new("retirement-org", "retirement-env"), default);
                foreach (var grant in new[] { oldGrant.Value!, newGrant.Value! })
                {
                    var grantId = grant.Download.Url.Split('/')[5];
                    Assert.Null(await ((ILocalFileContentIndex)files).GetUploadSessionIdForDownloadGrantAsync(
                        grantId, "retirement-org", "retirement-env", default));
                }
                // Same decision cannot change its frozen upstream policy, file or ownership facts.
                foreach (var index in new[] { 8, 9, 10, 11, 12, 13, 14, 17 })
                {
                    var changed = Fields();
                    changed[index] = index is 8 or 9 or 10 ? "600" : index == 14 ? $"sha256:{new string('a', 64)}" : "different";
                    using var conflict = await client.PostAsJsonAsync(RetirementRoute, Sign(changed));
                    Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
                }
                var otherDecision = Fields();
                otherDecision[6] = "01991000-0000-7000-8000-000000003045";
                using var other = await client.PostAsJsonAsync(RetirementRoute, Sign(otherDecision));
                Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
            }

            // A new host with changed valid configuration must still return the original frozen H.
            await using var restarted = RetirementFactory(clock, root.FullName, changedConfiguration: true);
            using var restartedClient = CreateClient(restarted);
            using var replay = await restartedClient.PostAsJsonAsync(RetirementRoute, Sign(Fields()));
            Assert.Equal(accepted, await replay.Content.ReadFromJsonAsync<RetireTemplateAssetResponse>());
            using var verification = restarted.Services.CreateScope();
            var db = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var collector = verification.ServiceProvider.GetRequiredService<PostgreSqlFileStorageGarbageCollector>();
            foreach (var advance in new[] { TimeSpan.FromDays(7), TimeSpan.FromDays(1), TimeSpan.FromDays(83) })
            {
                clock.Advance(advance);
                var result = await collector.CollectAsync(default);
                Assert.Equal(0, result.FormalFilesPhysicallyDeleted);
                Assert.Equal(0, result.LocalTusFilesRemoved);
                Assert.Equal("physical-hold", (await db.StoredFiles.AsNoTracking().SingleAsync()).Status);
                Assert.Null((await db.StoredFiles.AsNoTracking().SingleAsync()).PhysicalDeleteAfterUtc);
                Assert.Single(await db.UploadSessions.AsNoTracking().ToArrayAsync());
                var tombstone = await db.TemplateAssetRetirements.AsNoTracking().SingleAsync();
                Assert.Equal(2592000, tombstone.ReplayHorizonSeconds);
                Assert.Equal(604800, tombstone.PhysicalGraceSeconds);
                Assert.Equal("physical-hold", tombstone.Status);
                var accessor = verification.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>();
                Assert.True(accessor.TryGet(out var bytes));
                Assert.True(bytes.Exists("retirement-upload"));
                Assert.Equal(12, bytes.GetOffset("retirement-upload"));
            }
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>()
                .MigrateAsync("20260824091513_AddDurableUploadCommitProtocol"));
            Assert.Contains("Retirement receipts exist", downgrade.MessageText, StringComparison.Ordinal);
            Assert.Single(await db.TemplateAssetRetirements.AsNoTracking().ToArrayAsync());
        }
        finally { root.Delete(recursive: true); }
    }

    [FileStorageRealPostgresFact]
    public async Task Retirement_verifier_rejects_each_wire_and_resource_constraint_without_writes_on_postgres()
    {
        await ResetFileStorageSchemaAsync();
        await using var factory = RetirementFactory(new FakeTimeProvider(Epoch));
        using var client = CreateClient(factory);
        await SeedRetirementAssetAsync(factory, bytes: false);
        var cases = new List<(string Name, RetireTemplateAssetRequest Request)>();
        foreach (var (index, value) in new (int, string)[]
        {
            (0,"2"), (1,"SHA-256"), (2,"wrong-issuer"), (3,"wrong-audience"), (4,"00"),
            (5,"invalid"), (6,"invalid-decision"), (7,"2"), (8,"0"), (9,"0"), (10,"0"),
            (9,"-1"), (10,"-1"), (9,"7776000"), (11,"wrong-org"), (12,"wrong-env"),
            (13,"wrong-file"), (14,$"sha256:{new string('a',64)}"), (15,"other-service"),
            (16,"other-owner-type"), (17,"other-owner"), (18,"attachment")
        })
        {
            var fields = Fields(); fields[index] = value;
            cases.Add(($"field-{index}-{value}", Sign(fields)));
        }
        foreach (var (issued, expires) in new[] { (0,0), (0,-1), (0,301), (300,301), (301,302), (-301,-300), (-302,-301) })
        {
            var fields = Fields();
            fields[4] = (Epoch.ToUnixTimeSeconds()+issued).ToString(CultureInfo.InvariantCulture);
            fields[5] = (Epoch.ToUnixTimeSeconds()+expires).ToString(CultureInfo.InvariantCulture);
            cases.Add(($"clock-{issued}-{expires}", Sign(fields)));
        }
        var wire = Wire(Fields());
        cases.Add(("bom", SignBytes([0xef,0xbb,0xbf,..wire])));
        cases.Add(("crlf", SignBytes(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(wire).Replace("\n", "\r\n")))));
        cases.Add(("invalid-utf8", SignBytes([..wire[..^1],0xff])));
        cases.Add(("missing-field", Sign(Fields()[..^1])));
        cases.Add(("duplicate-field", Sign([..Fields(),Fields()[18]])));
        var reordered = Fields(); (reordered[11], reordered[12]) = (reordered[12], reordered[11]);
        cases.Add(("field-order", Sign(reordered)));
        cases.Add(("character-length-instead-of-byte-length", SignBytes(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(wire).Replace("9:模板甲", "3:模板甲")))));
        cases.Add(("leading-zero-length", SignBytes(Encoding.UTF8.GetBytes("0"+Encoding.UTF8.GetString(wire)))));
        cases.Add(("wrong-key", SignBytes(wire, Encoding.UTF8.GetBytes(new string('z',32)))));
        cases.Add(("signature-missing", Sign(Fields()) with { Signature = "" }));
        cases.Add(("signature-tampered", Sign(Fields()) with { Signature = new string('A',43) }));
        cases.Add(("signature-padding", Sign(Fields()) with { Signature = Sign(Fields()).Signature+"=" }));
        cases.Add(("payload-padding", Sign(Fields()) with { Payload = Sign(Fields()).Payload+"=" }));
        cases.Add(("payload-missing", Sign(Fields()) with { Payload = "" }));
        foreach (var item in cases)
        {
            using var response = await client.PostAsJsonAsync(RetirementRoute, item.Request);
            Assert.True((int)response.StatusCode is >= 400 and < 500, item.Name);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.TemplateAssetRetirements.ToArrayAsync());
            Assert.Equal("available", (await db.StoredFiles.SingleAsync()).Status);
            var usage = await scope.ServiceProvider.GetRequiredService<IFileStorageService>()
                .GetUsageAsync(new("retirement-org", "retirement-env", "barcode-label-template"), default);
            Assert.Equal(12, usage.Value!.UsedBytes);
        }
        client.DefaultRequestHeaders.Authorization = null;
        using var unauthenticated = await client.PostAsJsonAsync(RetirementRoute, Sign(Fields()));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [FileStorageRealPostgresFact]
    public async Task Retirement_concurrent_duplicate_waits_for_row_lock_and_rollback_is_atomic_on_postgres()
    {
        await ResetFileStorageSchemaAsync();
        await using var factory = RetirementFactory(new FakeTimeProvider(Epoch));
        await SeedRetirementAssetAsync(factory, bytes: false);
        var gate = new RetirementSaveGate();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(LaneConnectionString);
        await using var first = new ApplicationDbContext(options.Options);
        await using var held = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(LaneConnectionString).AddInterceptors(gate).Options);
        var request = new TemplateAssetRetirementProof(Options(), new FakeTimeProvider(Epoch)).Verify(Sign(Fields()))!;
        var pending = new TemplateAssetRetirementStore(held, new FakeTimeProvider(Epoch)).AcceptAsync(request, Options().Storage, default);
        await TestTimeout.RunAsync("retirement before save", async ct => await gate.Entered.Task.WaitAsync(ct), TimeSpan.FromSeconds(15));
        await first.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)first.Database.GetDbConnection()).ProcessID;
        var contender = new TemplateAssetRetirementStore(first, new FakeTimeProvider(Epoch.AddSeconds(1))).AcceptAsync(request, Options().Storage, default);
        try
        {
            await Eventually.AssertAsync("duplicate waits on production retirement row lock", async ct =>
            {
                await using var connection = new NpgsqlConnection(LaneConnectionString);
                await connection.OpenAsync(ct);
                await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @pid AND wait_event_type = 'Lock')", connection);
                query.Parameters.AddWithValue("pid", pid);
                Assert.True((bool)(await query.ExecuteScalarAsync(ct))!);
            }, new(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(20), []));
        }
        finally { gate.Release.TrySetResult(); }
        var results = await TestTimeout.RunAsync("retirement duplicate commit", async ct => await Task.WhenAll(pending, contender).WaitAsync(ct), TimeSpan.FromSeconds(15));
        Assert.All(results, result => Assert.NotNull(result.Receipt));
        Assert.Equal(results[0].Receipt!.AcceptedAtUtc, results[1].Receipt!.AcceptedAtUtc);
        Assert.Equal(1, await first.TemplateAssetRetirements.CountAsync());

        // A real database failure after modifying the tracked file rolls back both persisted facts.
        await ResetFileStorageSchemaAsync();
        await using var rollbackFactory = RetirementFactory(new FakeTimeProvider(Epoch));
        await SeedRetirementAssetAsync(rollbackFactory, bytes: false);
        await using var failed = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(LaneConnectionString).AddInterceptors(new RetirementFailure()).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TemplateAssetRetirementStore(failed, new FakeTimeProvider(Epoch)).AcceptAsync(request, Options().Storage, default));
        using var verify = rollbackFactory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.TemplateAssetRetirements.ToArrayAsync());
        Assert.Equal("available", (await db.StoredFiles.SingleAsync()).Status);
        Assert.Equal(12, (await verify.ServiceProvider.GetRequiredService<IFileStorageService>()
            .GetUsageAsync(new("retirement-org", "retirement-env", "barcode-label-template"), default)).Value!.UsedBytes);
    }

    private static WebApplicationFactory<Program> RetirementFactory(FakeTimeProvider clock, string? root = null, bool changedConfiguration = false) =>
        CreateFactory(LaneConnectionString, autoMigrate: true).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FileStorage:GarbageCollection:IntervalSeconds", "300");
            if (root is not null)
            {
                builder.UseSetting("FileStorage:Tus:RootPath", root);
                builder.UseSetting("FileStorage:UploadProvider", "tus");
            }
            if (changedConfiguration)
                builder.UseSetting("FileStorage:GarbageCollection:PhysicalDeleteGraceSeconds", "3456000");
            builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock); });
        });

    private static async Task SeedRetirementAssetAsync(WebApplicationFactory<Program> factory, bool bytes = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fields = Fields();
        var file = StoredFileRecord.Create(fields[13], fields[11], fields[12], fields[15], fields[16], fields[17], fields[18],
            "template.json", "application/vnd.nerv-iip.label-template+json", 12, fields[14], "retirement-object", "available", Epoch, Epoch);
        var session = UploadSessionRecord.Create("retirement-upload", file.FileId, file.OrganizationId, file.EnvironmentId,
            file.OwnerService, file.OwnerType, file.OwnerId, file.FilePurpose, file.FileName, file.ContentType,
            12, file.Checksum, file.ObjectKey, "tus", Epoch, Epoch.AddMinutes(15));
        session.BeginCommit("retirement-upload-commit", file.Checksum, Epoch);
        session.MarkCompleted(Epoch);
        db.StoredFiles.Add(file); db.UploadSessions.Add(session);
        await db.SaveChangesAsync();
        if (bytes)
        {
            Assert.True(scope.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>().TryGet(out var store));
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("0123456789ab"));
            await store.AppendAsync(session.UploadSessionId, 0, content, default);
        }
    }

    private sealed class RetirementSaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await TestTimeout.RunAsync("release retirement save", async ct => await Release.Task.WaitAsync(ct), TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private sealed class RetirementFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure after SQL writes and before transaction commit.");
    }
}
