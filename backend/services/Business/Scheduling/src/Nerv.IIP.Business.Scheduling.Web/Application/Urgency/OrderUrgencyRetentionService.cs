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

public sealed class OrderUrgencyRetentionService(
    ApplicationDbContext dbContext,
    IOrderUrgencyArchiveStore archiveStore,
    TimeProvider timeProvider,
    string workerId)
{
    private static readonly JsonSerializerOptions ArchiveJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

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
            var archiveDeleted = await DeleteExpiredArchivesAsync(scope, now, cancellationToken);
            var resumedDeletes = await ResumeSourceDeletesAsync(scope, now, cancellationToken);
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
            var candidates = await candidateQuery
                .OrderBy(x => x.CalculatedAtUtc)
                .ThenBy(x => x.OrderId)
                .Take(scope.BatchSize)
                .ToArrayAsync(cancellationToken);
            if (candidates.Length == 0)
            {
                return new OrderUrgencyRetentionRunResult(
                    0, resumedDeletes, archiveDeleted, 0, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
            }

            var archivedSnapshots = candidates.Select(ToArchivedSnapshot).ToArray();
            var identity = string.Join(
                "\n",
                archivedSnapshots.Select(x =>
                    $"{x.OrganizationId}\u001f{x.EnvironmentId}\u001f{x.OrderId}\u001f{x.ModelVersion}\u001f{x.InputFingerprint}\u001f{x.BusinessPriorityRevision}\u001f{x.CalculationBucketUtc:O}"));
            var batchId = Hash(Encoding.UTF8.GetBytes(identity))[..32];
            var batch = await dbContext.OrderUrgencyArchiveBatches.SingleOrDefaultAsync(
                x => x.OrganizationId == scope.OrganizationId &&
                     x.EnvironmentId == scope.EnvironmentId &&
                     x.BatchId == batchId,
                cancellationToken);
            if (batch is null)
            {
                batch = OrderUrgencyArchiveBatch.Create(
                    batchId,
                    scope.OrganizationId,
                    scope.EnvironmentId,
                    JsonSerializer.Serialize(candidates.Select(x => x.Id.Id).ToArray(), ArchiveJson),
                    candidates.Length,
                    candidates.Min(x => x.CalculatedAtUtc),
                    candidates.Max(x => x.CalculatedAtUtc),
                    now);
                dbContext.OrderUrgencyArchiveBatches.Add(batch);
            }

            var envelope = new OrderUrgencyArchiveEnvelope(
                "order-urgency-archive-v1",
                scope.OrganizationId,
                scope.EnvironmentId,
                batchId,
                now,
                archivedSnapshots);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, ArchiveJson);
            var sha256 = Hash(bytes);
            try
            {
                var evidence = await archiveStore.PutAsync(
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
                if (!CompleteEvidence(evidence, sha256, bytes.LongLength))
                {
                    throw new InvalidOperationException("Archive evidence is incomplete.");
                }
                batch.MarkArchived(evidence.ObjectKey, evidence.VersionId, evidence.Sha256, evidence.SizeBytes, evidence.VerifiedAtUtc);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                batch.MarkFailed("archive-evidence-incomplete", exception.Message);
                await dbContext.SaveChangesAsync(cancellationToken);
                return new OrderUrgencyRetentionRunResult(
                    0, resumedDeletes, archiveDeleted, 1, false, true, eligibleSnapshots, oldestEligibleAgeSeconds);
            }

            var deleted = await DeleteSourceBatchAsync(batch, scope, now, cancellationToken);
            return new OrderUrgencyRetentionRunResult(
                candidates.Length,
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
        if (!CompleteEvidence(response.Evidence, batch.Sha256, batch.SizeBytes.Value) ||
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
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!scope.CanDeleteSource(now)) return 0;
        var batches = await dbContext.OrderUrgencyArchiveBatches
            .Where(x => x.OrganizationId == scope.OrganizationId &&
                        x.EnvironmentId == scope.EnvironmentId &&
                        x.Status == "archived")
            .OrderBy(x => x.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        var deleted = 0;
        foreach (var batch in batches)
        {
            deleted += await DeleteSourceBatchAsync(batch, scope, now, cancellationToken);
        }
        return deleted;
    }

    private async Task<int> DeleteSourceBatchAsync(
        OrderUrgencyArchiveBatch batch,
        OrderUrgencyRetentionScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (batch.Status != "archived" || !scope.CanDeleteSource(now)) return 0;
        if (string.IsNullOrWhiteSpace(batch.ObjectKey) ||
            string.IsNullOrWhiteSpace(batch.ObjectVersionId) ||
            string.IsNullOrWhiteSpace(batch.Sha256) ||
            !batch.SizeBytes.HasValue)
        {
            return 0;
        }
        var liveEvidence = await archiveStore.GetAsync(
            new GetVersionedArchiveRequest(
                scope.OrganizationId,
                scope.EnvironmentId,
                batch.ObjectKey,
                batch.ObjectVersionId,
                batch.Sha256,
                batch.SizeBytes.Value),
            cancellationToken);
        if (!CompleteEvidence(liveEvidence.Evidence, batch.Sha256, batch.SizeBytes.Value))
        {
            throw new InvalidOperationException("Archive evidence could not be revalidated immediately before source deletion.");
        }
        var ids = (JsonSerializer.Deserialize<Guid[]>(batch.SnapshotIdsJson, ArchiveJson) ?? [])
            .Select(id => new OrderUrgencySnapshotId(id))
            .ToArray();
        var rows = await dbContext.OrderUrgencySnapshots
            .Where(x => x.OrganizationId == scope.OrganizationId &&
                        x.EnvironmentId == scope.EnvironmentId &&
                        ids.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
        dbContext.OrderUrgencySnapshots.RemoveRange(rows);
        var authorization = scope.SourceDeletionAuthorization!;
        batch.MarkSourceDeleted(authorization.Reference, authorization.Actor, authorization.Reason, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return rows.Length;
    }

    private async Task<int> DeleteExpiredArchivesAsync(
        OrderUrgencyRetentionScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!scope.CanDeleteArchive(now)) return 0;
        var cutoff = now - scope.TotalRetention;
        var batches = await dbContext.OrderUrgencyArchiveBatches
            .Where(x => x.OrganizationId == scope.OrganizationId &&
                        x.EnvironmentId == scope.EnvironmentId &&
                        x.Status == "source-deleted" &&
                        x.MaxCalculatedAtUtc < cutoff)
            .OrderBy(x => x.MaxCalculatedAtUtc)
            .Take(scope.BatchSize)
            .ToArrayAsync(cancellationToken);
        var deleted = 0;
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch.ObjectKey) || string.IsNullOrWhiteSpace(batch.ObjectVersionId)) continue;
            var authorization = scope.ArchiveDeletionAuthorization!;
            await archiveStore.DeleteAsync(new DeleteVersionedArchiveRequest(
                scope.OrganizationId,
                scope.EnvironmentId,
                batch.ObjectKey,
                batch.ObjectVersionId,
                authorization.Reference,
                authorization.Actor,
                authorization.Reason), cancellationToken);
            batch.MarkArchiveDeleted(authorization.Reference, authorization.Actor, authorization.Reason, now);
            await dbContext.SaveChangesAsync(cancellationToken);
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

    private static bool CompleteEvidence(VersionedArchiveEvidence evidence, string sha256, long sizeBytes) =>
        !string.IsNullOrWhiteSpace(evidence.ObjectKey) &&
        !string.IsNullOrWhiteSpace(evidence.VersionId) &&
        string.Equals(evidence.Sha256, sha256, StringComparison.OrdinalIgnoreCase) &&
        evidence.SizeBytes == sizeBytes;

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
