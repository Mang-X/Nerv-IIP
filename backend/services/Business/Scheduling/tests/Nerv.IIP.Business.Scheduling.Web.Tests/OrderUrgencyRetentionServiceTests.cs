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
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");

        var result = await service.RunScopeAsync(Policy(), CancellationToken.None);
        var replay = await service.RunScopeAsync(Policy(), CancellationToken.None);

        Assert.Equal(1, result.ArchivedSnapshots);
        Assert.Equal(0, result.SourceDeletedSnapshots);
        Assert.Equal(0, replay.ArchivedSnapshots);
        Assert.Equal(1, archive.PutCount);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
    }

    [Fact]
    public async Task Source_delete_revalidates_the_current_online_retention_window()
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

        var result = await service.RunScopeAsync(
            Policy(sourceDeletion: Authorization(), onlineRetention: TimeSpan.FromDays(365)),
            CancellationToken.None);

        Assert.Equal(0, result.SourceDeletedSnapshots);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus,
            Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync()).Status);
    }

    [Fact]
    public async Task Source_delete_preserves_an_archived_row_that_became_the_latest_generation()
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
        var latest = await db.OrderUrgencySnapshots.SingleAsync(x => x.BusinessPriorityRevision == 2);
        db.OrderUrgencySnapshots.Remove(latest);
        await db.SaveChangesAsync();

        var result = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.Equal(0, result.SourceDeletedSnapshots);
        Assert.Single(await db.OrderUrgencySnapshots.ToArrayAsync());
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus,
            Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync()).Status);
    }

    [Fact]
    public async Task Archive_batch_shrinks_by_bytes_and_never_crosses_the_atomic_single_put_limit()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var orderId in new[] { "WO-001", "WO-002" })
        {
            Seed(db, "org-001", "prod", orderId, Now.AddDays(-200), 1, new string('x', 2_048));
            Seed(db, "org-001", "prod", orderId, Now.AddDays(-1), 2);
        }
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");

        var result = await service.RunScopeAsync(Policy(maxArchiveBytes: 3_500), CancellationToken.None);

        Assert.Equal(1, result.ArchivedSnapshots);
        Assert.InRange(archive.LastPutSize, 1, 3_500);
        Assert.Equal(1, archive.PutCount);
    }

    [Fact]
    public async Task Oversized_individual_snapshot_records_failure_without_calling_object_storage()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1, new string('x', 6_000));
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        Seed(db, "org-001", "prod", "WO-002", Now.AddDays(-199), 1);
        Seed(db, "org-001", "prod", "WO-002", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");

        var result = await service.RunScopeAsync(Policy(maxArchiveBytes: 3_000), CancellationToken.None);
        var quarantined = Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync());
        var retry = await service.RunScopeAsync(Policy(maxArchiveBytes: 3_000), CancellationToken.None);

        Assert.Equal(1, result.Failures);
        Assert.Equal(1, retry.ArchivedSnapshots);
        Assert.Equal(1, archive.PutCount);
        Assert.Equal(4, await db.OrderUrgencySnapshots.CountAsync());
        var batches = await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, batches.Length);
        var oversized = Assert.Single(batches, batch => batch.ErrorCode == "archive-payload-too-large");
        Assert.Equal(quarantined.Id, oversized.Id);
        Assert.Equal(quarantined.AttemptCount, oversized.AttemptCount);
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
        var batch = Assert.Single(await db.OrderUrgencyArchiveBatches.ToArrayAsync());
        Assert.Equal("failed", batch.Status);
        Assert.Equal("archive-evidence-mismatch", batch.ErrorCode);
    }

    [Fact]
    public async Task Failed_fixed_intent_retries_the_same_snapshot_set_after_batch_configuration_changes()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var orderId in new[] { "WO-001", "WO-002" })
        {
            Seed(db, "org-001", "prod", orderId, Now.AddDays(-200), 1);
            Seed(db, "org-001", "prod", orderId, Now.AddDays(-1), 2);
        }
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore { ReturnIncompleteEvidence = true };
        var service = new OrderUrgencyRetentionService(db, archive, new MutableTimeProvider(Now), "worker-a");
        await service.RunScopeAsync(Policy(batchSize: 1), CancellationToken.None);
        var failed = Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync());
        var fixedIds = failed.SnapshotIdsJson;
        archive.ReturnIncompleteEvidence = false;

        var retry = await service.RunScopeAsync(Policy(batchSize: 100), CancellationToken.None);

        Assert.Equal(1, retry.ArchivedSnapshots);
        Assert.Equal(2, archive.PutCount);
        var retried = Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync());
        Assert.Equal(failed.BatchId, retried.BatchId);
        Assert.Equal(fixedIds, retried.SnapshotIdsJson);
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus, retried.Status);
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
    public async Task Different_object_version_with_identical_content_never_authorizes_source_deletion()
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
        archive.ResponseVersionOverride = "different-version";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None));

        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus,
            Assert.Single(await db.OrderUrgencyArchiveBatches.ToArrayAsync()).Status);
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
    public async Task Restored_rows_archive_as_a_new_generation_without_regressing_the_original_batch()
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
        var original = Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync());
        await service.RestoreAsync("org-001", "prod", original.BatchId, "user:operator", "Audit request", CancellationToken.None);

        var rerun = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.Equal(1, rerun.ArchivedSnapshots);
        Assert.Equal(1, rerun.SourceDeletedSnapshots);
        Assert.Equal(2, archive.PutCount);
        var batches = await db.OrderUrgencyArchiveBatches.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToArrayAsync();
        Assert.Equal(2, batches.Length);
        Assert.NotEqual(batches[0].BatchId, batches[1].BatchId);
        Assert.All(batches, batch => Assert.Equal("source-deleted", batch.Status));
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
    public async Task Lease_is_renewed_after_slow_archive_revalidation_before_source_deletion()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var clock = new MutableTimeProvider(Now);
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(db, archive, clock, "worker-a");
        await service.RunScopeAsync(Policy(), CancellationToken.None);
        var revisionBeforeDelete = Assert.Single(
            await db.OrderUrgencyRetentionLeases.AsNoTracking().ToArrayAsync()).Revision;
        archive.AfterGet = () => clock.UtcNow = Now.AddMinutes(9);

        var result = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.Equal(1, result.SourceDeletedSnapshots);
        var lease = Assert.Single(await db.OrderUrgencyRetentionLeases.AsNoTracking().ToArrayAsync());
        Assert.Equal(revisionBeforeDelete + 4, lease.Revision);
    }

    [Fact]
    public async Task Lease_takeover_before_source_commit_discards_the_tracked_deletion()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var clock = new MutableTimeProvider(Now);
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(
            db,
            archive,
            clock,
            "worker-a",
            _ =>
            {
                clock.UtcNow = Now.AddMinutes(11);
                using var competingScope = provider.CreateScope();
                var competingDb = competingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var lease = competingDb.OrderUrgencyRetentionLeases.Single();
                Assert.True(lease.ExpiresAtUtc <= clock.UtcNow);
                lease.Acquire("worker-b", clock.UtcNow, clock.UtcNow.AddMinutes(10));
                competingDb.SaveChanges();
                return Task.CompletedTask;
            });
        await service.RunScopeAsync(Policy(), CancellationToken.None);
        archive.AfterGet = () => clock.UtcNow = Now.AddMinutes(9);

        var result = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.Equal(0, result.SourceDeletedSnapshots);
        await using var assertionScope = provider.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await assertionDb.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus,
            Assert.Single(await assertionDb.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync()).Status);
        Assert.Equal("worker-b",
            Assert.Single(await assertionDb.OrderUrgencyRetentionLeases.AsNoTracking().ToArrayAsync()).OwnerId);
    }

    [Fact]
    public async Task Naturally_expired_lease_without_takeover_discards_the_tracked_source_deletion()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var clock = new MutableTimeProvider(Now);
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(
            db,
            archive,
            clock,
            "worker-a",
            _ =>
            {
                clock.UtcNow = Now.AddMinutes(11);
                return Task.CompletedTask;
            });
        await service.RunScopeAsync(Policy(), CancellationToken.None);

        var result = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.Equal(0, result.SourceDeletedSnapshots);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus,
            Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync()).Status);
    }

    [Fact]
    public async Task Authorization_expiring_before_source_commit_discards_the_tracked_deletion()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var clock = new MutableTimeProvider(Now);
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(
            db,
            archive,
            clock,
            "worker-a",
            _ =>
            {
                clock.UtcNow = Now.AddMinutes(2);
                return Task.CompletedTask;
            });
        await service.RunScopeAsync(Policy(), CancellationToken.None);
        var expiringAuthorization = new OrderUrgencyDeletionAuthorization(
            "CAB-42", "user:compliance", "Approved retention enforcement", Now.AddHours(-1), Now.AddMinutes(1));

        var result = await service.RunScopeAsync(
            Policy(sourceDeletion: expiringAuthorization),
            CancellationToken.None);

        Assert.Equal(0, result.SourceDeletedSnapshots);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus,
            Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync()).Status);
    }

    [Fact]
    public async Task Commit_precheck_exception_never_leaks_a_tracked_delete_through_lease_release()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(
            db,
            archive,
            new MutableTimeProvider(Now),
            "worker-a",
            _ =>
            {
                var oldSnapshot = db.OrderUrgencySnapshots.Local.Single(x => x.BusinessPriorityRevision == 1);
                db.OrderUrgencySnapshots.Remove(oldSnapshot);
                throw new InvalidOperationException("Injected commit precheck failure.");
            });
        await service.RunScopeAsync(Policy(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None));

        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus,
            Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync()).Status);
    }

    [Fact]
    public async Task Live_lease_read_crossing_expiry_uses_the_post_read_commit_time_and_preserves_source()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-200), 1);
        Seed(db, "org-001", "prod", "WO-001", Now.AddDays(-1), 2);
        await db.SaveChangesAsync();
        var clock = new MutableTimeProvider(Now);
        var archive = new FakeArchiveStore();
        var service = new OrderUrgencyRetentionService(
            db,
            archive,
            clock,
            "worker-a",
            afterSourceDeleteLeaseRead: _ =>
            {
                clock.UtcNow = Now.AddMinutes(11);
                return Task.CompletedTask;
            });
        await service.RunScopeAsync(Policy(), CancellationToken.None);

        var result = await service.RunScopeAsync(Policy(sourceDeletion: Authorization()), CancellationToken.None);

        Assert.Equal(0, result.SourceDeletedSnapshots);
        Assert.Equal(2, await db.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(OrderUrgencyArchiveBatch.ArchivedStatus,
            Assert.Single(await db.OrderUrgencyArchiveBatches.AsNoTracking().ToArrayAsync()).Status);
    }

    [Fact]
    public void Terminal_archive_batch_state_cannot_regress()
    {
        var batch = OrderUrgencyArchiveBatch.Create(
            "batch-001", "org-001", "prod", "[]", 1, Now.AddDays(-200), Now.AddDays(-200), Now);
        batch.MarkArchived("key", "version-1", new string('a', 64), 10, Now);
        batch.MarkSourceDeleted("CAB-42", "user:compliance", "Approved", Now);

        Assert.Throws<InvalidOperationException>(() =>
            batch.MarkArchived("key", "version-2", new string('b', 64), 10, Now));
        Assert.Throws<InvalidOperationException>(() => batch.MarkFailed("archive-put-failed", "late failure"));
        Assert.Equal(OrderUrgencyArchiveBatch.SourceDeletedStatus, batch.Status);
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
        OrderUrgencyDeletionAuthorization? sourceDeletion = null,
        int maxArchiveBytes = VersionedArchiveLimits.MaximumConditionallyWritableBytes,
        int batchSize = 100,
        TimeSpan? onlineRetention = null) =>
        new("org-001", "prod", onlineRetention ?? TimeSpan.FromDays(180), TimeSpan.FromDays(1095), batchSize, legalHold, sourceDeletion, null, maxArchiveBytes);

    private static OrderUrgencyDeletionAuthorization Authorization() =>
        new("CAB-42", "user:compliance", "Approved retention enforcement", Now.AddHours(-1), Now.AddHours(1));

    private static void Seed(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string orderId,
        DateTimeOffset at,
        long revision,
        string? resultJsonOverride = null)
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
            "order-urgency-v1", $"fingerprint-{revision}", revision, at, at, resultJsonOverride ?? resultJson));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"urgency-retention-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private sealed class FakeArchiveStore : IOrderUrgencyArchiveStore
    {
        private readonly Dictionary<string, byte[]> content = new(StringComparer.Ordinal);
        public int PutCount { get; private set; }
        public long LastPutSize { get; private set; }
        public int DeleteCount { get; private set; }
        public bool ReturnIncompleteEvidence { get; set; }
        public bool ThrowOnGet { get; set; }
        public Action? AfterGet { get; set; }
        public string? ResponseVersionOverride { get; set; }

        public Task<VersionedArchiveEvidence> PutAsync(PutVersionedArchiveRequest request, CancellationToken cancellationToken)
        {
            PutCount++;
            var bytes = Convert.FromBase64String(request.ContentBase64);
            LastPutSize = bytes.LongLength;
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
            AfterGet?.Invoke();
            return Task.FromResult(new GetVersionedArchiveResponse(
                new VersionedArchiveEvidence(
                    request.ObjectKey,
                    ResponseVersionOverride ?? request.VersionId,
                    request.Sha256,
                    request.SizeBytes,
                    Now),
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
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
