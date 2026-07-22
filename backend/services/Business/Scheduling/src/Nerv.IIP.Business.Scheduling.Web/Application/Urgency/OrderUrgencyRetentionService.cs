using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Infrastructure.Urgency;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

public interface IOrderUrgencyArchiveStore
{
    Task<VersionedArchiveEvidence> PutAsync(PutVersionedArchiveRequest request, CancellationToken cancellationToken);
    Task<GetVersionedArchiveResponse> GetAsync(GetVersionedArchiveRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(DeleteVersionedArchiveRequest request, CancellationToken cancellationToken);
}

public sealed record OrderUrgencyRetentionRunResult(
    int ArchivedSnapshots,
    int SourceDeletedSnapshots,
    int ArchiveDeletedBatches,
    int Failures,
    bool LegalHoldActive,
    bool LeaseAcquired,
    int EligibleSnapshots,
    double OldestEligibleAgeSeconds);

public sealed record OrderUrgencyRestoreResult(int RestoredSnapshots, string BatchId, string ObjectVersionId);

public sealed class OrderUrgencyRetentionOperationException(
    string errorCode,
    string message,
    Exception innerException) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class OrderUrgencyRetentionService(
    ApplicationDbContext dbContext,
    IOrderUrgencyArchiveStore archiveStore,
    TimeProvider timeProvider,
    string workerId,
    Func<CancellationToken, Task>? beforeSourceDeleteCommit = null,
    Func<CancellationToken, Task>? afterSourceDeleteLeaseRead = null)
{
    private static readonly JsonSerializerOptions ArchiveJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LeaseRenewalThreshold = TimeSpan.FromMinutes(2);
    private const int RecoveryBatchLimit = 10;

    public async Task<OrderUrgencyRetentionRunResult> RunScopeAsync(
        OrderUrgencyRetentionScope scope,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (scope.LegalHoldActive)
        {
            return new OrderUrgencyRetentionRunResult(0, 0, 0, 0, true, false, 0, 0);
        }
        if (!await TryAcquireLeaseAsync(scope, now, cancellationToken))
        {
            return new OrderUrgencyRetentionRunResult(0, 0, 0, 0, false, false, 0, 0);
        }

        try
        {
            var archiveDeleted = await DeleteExpiredArchivesAsync(scope, cancellationToken);
            var resumedDeletes = await ResumeSourceDeletesAsync(scope, cancellationToken);
            var cutoff = now - scope.OnlineRetention;
            var candidateQuery = dbContext.OrderUrgencySnapshots.AsNoTracking()
                .Where(snapshot =>
                    snapshot.OrganizationId == scope.OrganizationId &&
                    snapshot.EnvironmentId == scope.EnvironmentId &&
                    snapshot.CalculatedAtUtc < cutoff &&
                    dbContext.OrderUrgencySnapshots.Any(newer =>
                        newer.OrganizationId == snapshot.OrganizationId &&
                        newer.EnvironmentId == snapshot.EnvironmentId &&
                        newer.OrderId == snapshot.OrderId &&
                        (newer.CalculatedAtUtc > snapshot.CalculatedAtUtc ||
                         (newer.CalculatedAtUtc == snapshot.CalculatedAtUtc &&
                          newer.BusinessPriorityRevision > snapshot.BusinessPriorityRevision))));
            var eligibleSnapshots = await candidateQuery.CountAsync(cancellationToken);
            var oldestEligibleAtUtc = eligibleSnapshots == 0
                ? (DateTimeOffset?)null
                : await candidateQuery.MinAsync(x => x.CalculatedAtUtc, cancellationToken);
            var oldestEligibleAgeSeconds = oldestEligibleAtUtc.HasValue
                ? Math.Max(0, (now - oldestEligibleAtUtc.Value).TotalSeconds)
                : 0;
            var batch = await dbContext.OrderUrgencyArchiveBatches
                .Where(
                x => x.OrganizationId == scope.OrganizationId &&
                     x.EnvironmentId == scope.EnvironmentId &&
                     (x.Status == OrderUrgencyArchiveBatch.PendingStatus ||
                      (x.Status == OrderUrgencyArchiveBatch.FailedStatus &&
                       x.ErrorCode != "archive-payload-too-large" &&
                       x.ErrorCode != "archive-source-incomplete" &&
                       x.ErrorCode != "archive-intent-mismatch")))
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var retryingFixedIntent = batch is not null;
            OrderUrgencySnapshot[] candidates;
            ArchiveMaterial archiveMaterial;
            if (batch is not null)
            {
                candidates = await LoadFixedIntentCandidatesAsync(batch, scope, cancellationToken);
                if (candidates.Length != batch.SnapshotCount)
                {
                    if (batch.ErrorCode != "archive-source-incomplete")
                    {
                        batch.MarkFailed(
                            "archive-source-incomplete",
                            "The fixed archive intent no longer has every recorded source snapshot; source deletion remains blocked.");
                        await SaveWithFailureCodeAsync(
                            "archive-failure-persist-failed",
                            "The incomplete archive source classification could not be persisted; no source rows were deleted.",
                            cancellationToken);
                    }
                    return new OrderUrgencyRetentionRunResult(
                        0, resumedDeletes, archiveDeleted, 1, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
                }
                archiveMaterial = CreateArchiveMaterial(candidates, scope, batch.CreatedAtUtc);
                if (!string.Equals(archiveMaterial.BatchId, batch.BatchId, StringComparison.Ordinal))
                {
                    batch.MarkFailed(
                        "archive-intent-mismatch",
                        "The fixed archive intent no longer reconstructs its recorded batch identity; source deletion remains blocked.");
                    await SaveWithFailureCodeAsync(
                        "archive-failure-persist-failed",
                        "The archive intent mismatch classification could not be persisted; no source rows were deleted.",
                        cancellationToken);
                    return new OrderUrgencyRetentionRunResult(
                        0, resumedDeletes, archiveDeleted, 1, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
                }
            }
            else
            {
                candidates = await candidateQuery
                    .Where(snapshot => !dbContext.OrderUrgencyArchiveBatchSnapshots.Any(membership =>
                        membership.OrganizationId == snapshot.OrganizationId &&
                        membership.EnvironmentId == snapshot.EnvironmentId &&
                        membership.SnapshotId == snapshot.Id))
                    .OrderBy(x => x.CalculatedAtUtc)
                    .ThenBy(x => x.OrderId)
                    .ThenBy(x => x.BusinessPriorityRevision)
                    .ThenBy(x => x.Id)
                    .Take(scope.BatchSize)
                    .ToArrayAsync(cancellationToken);
                if (candidates.Length == 0)
                {
                    return new OrderUrgencyRetentionRunResult(
                        0, resumedDeletes, archiveDeleted, 0, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
                }

                candidates = SelectCandidatePrefix(candidates, scope, now);
                archiveMaterial = CreateArchiveMaterial(candidates, scope, now);
                batch = OrderUrgencyArchiveBatch.Create(
                    archiveMaterial.BatchId,
                    scope.OrganizationId,
                    scope.EnvironmentId,
                    JsonSerializer.Serialize(candidates.Select(x => x.Id.Id).ToArray(), ArchiveJson),
                    candidates.Length,
                    candidates.Min(x => x.CalculatedAtUtc),
                    candidates.Max(x => x.CalculatedAtUtc),
                    now);
                dbContext.OrderUrgencyArchiveBatches.Add(batch);
                dbContext.OrderUrgencyArchiveBatchSnapshots.AddRange(
                    candidates.Select((snapshot, sequence) => new OrderUrgencyArchiveBatchSnapshot(
                        batch.Id,
                        snapshot.Id,
                        scope.OrganizationId,
                        scope.EnvironmentId,
                        sequence)));
                await SaveWithFailureCodeAsync(
                    "archive-intent-persist-failed",
                    "The archive batch intent could not be persisted before upload; no source rows were deleted.",
                    cancellationToken);
            }

            var batchId = archiveMaterial.BatchId;
            var bytes = archiveMaterial.Bytes;
            var sha256 = archiveMaterial.Sha256;
            var archivedThisRun = 0;
            if (batch.Status == OrderUrgencyArchiveBatch.ArchivedStatus)
            {
                if (!batch.HasCompleteArchiveEvidence())
                {
                    return new OrderUrgencyRetentionRunResult(
                        0, resumedDeletes, archiveDeleted, 1, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
                }
            }
            else if (batch.Status is not (OrderUrgencyArchiveBatch.PendingStatus or OrderUrgencyArchiveBatch.FailedStatus))
            {
                return new OrderUrgencyRetentionRunResult(
                    0, resumedDeletes, archiveDeleted, 1, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
            }
            else
            {
                if (!retryingFixedIntent && bytes.LongLength > scope.MaxArchiveBytes)
                {
                    batch.MarkFailed(
                        "archive-payload-too-large",
                        $"A single archive snapshot exceeds the configured atomic archive limit of {scope.MaxArchiveBytes} bytes.");
                    await SaveWithFailureCodeAsync(
                        "archive-failure-persist-failed",
                        "The oversized archive failure classification could not be persisted; no source rows were deleted.",
                        cancellationToken);
                    return new OrderUrgencyRetentionRunResult(
                        0, resumedDeletes, archiveDeleted, 1, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
                }

                VersionedArchiveEvidence evidence;
                await EnsureLeaseAsync(scope, cancellationToken, forceRenewal: true);
                try
                {
                    evidence = await archiveStore.PutAsync(
                        new PutVersionedArchiveRequest(
                            scope.OrganizationId,
                            scope.EnvironmentId,
                            "order-urgency",
                            batchId,
                            Convert.ToBase64String(bytes),
                            "application/json",
                            sha256,
                            false),
                        cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    batch.MarkFailed("archive-put-failed", exception.Message);
                    await SaveWithFailureCodeAsync(
                        "archive-failure-persist-failed",
                        "The archive upload failure classification could not be persisted; no source rows were deleted.",
                        cancellationToken);
                    return new OrderUrgencyRetentionRunResult(
                        0, resumedDeletes, archiveDeleted, 1, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
                }
                if (!CompleteEvidence(evidence, sha256, bytes.LongLength, archiveMaterial.ExpectedObjectKey))
                {
                    batch.MarkFailed("archive-evidence-mismatch", "Archive evidence is incomplete or does not match the uploaded bytes.");
                    await SaveWithFailureCodeAsync(
                        "archive-failure-persist-failed",
                        "The archive evidence failure classification could not be persisted; no source rows were deleted.",
                        cancellationToken);
                    return new OrderUrgencyRetentionRunResult(
                        0, resumedDeletes, archiveDeleted, 1, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
                }
                await EnsureLeaseAsync(scope, cancellationToken);
                batch.MarkArchived(evidence.ObjectKey, evidence.VersionId, evidence.Sha256, evidence.SizeBytes, evidence.VerifiedAtUtc);
                await SaveWithFailureCodeAsync(
                    "archive-evidence-persist-failed",
                    "Verified exact-version archive evidence could not be persisted; no source rows were deleted.",
                    cancellationToken);
                archivedThisRun = candidates.Length;
            }

            var deleted = await DeleteSourceBatchAsync(batch, scope, cancellationToken);
            return new OrderUrgencyRetentionRunResult(
                archivedThisRun,
                resumedDeletes + deleted,
                archiveDeleted,
                0,
                false,
                true,
                eligibleSnapshots,
                oldestEligibleAgeSeconds);
        }
        finally
        {
            await ReleaseLeaseAsync(scope, cancellationToken);
        }
    }

    public async Task<OrderUrgencyRestoreResult> RestoreAsync(
        string organizationId,
        string environmentId,
        string batchId,
        string actor,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Restore actor and reason are required.");
        }
        var batch = await dbContext.OrderUrgencyArchiveBatches.AsNoTracking().SingleAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.BatchId == batchId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(batch.ObjectKey) ||
            string.IsNullOrWhiteSpace(batch.ObjectVersionId) ||
            string.IsNullOrWhiteSpace(batch.Sha256) ||
            !batch.SizeBytes.HasValue)
        {
            throw new InvalidOperationException("Archive batch does not contain complete object evidence.");
        }
        var response = await archiveStore.GetAsync(
            new GetVersionedArchiveRequest(
                organizationId,
                environmentId,
                batch.ObjectKey,
                batch.ObjectVersionId,
                batch.Sha256,
                batch.SizeBytes.Value),
            cancellationToken);
        var bytes = Convert.FromBase64String(response.ContentBase64);
        if (!CompleteEvidence(
                response.Evidence,
                batch.Sha256,
                batch.SizeBytes.Value,
                batch.ObjectKey,
                batch.ObjectVersionId) ||
            !string.Equals(Hash(bytes), batch.Sha256, StringComparison.Ordinal) ||
            bytes.LongLength != batch.SizeBytes.Value)
        {
            throw new InvalidOperationException("Restored archive bytes do not match recorded evidence.");
        }
        var envelope = JsonSerializer.Deserialize<OrderUrgencyArchiveEnvelope>(bytes, ArchiveJson)
            ?? throw new InvalidOperationException("Archive envelope is invalid.");
        if (envelope.SchemaVersion != "order-urgency-archive-v1" ||
            envelope.OrganizationId != organizationId ||
            envelope.EnvironmentId != environmentId ||
            envelope.BatchId != batchId)
        {
            throw new InvalidOperationException("Archive envelope scope or schema version does not match the restore request.");
        }

        var restored = 0;
        foreach (var snapshot in envelope.Snapshots)
        {
            var exists = await dbContext.OrderUrgencySnapshots.AsNoTracking().AnyAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.OrderId == snapshot.OrderId &&
                x.ModelVersion == snapshot.ModelVersion &&
                x.InputFingerprint == snapshot.InputFingerprint &&
                x.BusinessPriorityRevision == snapshot.BusinessPriorityRevision &&
                x.CalculationBucketUtc == snapshot.CalculationBucketUtc,
                cancellationToken);
            if (exists) continue;
            dbContext.OrderUrgencySnapshots.Add(new OrderUrgencySnapshot(
                snapshot.OrganizationId,
                snapshot.EnvironmentId,
                snapshot.OrderId,
                snapshot.BusinessReference,
                snapshot.Level,
                snapshot.ModelVersion,
                snapshot.InputFingerprint,
                snapshot.BusinessPriorityRevision,
                snapshot.CalculationBucketUtc,
                snapshot.CalculatedAtUtc,
                snapshot.ResultJson));
            restored++;
        }
        dbContext.OrderUrgencyRestoreAudits.Add(new OrderUrgencyRestoreAudit(
            batchId,
            organizationId,
            environmentId,
            batch.ObjectVersionId,
            actor.Trim(),
            reason.Trim(),
            restored,
            timeProvider.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new OrderUrgencyRestoreResult(restored, batchId, batch.ObjectVersionId);
    }

    private async Task<int> ResumeSourceDeletesAsync(
        OrderUrgencyRetentionScope scope,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!scope.CanDeleteSource(now)) return 0;
        var batches = await dbContext.OrderUrgencyArchiveBatches
            .Where(x => x.OrganizationId == scope.OrganizationId &&
                        x.EnvironmentId == scope.EnvironmentId &&
                        x.Status == OrderUrgencyArchiveBatch.ArchivedStatus)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(RecoveryBatchLimit)
            .ToArrayAsync(cancellationToken);
        var deleted = 0;
        foreach (var batch in batches)
        {
            deleted += await DeleteSourceBatchAsync(batch, scope, cancellationToken);
        }
        return deleted;
    }

    private async Task<int> DeleteSourceBatchAsync(
        OrderUrgencyArchiveBatch batch,
        OrderUrgencyRetentionScope scope,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (batch.Status != OrderUrgencyArchiveBatch.ArchivedStatus || !scope.CanDeleteSource(now)) return 0;
        if (!batch.HasCompleteArchiveEvidence())
        {
            return 0;
        }
        await EnsureLeaseAsync(scope, cancellationToken, forceRenewal: true);
        var liveEvidence = await archiveStore.GetAsync(
            new GetVersionedArchiveRequest(
                scope.OrganizationId,
                scope.EnvironmentId,
                batch.ObjectKey!,
                batch.ObjectVersionId!,
                batch.Sha256!,
                batch.SizeBytes!.Value),
            cancellationToken);
        if (!CompleteEvidence(
                liveEvidence.Evidence,
                batch.Sha256!,
                batch.SizeBytes!.Value,
                batch.ObjectKey,
                batch.ObjectVersionId))
        {
            throw new InvalidOperationException("Archive evidence could not be revalidated immediately before source deletion.");
        }
        now = timeProvider.GetUtcNow();
        if (!scope.CanDeleteSource(now)) return 0;
        var ids = await LoadMembershipIdsAsync(batch.Id, cancellationToken);
        var rows = await dbContext.OrderUrgencySnapshots
            .Where(x => x.OrganizationId == scope.OrganizationId &&
                        x.EnvironmentId == scope.EnvironmentId &&
                        ids.Contains(x.Id) &&
                        x.CalculatedAtUtc < now - scope.OnlineRetention &&
                        dbContext.OrderUrgencySnapshots.Any(newer =>
                            newer.OrganizationId == x.OrganizationId &&
                            newer.EnvironmentId == x.EnvironmentId &&
                            newer.OrderId == x.OrderId &&
                            (newer.CalculatedAtUtc > x.CalculatedAtUtc ||
                             (newer.CalculatedAtUtc == x.CalculatedAtUtc &&
                              newer.BusinessPriorityRevision > x.BusinessPriorityRevision))))
            .ToArrayAsync(cancellationToken);
        if (ids.Length != batch.SnapshotCount || rows.Length != batch.SnapshotCount)
        {
            return 0;
        }
        if (beforeSourceDeleteCommit is not null)
        {
            await beforeSourceDeleteCommit(cancellationToken);
        }
        var commitAtUtc = await TryPrepareLeaseFenceAtCommitAsync(scope, cancellationToken);
        if (!commitAtUtc.HasValue)
        {
            dbContext.ChangeTracker.Clear();
            return 0;
        }
        dbContext.OrderUrgencySnapshots.RemoveRange(rows);
        var authorization = scope.SourceDeletionAuthorization!;
        batch.MarkSourceDeleted(authorization.Reference, authorization.Actor, authorization.Reason, commitAtUtc.Value);
        await SaveWithFailureCodeAsync(
            "source-delete-persist-failed",
            "The fenced source deletion could not be committed; the database transaction was rolled back.",
            cancellationToken);
        return rows.Length;
    }

    private async Task<int> DeleteExpiredArchivesAsync(
        OrderUrgencyRetentionScope scope,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!scope.CanDeleteArchive(now)) return 0;
        var cutoff = now - scope.TotalRetention;
        var batches = await dbContext.OrderUrgencyArchiveBatches
            .Where(x => x.OrganizationId == scope.OrganizationId &&
                        x.EnvironmentId == scope.EnvironmentId &&
                        x.Status == OrderUrgencyArchiveBatch.SourceDeletedStatus &&
                        x.MaxCalculatedAtUtc < cutoff)
            .OrderBy(x => x.MaxCalculatedAtUtc)
            .Take(RecoveryBatchLimit)
            .ToArrayAsync(cancellationToken);
        var deleted = 0;
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch.ObjectKey) || string.IsNullOrWhiteSpace(batch.ObjectVersionId)) continue;
            await EnsureLeaseAsync(scope, cancellationToken, forceRenewal: true);
            now = timeProvider.GetUtcNow();
            if (!scope.CanDeleteArchive(now)) break;
            var authorization = scope.ArchiveDeletionAuthorization!;
            await archiveStore.DeleteAsync(new DeleteVersionedArchiveRequest(
                scope.OrganizationId,
                scope.EnvironmentId,
                batch.ObjectKey,
                batch.ObjectVersionId,
                authorization.Reference,
                authorization.Actor,
                authorization.Reason), cancellationToken);
            now = timeProvider.GetUtcNow();
            await PrepareLeaseFenceAsync(scope, now, cancellationToken);
            batch.MarkArchiveDeleted(authorization.Reference, authorization.Actor, authorization.Reason, now);
            await SaveWithFailureCodeAsync(
                "archive-delete-audit-persist-failed",
                "The fenced archive deletion audit could not be committed and requires reconciliation.",
                cancellationToken);
            deleted++;
        }
        return deleted;
    }

    private async Task<bool> TryAcquireLeaseAsync(
        OrderUrgencyRetentionScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lease = await dbContext.OrderUrgencyRetentionLeases.SingleOrDefaultAsync(
            x => x.OrganizationId == scope.OrganizationId && x.EnvironmentId == scope.EnvironmentId,
            cancellationToken);
        if (lease is not null && lease.IsActiveAt(now) && lease.OwnerId != workerId) return false;
        if (lease is null)
        {
            dbContext.OrderUrgencyRetentionLeases.Add(new OrderUrgencyRetentionLease(
                scope.OrganizationId, scope.EnvironmentId, workerId, now, now + LeaseDuration));
        }
        else
        {
            lease.Acquire(workerId, now, now + LeaseDuration);
        }
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task ReleaseLeaseAsync(OrderUrgencyRetentionScope scope, CancellationToken cancellationToken)
    {
        // A failed retention step must never be committed incidentally by the lease
        // release SaveChanges. All intended work is persisted before entering finally;
        // reload only the lease into a clean unit of work.
        dbContext.ChangeTracker.Clear();
        var lease = await dbContext.OrderUrgencyRetentionLeases.SingleOrDefaultAsync(
            x => x.OrganizationId == scope.OrganizationId && x.EnvironmentId == scope.EnvironmentId,
            cancellationToken);
        if (lease is null) return;
        lease.Release(workerId, timeProvider.GetUtcNow());
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Entry(lease).State = EntityState.Detached;
        }
    }

    private async Task EnsureLeaseAsync(
        OrderUrgencyRetentionScope scope,
        CancellationToken cancellationToken,
        bool forceRenewal = false)
    {
        var now = timeProvider.GetUtcNow();
        var lease = await dbContext.OrderUrgencyRetentionLeases.SingleOrDefaultAsync(
            x => x.OrganizationId == scope.OrganizationId && x.EnvironmentId == scope.EnvironmentId,
            cancellationToken);
        if (lease is null ||
            !string.Equals(lease.OwnerId, workerId, StringComparison.Ordinal) ||
            !lease.IsActiveAt(now))
        {
            throw new InvalidOperationException("Order urgency retention lease was lost; source rows were preserved.");
        }
        if (!forceRenewal && lease.ExpiresAtUtc - now > LeaseRenewalThreshold) return;

        lease.Renew(workerId, now, now + LeaseDuration);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.ChangeTracker.Clear();
            throw new InvalidOperationException(
                "Order urgency retention lease renewal lost a concurrency race; source rows were preserved.",
                exception);
        }
    }

    private async Task PrepareLeaseFenceAsync(
        OrderUrgencyRetentionScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lease = await dbContext.OrderUrgencyRetentionLeases.SingleOrDefaultAsync(
            x => x.OrganizationId == scope.OrganizationId && x.EnvironmentId == scope.EnvironmentId,
            cancellationToken);
        if (lease is null ||
            !string.Equals(lease.OwnerId, workerId, StringComparison.Ordinal) ||
            !lease.IsActiveAt(now))
        {
            throw new InvalidOperationException("Order urgency retention lease was lost; source rows were preserved.");
        }

        // This revision update is committed in the same SaveChanges transaction as the
        // destructive transition. A concurrent lease takeover changes the revision and
        // forces the complete transaction, including source deletes, to roll back.
        lease.Renew(workerId, now, now + LeaseDuration);
    }

    private async Task<DateTimeOffset?> TryPrepareLeaseFenceAtCommitAsync(
        OrderUrgencyRetentionScope scope,
        CancellationToken cancellationToken)
    {
        var liveLease = await dbContext.OrderUrgencyRetentionLeases.AsNoTracking().SingleOrDefaultAsync(
            x => x.OrganizationId == scope.OrganizationId && x.EnvironmentId == scope.EnvironmentId,
            cancellationToken);
        if (afterSourceDeleteLeaseRead is not null)
        {
            await afterSourceDeleteLeaseRead(cancellationToken);
        }
        var commitAtUtc = timeProvider.GetUtcNow();
        if (liveLease is null ||
            !scope.CanDeleteSource(commitAtUtc) ||
            !string.Equals(liveLease.OwnerId, workerId, StringComparison.Ordinal) ||
            !liveLease.IsActiveAt(commitAtUtc))
        {
            return null;
        }

        var trackedLease = dbContext.OrderUrgencyRetentionLeases.Local.SingleOrDefault(
            x => x.OrganizationId == scope.OrganizationId && x.EnvironmentId == scope.EnvironmentId);
        if (trackedLease is null ||
            !string.Equals(trackedLease.OwnerId, workerId, StringComparison.Ordinal))
        {
            return null;
        }

        // The live read rejects natural expiry or an already-completed takeover. This
        // tracked revision update then fences a takeover racing between that read and
        // the destructive SaveChanges transaction.
        trackedLease.Renew(workerId, commitAtUtc, commitAtUtc + LeaseDuration);
        return commitAtUtc;
    }

    private async Task SaveWithFailureCodeAsync(
        string errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            dbContext.ChangeTracker.Clear();
            throw new OrderUrgencyRetentionOperationException(errorCode, message, exception);
        }
    }

    private async Task<OrderUrgencySnapshot[]> LoadFixedIntentCandidatesAsync(
        OrderUrgencyArchiveBatch batch,
        OrderUrgencyRetentionScope scope,
        CancellationToken cancellationToken)
    {
        var snapshotIds = await LoadMembershipIdsAsync(batch.Id, cancellationToken);
        var snapshots = await dbContext.OrderUrgencySnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.OrganizationId == scope.OrganizationId &&
                snapshot.EnvironmentId == scope.EnvironmentId &&
                snapshotIds.Contains(snapshot.Id))
            .ToArrayAsync(cancellationToken);
        var byId = snapshots.ToDictionary(snapshot => snapshot.Id);
        return snapshotIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToArray();
    }

    private Task<OrderUrgencySnapshotId[]> LoadMembershipIdsAsync(Guid archiveBatchId, CancellationToken cancellationToken) =>
        dbContext.OrderUrgencyArchiveBatchSnapshots.AsNoTracking()
            .Where(membership => membership.ArchiveBatchId == archiveBatchId)
            .OrderBy(membership => membership.Sequence)
            .Select(membership => membership.SnapshotId)
            .ToArrayAsync(cancellationToken);

    private static bool CompleteEvidence(
        VersionedArchiveEvidence evidence,
        string sha256,
        long sizeBytes,
        string? expectedObjectKey = null,
        string? expectedVersionId = null) =>
        !string.IsNullOrWhiteSpace(evidence.ObjectKey) &&
        !string.IsNullOrWhiteSpace(evidence.VersionId) &&
        (expectedObjectKey is null || string.Equals(evidence.ObjectKey, expectedObjectKey, StringComparison.Ordinal)) &&
        (expectedVersionId is null || string.Equals(evidence.VersionId, expectedVersionId, StringComparison.Ordinal)) &&
        string.Equals(evidence.Sha256, sha256, StringComparison.OrdinalIgnoreCase) &&
        evidence.SizeBytes == sizeBytes &&
        evidence.VerifiedAtUtc != default;

    private static OrderUrgencySnapshot[] SelectCandidatePrefix(
        OrderUrgencySnapshot[] candidates,
        OrderUrgencyRetentionScope scope,
        DateTimeOffset archivedAtUtc)
    {
        var low = 1;
        var high = candidates.Length;
        var best = 0;
        while (low <= high)
        {
            var count = low + ((high - low) / 2);
            var material = CreateArchiveMaterial(candidates[..count], scope, archivedAtUtc);
            if (material.Bytes.LongLength <= scope.MaxArchiveBytes)
            {
                best = count;
                low = count + 1;
            }
            else
            {
                high = count - 1;
            }
        }

        // Keep one row when an individual snapshot is oversized so the durable
        // batch can record a stable failure without ever invoking FileStorage.
        return candidates[..Math.Max(1, best)];
    }

    private static ArchiveMaterial CreateArchiveMaterial(
        OrderUrgencySnapshot[] candidates,
        OrderUrgencyRetentionScope scope,
        DateTimeOffset archivedAtUtc)
    {
        var archivedSnapshots = candidates.Select(ToArchivedSnapshot).ToArray();
        var identity = string.Join(
            "\n",
            archivedSnapshots.Select((snapshot, index) =>
                $"{candidates[index].Id.Id:N}\u001f{snapshot.OrganizationId}\u001f{snapshot.EnvironmentId}\u001f{snapshot.OrderId}\u001f{snapshot.ModelVersion}\u001f{snapshot.InputFingerprint}\u001f{snapshot.BusinessPriorityRevision}\u001f{snapshot.CalculationBucketUtc:O}"));
        var batchId = Hash(Encoding.UTF8.GetBytes(identity))[..32];
        var envelope = new OrderUrgencyArchiveEnvelope(
            "order-urgency-archive-v1",
            scope.OrganizationId,
            scope.EnvironmentId,
            batchId,
            archivedAtUtc,
            archivedSnapshots);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, ArchiveJson);
        return new ArchiveMaterial(
            batchId,
            bytes,
            Hash(bytes),
            $"compliance-archives/{scope.OrganizationId}/{scope.EnvironmentId}/order-urgency/{batchId}.json");
    }

    private static OrderUrgencyArchivedSnapshot ToArchivedSnapshot(OrderUrgencySnapshot snapshot) =>
        new(
            snapshot.OrganizationId,
            snapshot.EnvironmentId,
            snapshot.OrderId,
            snapshot.BusinessReference,
            snapshot.Level,
            snapshot.ModelVersion,
            snapshot.InputFingerprint,
            snapshot.BusinessPriorityRevision,
            snapshot.CalculationBucketUtc,
            snapshot.CalculatedAtUtc,
            snapshot.ResultJson);

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed record OrderUrgencyArchiveEnvelope(
        string SchemaVersion,
        string OrganizationId,
        string EnvironmentId,
        string BatchId,
        DateTimeOffset ArchivedAtUtc,
        IReadOnlyCollection<OrderUrgencyArchivedSnapshot> Snapshots);

    private sealed record ArchiveMaterial(
        string BatchId,
        byte[] Bytes,
        string Sha256,
        string ExpectedObjectKey);

    private sealed record OrderUrgencyArchivedSnapshot(
        string OrganizationId,
        string EnvironmentId,
        string OrderId,
        string BusinessReference,
        OrderUrgencyLevel Level,
        string ModelVersion,
        string InputFingerprint,
        long BusinessPriorityRevision,
        DateTimeOffset CalculationBucketUtc,
        DateTimeOffset CalculatedAtUtc,
        string ResultJson);
}
