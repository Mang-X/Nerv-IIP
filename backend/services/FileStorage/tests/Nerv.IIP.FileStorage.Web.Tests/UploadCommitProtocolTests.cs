using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class UploadCommitProtocolTests
{
    [Fact]
    public void UploadSessionState_UsesDatabaseContractLiterals()
    {
        Assert.Equal("open", UploadSessionState.Open);
        Assert.Equal("committing", UploadSessionState.Committing);
        Assert.Equal("completed", UploadSessionState.Completed);
    }

    [Fact]
    public async Task PatchWaitingBehindTx1_ObservesCommittingBeforeMutation()
    {
        var services = new ServiceCollection();
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"filestorage-gate-{Guid.NewGuid():N}";
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddSingleton<UploadSessionGateRegistry>();
        services.AddSingleton<IUploadSessionMutationGate, UploadSessionMutationGate>();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UploadSessions.Add(CreateSession("ups_gate"));
            await db.SaveChangesAsync();
        }

        var registry = provider.GetRequiredService<UploadSessionGateRegistry>();
        await using var tx1Gate = await registry.EnterPatchCommitAsync("ups_gate", CancellationToken.None);
        var mutationGate = provider.GetRequiredService<IUploadSessionMutationGate>();
        var secondMutationRan = false;
        var waitingMutation = mutationGate.ExecutePatchMutationAsync(
            "ups_gate",
            _ =>
            {
                secondMutationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Assert.False(waitingMutation.IsCompleted);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = await db.UploadSessions.SingleAsync();
            session.BeginCommit("cmt_gate", null, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        await tx1Gate.DisposeAsync();

        Assert.Equal(UploadSessionMutationResult.NotOpen, await waitingMutation);
        Assert.False(secondMutationRan);
    }

    [Fact]
    public async Task FinalEvidenceChecksumDifferentFromFrozenIntent_FailsClosed()
    {
        await using var db = CreateDbContext();
        var expected = $"sha256:{new string('a', 64)}";
        var service = CreateService(db, new VerifiedStorage($"sha256:{new string('b', 64)}"));
        var created = (await service.CreateUploadSessionAsync(CreateRequest(expected), CancellationToken.None)).Value!;

        var result = await service.CompleteUploadSessionAsync(
            created.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", expected, 5),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        var session = await db.UploadSessions.SingleAsync();
        Assert.Equal(UploadSessionState.Committing, session.State);
        Assert.Null(session.NextRecoveryAtUtc);
        Assert.NotNull(session.RecoveryTerminalAtUtc);
        Assert.Empty(await db.StoredFiles.ToArrayAsync());

        var recoveryStorage = new ThrowingStorage();
        var processor = new UploadCommitRecoveryProcessor(
            db,
            CreateService(db, recoveryStorage),
            TimeProvider.System,
            NullLogger<UploadCommitRecoveryProcessor>.Instance);
        Assert.Equal(new UploadCommitRecoveryResult(0, 0, 0, 1), await processor.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, recoveryStorage.Attempts);
    }

    [Fact]
    public async Task ExistingStoredFileForCommittingIntent_CompletesWithoutRepeatingStorage()
    {
        await using var db = CreateDbContext();
        var session = CreateSession("ups_existing_file");
        session.BeginCommit("cmt_existing_file", null, DateTimeOffset.UtcNow);
        db.UploadSessions.Add(session);
        db.StoredFiles.Add(StoredFileRecord.Create(
            session.FileId,
            session.OrganizationId,
            session.EnvironmentId,
            session.OwnerService,
            session.OwnerType,
            session.OwnerId,
            session.FilePurpose,
            session.FileName,
            session.ContentType,
            session.ExpectedSizeBytes,
            $"sha256:{new string('a', 64)}",
            session.ObjectKey,
            Nerv.IIP.FileStorage.Domain.FileStorageFileStatus.Available,
            session.CreatedAtUtc,
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var storage = new ThrowingStorage();
        var service = CreateService(db, storage);

        var result = await service.CompleteUploadSessionAsync(
            session.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "attachment", null, 5),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal(0, storage.Attempts);
        Assert.Equal(UploadSessionState.Completed, (await db.UploadSessions.SingleAsync()).State);
        Assert.Single(await db.StoredFiles.ToArrayAsync());
    }

    [Fact]
    public async Task ActiveStorageExecution_RenewsLeaseAndPreventsSecondOwnerAfterOriginalExpiry()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = new InMemoryDatabaseRoot();
        var databaseName = $"filestorage-lease-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        await using var firstDb = new ApplicationDbContext(options);
        await using var secondDb = new ApplicationDbContext(options);
        var leaseManager = new UploadCommitExecutionLeaseManager(
            new TestDbContextFactory(options),
            clock,
            NullLogger<UploadCommitExecutionLeaseManager>.Instance);
        var session = CreateSession("ups_lease");
        session.BeginCommit("cmt_lease", null, clock.GetUtcNow());
        firstDb.UploadSessions.Add(session);
        await firstDb.SaveChangesAsync();
        var blockingStorage = new BlockingStorage();
        var firstService = CreateService(firstDb, blockingStorage, clock, leaseManager);
        var secondService = CreateService(
            secondDb,
            new VerifiedStorage($"sha256:{new string('a', 64)}"),
            clock,
            leaseManager);

        var first = firstService.CompleteUploadSessionAsync(
            session.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "attachment", null, 5),
            CancellationToken.None);
        await blockingStorage.Entered;
        for (var minute = 0; minute < 6; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await WaitUntilAsync(async () =>
            {
                await using var evidenceDb = new ApplicationDbContext(options);
                return await evidenceDb.UploadSessions
                    .AsNoTracking()
                    .AnyAsync(x => x.UploadSessionId == session.UploadSessionId
                        && x.ExecutionLeaseUntilUtc > clock.GetUtcNow());
            });
        }

        var second = await secondService.CompleteUploadSessionAsync(
            session.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "attachment", null, 5),
            CancellationToken.None);
        blockingStorage.Release();
        var firstResult = await first;

        Assert.Equal(StatusCodes.Status409Conflict, second.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, firstResult.StatusCode);
        Assert.Single(await firstDb.StoredFiles.ToArrayAsync());
    }

    [Fact]
    public async Task StorageExecutionThatLosesOwnership_ReturnsConflictWithoutConcurrencyException()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = new InMemoryDatabaseRoot();
        var databaseName = $"filestorage-lost-owner-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        await using var ownerDb = new ApplicationDbContext(options);
        var session = CreateSession("ups_lost_owner");
        session.BeginCommit("cmt_lost_owner", null, clock.GetUtcNow());
        ownerDb.UploadSessions.Add(session);
        await ownerDb.SaveChangesAsync();
        var storage = new BlockingStorage();
        var leaseManager = new UploadCommitExecutionLeaseManager(
            new TestDbContextFactory(options),
            clock,
            NullLogger<UploadCommitExecutionLeaseManager>.Instance);
        var service = CreateService(ownerDb, storage, clock, leaseManager);
        var completion = service.CompleteUploadSessionAsync(
            session.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "attachment", null, 5),
            CancellationToken.None);
        await storage.Entered;
        await using (var stealingDb = new ApplicationDbContext(options))
        {
            var stolen = await stealingDb.UploadSessions.SingleAsync();
            stolen.ClaimExecution("wrk_stolen", clock.GetUtcNow().AddMinutes(5));
            await stealingDb.SaveChangesAsync();
        }

        storage.Release();
        var result = await completion;

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        await using var evidenceDb = new ApplicationDbContext(options);
        Assert.Empty(await evidenceDb.StoredFiles.ToArrayAsync());
        Assert.Equal(UploadSessionState.Committing, (await evidenceDb.UploadSessions.SingleAsync()).State);
    }

    [Fact]
    public async Task UppercaseExpectedChecksumDifferentFromFinalEvidence_FailsClosedWithCanonicalIntent()
    {
        await using var db = CreateDbContext();
        var expected = $"SHA256:{new string('A', 64)}";
        var service = CreateService(db, new VerifiedStorage($"sha256:{new string('b', 64)}"));
        var created = (await service.CreateUploadSessionAsync(CreateRequest(expected), CancellationToken.None)).Value!;

        var result = await service.CompleteUploadSessionAsync(
            created.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", expected, 5),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        var session = await db.UploadSessions.SingleAsync();
        Assert.Equal($"sha256:{new string('a', 64)}", session.CommitChecksum);
        Assert.Equal(UploadSessionState.Committing, session.State);
        Assert.Empty(await db.StoredFiles.ToArrayAsync());
    }

    [Fact]
    public async Task RecoveryAfterEarlierStorageActionStarted_RemainsCommittingAndRejectsPatchMutation()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"filestorage-safe-reopen-{Guid.NewGuid():N}";
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddSingleton<UploadSessionGateRegistry>();
        services.AddSingleton<IUploadSessionMutationGate, UploadSessionMutationGate>();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = CreateSession("ups_safe_reopen");
            session.BeginCommit("cmt_safe_reopen", null, clock.GetUtcNow());
            session.MarkStorageActionStarted(clock.GetUtcNow());
            session.RecordRecoveryFailure("process-stopped", clock.GetUtcNow());
            db.UploadSessions.Add(session);
            await db.SaveChangesAsync();

            var service = new PostgreSqlFileStorageService(
                db,
                new ServerProxyUploadProvider(),
                configuration: FileStorageTestConfiguration.Default,
                timeProvider: clock,
                gateRegistry: scope.ServiceProvider.GetRequiredService<UploadSessionGateRegistry>());
            var processor = new UploadCommitRecoveryProcessor(
                db,
                service,
                clock,
                NullLogger<UploadCommitRecoveryProcessor>.Instance);

            Assert.Equal(new UploadCommitRecoveryResult(1, 0, 1), await processor.RunOnceAsync(CancellationToken.None));
            db.ChangeTracker.Clear();
            var retained = await db.UploadSessions.SingleAsync();
            Assert.Equal(UploadSessionState.Committing, retained.State);
            Assert.Equal("cmt_safe_reopen", retained.CommitId);
            Assert.NotNull(retained.CommittingAtUtc);
            Assert.NotNull(retained.StorageActionStartedAtUtc);
        }

        var patchMutationRan = false;
        var mutation = await provider.GetRequiredService<IUploadSessionMutationGate>().ExecutePatchMutationAsync(
            "ups_safe_reopen",
            _ =>
            {
                patchMutationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(UploadSessionMutationResult.NotOpen, mutation);
        Assert.False(patchMutationRan);
    }

    [Fact]
    public async Task MayHaveFinalFailure_RemainsCommittingAndRecoveryContinuesSameIntentIntoTx2()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = CreateDbContext();
        var unavailable = CreateService(db, new MayHaveFinalStorage(), clock);
        var created = (await unavailable.CreateUploadSessionAsync(CreateRequest(null), CancellationToken.None)).Value!;
        var first = await unavailable.CompleteUploadSessionAsync(
            created.UploadSessionId,
            new CompleteUploadSessionRequest("org-001", "prod", "application-package", null, 5),
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, first.StatusCode);
        var failedSession = await db.UploadSessions.SingleAsync();
        var commitId = failedSession.CommitId;
        Assert.Equal(UploadSessionState.Committing, failedSession.State);
        Assert.NotNull(failedSession.StorageActionStartedAtUtc);
        clock.Advance(TimeSpan.FromSeconds(2));

        var recoveringService = CreateService(db, new VerifiedStorage($"sha256:{new string('c', 64)}"), clock);
        var processor = new UploadCommitRecoveryProcessor(
            db,
            recoveringService,
            clock,
            NullLogger<UploadCommitRecoveryProcessor>.Instance);
        var recovered = await processor.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new UploadCommitRecoveryResult(1, 1, 0), recovered);
        var session = await db.UploadSessions.SingleAsync();
        Assert.Equal(commitId, session.CommitId);
        Assert.Equal(UploadSessionState.Completed, session.State);
        Assert.Single(await db.StoredFiles.ToArrayAsync());
    }

    private static PostgreSqlFileStorageService CreateService(
        ApplicationDbContext db,
        IUploadCommitStorage storage,
        TimeProvider? timeProvider = null,
        UploadCommitExecutionLeaseManager? executionLeaseManager = null) =>
        new(
            db,
            new ServerProxyUploadProvider(),
            configuration: FileStorageTestConfiguration.Default,
            timeProvider: timeProvider,
            commitStorage: storage,
            executionLeaseManager: executionLeaseManager);

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"filestorage-protocol-{Guid.NewGuid():N}")
            .Options);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("等待提交执行租约续租超时。");
    }

    private static UploadSessionRecord CreateSession(string uploadSessionId) =>
        UploadSessionRecord.Create(
            uploadSessionId,
            $"file_{uploadSessionId}",
            "org-001",
            "prod",
            "AppHub",
            "Attachment",
            "owner-1",
            "attachment",
            "a.txt",
            "text/plain",
            5,
            null,
            $"org-001/file_{uploadSessionId}",
            "tus",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(15));

    private static CreateUploadSessionRequest CreateRequest(string? checksum) =>
        new(
            "org-001",
            "prod",
            new OwnerReference("AppHub", "ApplicationPackage", "app-42"),
            "application-package",
            "demo.zip",
            "application/zip",
            5,
            checksum);

    private sealed class VerifiedStorage(string checksum) : IUploadCommitStorage
    {
        public Task<UploadCommitStorageResult> CommitAsync(
            UploadCommitIntent intent,
            CancellationToken cancellationToken) =>
            Task.FromResult(UploadCommitStorageResult.Verified(intent.ExpectedSizeBytes, checksum));
    }

    private sealed class MayHaveFinalStorage : IUploadCommitStorage
    {
        public Task<UploadCommitStorageResult> CommitAsync(
            UploadCommitIntent intent,
            CancellationToken cancellationToken) =>
            Task.FromResult(UploadCommitStorageResult.RetryableUnavailable());
    }

    private sealed class ThrowingStorage : IUploadCommitStorage
    {
        public int Attempts { get; private set; }

        public Task<UploadCommitStorageResult> CommitAsync(
            UploadCommitIntent intent,
            CancellationToken cancellationToken)
        {
            Attempts++;
            throw new InvalidOperationException("测试不应调用存储 seam。");
        }
    }

    private sealed class BlockingStorage : IUploadCommitStorage
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
                $"sha256:{new string('a', 64)}");
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}
