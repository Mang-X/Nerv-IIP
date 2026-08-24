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

        var mutationGate = provider.GetRequiredService<IUploadSessionMutationGate>();
        var registry = provider.GetRequiredService<UploadSessionGateRegistry>();
        var firstMutationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstMutation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMutation = mutationGate.ExecutePatchMutationAsync(
            "ups_gate",
            async _ =>
            {
                firstMutationEntered.SetResult();
                await releaseFirstMutation.Task;
            },
            CancellationToken.None);
        var firstEdge = await Task.WhenAny(firstMutationEntered.Task, firstMutation);
        Assert.Same(firstMutationEntered.Task, firstEdge);

        var tx1GateTask = registry.EnterPatchCommitAsync("ups_gate", CancellationToken.None).AsTask();
        Assert.False(tx1GateTask.IsCompleted);
        releaseFirstMutation.SetResult();
        Assert.Equal(UploadSessionMutationResult.Mutated, await firstMutation);
        await using var tx1Gate = await tx1GateTask;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = await db.UploadSessions.SingleAsync();
            session.BeginCommit("cmt_gate", null, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var secondMutationRan = false;
        var waitingMutation = mutationGate.ExecutePatchMutationAsync(
            "ups_gate",
            _ =>
            {
                secondMutationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);
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
        Assert.Equal(UploadSessionState.Committing, (await db.UploadSessions.SingleAsync()).State);
        Assert.Empty(await db.StoredFiles.ToArrayAsync());
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
    public async Task RecoveryWithProofNoFinalActionStarted_ReopensSessionAndAllowsPatchMutation()
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
            var reopened = await db.UploadSessions.SingleAsync();
            Assert.Equal(UploadSessionState.Open, reopened.State);
            Assert.Null(reopened.CommitId);
            Assert.Null(reopened.CommitChecksum);
            Assert.Null(reopened.CommittingAtUtc);
            Assert.Null(reopened.StorageActionStartedAtUtc);
            Assert.Null(reopened.ExecutionOwnerId);
            Assert.Null(reopened.ExecutionLeaseUntilUtc);
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

        Assert.Equal(UploadSessionMutationResult.Mutated, mutation);
        Assert.True(patchMutationRan);
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
        TimeProvider? timeProvider = null) =>
        new(
            db,
            new ServerProxyUploadProvider(),
            configuration: FileStorageTestConfiguration.Default,
            timeProvider: timeProvider,
            commitStorage: storage);

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"filestorage-protocol-{Guid.NewGuid():N}")
            .Options);

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
}
