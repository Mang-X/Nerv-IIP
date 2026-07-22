using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Infrastructure.Urgency;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class OrderUrgencyRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Archives_only_old_non_latest_rows_in_the_requested_scope()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        Seed(db, "org-002", "prod", "WO-002", Now.AddDays(-200), 1);
        Seed(db, "org-002", "prod", "WO-002", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");

        var result = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.Equal(1, result.ArchivedSnapshots);
        Assert.Equal(1, result.SourceDeletedSnapshots);
        Assert.Single(await db.OrderUrgencySnapshots.Where(x => x.OrganizationId == "org-001").ToArrayAsync());
        Assert.Equal(3, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Single(await db.OrderUrgencyArchiveBatches.ToArrayAsync());
    }

    [Fact]
    public async Task Missing_authorization_archives_but_never_deletes_source()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var service = new OrderUrgencyRetentionService(db, new FakeArchiveStore(), new MutableTimeProvider(Now), "worker-a");

        var result = await service.RunScopeAsync(Policy(), CancellationToken.None);

        Assert.Equal(1, result.ArchivedSnapshots);
        Assert.Equal(0, result.SourceDeletedSnapshots);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
    }

    [Fact]
    public async Task Archive_failure_or_incomplete_evidence_never_deletes_source()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore { ReturnIncompleteEvidence = true };
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");

        var result = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.Equal(0, result.SourceDeletedSnapshots);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal("failed", Assert.Single(await db.OrderUrgencyArchiveBatches.ToArrayAsync()).Status);
    }

    [Fact]
    public async Task Previously_archived_batch_is_not_deleted_when_exact_version_is_unavailable_at_delete_time()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");
        await service.RunScopeAsync(Policy(), CancellationToken.None);
        archive.ThrowOnGet = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None));

        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal("archived", Assert.Single(await db.OrderUrgencyArchiveBatches.ToArrayAsync()).Status);
    }

    [Fact]
    public async Task Legal_hold_blocks_archive_and_delete_and_repeat_is_idempotent()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");

        var held = await service.RunScopeAsync(Policy(legalHold: true, sourceDeletion: Authorization()), CancellationToken.None);
        var first = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);
        var replay = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.True(held.LegalHoldActive);
        Assert.Equal(1, first.SourceDeletedSnapshots);
        Assert.Equal(0, replay.SourceDeletedSnapshots);
        Assert.Equal(1, archive.PutCount);
    }

    [Fact]
    public async Task Restore_rehydrates_missing_rows_idempotently_from_exact_archive_evidence()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");
        await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);
        var batch = Assert.Single(await db.OrderUrgencyArchiveBatches.ToArrayAsync());

        var first = await service.RestoreAsync("org-001", "prod", batch.BatchId, "user:operator", "Audit request", CancellationToken.None);
        var replay = await service.RestoreAsync("org-001", "prod", batch.BatchId, "user:operator", "Audit retry", CancellationToken.None);

        Assert.Equal(1, first.RestoredSnapshots);
        Assert.Equal(0, replay.RestoredSnapshots);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(2, await db.OrderUrgencyRestoreAudits.CountAsync());
    }

    [Fact]
    public async Task Exact_online_boundary_is_retained_and_an_active_database_lease_excludes_another_worker()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-180), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        db.OrderUrgencyRetentionLeases.Add(new OrderUrgencyRetentionLease(
            "org-001", "prod", "worker-a", Now, Now.AddMinutes(5)));
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-b");

        var result = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.False(result.LeaseAcquired);
        Assert.Equal(0, archive.PutCount);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
    }

    [Fact]
    public async Task Exact_archive_version_expires_only_after_three_years_with_separate_authorization()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");
        var source = Authorization();
        var archiveAuthorization = new OrderUrgencyDeletionAuthorization(
            "CAB-43", "user:records", "Approved total-retention expiry", Now.AddHours(-1), Now.AddHours(1));
        var policy = new OrderUrgencyRetentionScope(
            "org-001", "prod", TimeSpan.FromDays(180), TimeSpan.FromDays(1095), 100, false, source, archiveAuthorization);

        await service.RunScopeAsync(policy, CancellationToken.None);
        var expiry = await service.RunScopeAsync(policy, CancellationToken.None);

        Assert.Equal(1, expiry.ArchiveDeletedBatches);
        Assert.Equal(1, archive.DeleteCount);
        var batch = Assert.Single(await db.OrderUrgencyArchiveBatches.ToArrayAsync());
        Assert.Equal("archive-deleted", batch.Status);
        Assert.Equal("CAB-42", batch.SourceDeletionAuthorizationReference);
        Assert.Equal("CAB-43", batch.ArchiveDeletionAuthorizationReference);
    }

    private static OrderUrgencyRetentionScope Policy(
        bool legalHold = false,
        OrderUrgencyDeletionAuthorization? sourceDeletion = null) =>
        new("org-001", "prod", TimeSpan.FromDays(180), TimeSpan.FromDays(1095), 100, legalHold, sourceDeletion, null);

    private static OrderUrgencyDeletionAuthorization Authorization() =>
        new("CAB-42", "user:compliance", "Approved retention enforcement", Now.AddHours(-1), Now.AddHours(1));

    private static void Seed(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string orderId,
        DateTimeOffset at,
        long revision)
    {
        var resultJson = $$"""
        {
          "orderId": "{{orderId}}",
          "businessReference": "SO-{{orderId}}",
          "level": "normal",
          "businessPriority": {
            "level": "p2",
            "source": "test",
            "reason": "test",
            "setAtUtc": "2026-01-01T00:00:00Z",
            "expiresAtUtc": null,
            "revision": {{revision}},
            "reasonCodes": []
          },
          "timeCriticality": {
            "level": "normal",
            "criticalRatio": null,
            "slackHours": null,
            "expectedDelayHours": 0,
            "dueUtc": null,
            "estimatedCompletionUtc": "2026-01-01T00:00:00Z",
            "remainingCycleHours": 0,
            "reasonCodes": []
          },
          "executionRisk": {
            "level": "normal",
            "isSourceMissing": false,
            "isSourceStale": false,
            "factsObservedAtUtc": "2026-01-01T00:00:00Z",
            "reasonCodes": [],
            "facts": []
          },
          "calculatedAtUtc": "{{at:O}}",
          "modelVersion": "order-urgency-v1",
          "inputFingerprint": "fingerprint-{{revision}}"
        }
        """;
        db.OrderUrgencySnapshots.Add(new OrderUrgencySnapshot(
            organizationId, environmentId, orderId, $"SO-{orderId}", OrderUrgencyLevel.Normal,
            "order-urgency-v1", $"fingerprint-{revision}", revision, at, at, resultJson));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"urgency-retention-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private sealed class FakeArchiveStore : IOrderUrgencyArchiveStore
    {
        private readonly Dictionary<string, byte[]> content = new(StringComparer.Ordinal);
        public int PutCount { get; private set; }
        public int DeleteCount { get; private set; }
        public bool ReturnIncompleteEvidence { get; init; }
        public bool ThrowOnGet { get; set; }

        public Task<VersionedArchiveEvidence> PutAsync(PutVersionedArchiveRequest request, CancellationToken cancellationToken)
        {
            PutCount++;
            var bytes = Convert.FromBase64String(request.ContentBase64);
            var key = $"compliance-archives/{request.OrganizationId}/{request.EnvironmentId}/{request.ArchiveKind}/{request.BatchId}.json";
            content[key] = bytes;
            return Task.FromResult(new VersionedArchiveEvidence(
                key,
                ReturnIncompleteEvidence ? string.Empty : $"version-{PutCount}",
                request.Sha256,
                bytes.LongLength,
                Now));
        }

        public Task<GetVersionedArchiveResponse> GetAsync(GetVersionedArchiveRequest request, CancellationToken cancellationToken)
        {
            if (ThrowOnGet) throw new InvalidOperationException("Exact archive version is unavailable.");
            var bytes = content[request.ObjectKey];
            return Task.FromResult(new GetVersionedArchiveResponse(
                new VersionedArchiveEvidence(request.ObjectKey, request.VersionId, request.Sha256, request.SizeBytes, Now),
                Convert.ToBase64String(bytes)));
        }

        public Task DeleteAsync(DeleteVersionedArchiveRequest request, CancellationToken cancellationToken)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
