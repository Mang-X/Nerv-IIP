using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Retirement;

public sealed class TemplateAssetRetirementExecutionStore(ApplicationDbContext db, TimeProvider clock)
{
    public Task<int> CountUnknownExclusionsAsync(CancellationToken ct) =>
        db.TemplateAssetRetirementDecisions.CountAsync(
            x => x.Status == TemplateAssetRetirementDecision.ExecutionOutcomeUnknownStatus, ct);

    public async Task<TemplateAssetRetirementDecision?> ClaimAsync(
        long clientWindowSeconds, long leaseSeconds, long maxBackoffSeconds, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await CleanupAsync(ct);
        await ExpireAsync(now, ct);
        var candidate = await db.TemplateAssetRetirementDecisions.AsNoTracking()
            .Where(x => x.Status == TemplateAssetRetirementDecision.PendingStatus
                && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
            .OrderBy(x => x.CreatedAtUtc).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (candidate is null) return null;

        var leaseId = Guid.NewGuid();
        var changed = await db.TemplateAssetRetirementDecisions
            .Where(x => x.Id == candidate && x.Status == TemplateAssetRetirementDecision.PendingStatus
                && (x.RecoveryUntilUtc == null || now < x.RecoveryUntilUtc)
                && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.FirstSentAtUtc, x => x.FirstSentAtUtc ?? now)
                .SetProperty(x => x.RecoveryUntilUtc, x => x.RecoveryUntilUtc ?? now.AddDays(7))
                .SetProperty(x => x.ReplayPolicyVersion, x => x.ReplayPolicyVersion ?? 1L)
                .SetProperty(x => x.ClientWindowSeconds, x => x.ClientWindowSeconds ?? clientWindowSeconds)
                .SetProperty(x => x.ExecutorLeaseSeconds, x => x.ExecutorLeaseSeconds ?? leaseSeconds)
                .SetProperty(x => x.ExecutorMaxBackoffSeconds, x => x.ExecutorMaxBackoffSeconds ?? maxBackoffSeconds)
                .SetProperty(x => x.ExecutionLeaseId, leaseId)
                .SetProperty(x => x.NextAttemptAtUtc, x => now.AddSeconds(x.ExecutorLeaseSeconds ?? leaseSeconds))
                .SetProperty(x => x.UpdatedAtUtc, now), ct);
        return changed == 0 ? null : await db.TemplateAssetRetirementDecisions.AsNoTracking()
            .SingleAsync(x => x.Id == candidate, ct);
    }

    public async Task CompleteAsync(TemplateAssetRetirementDecision decision,
        DateTimeOffset quotaReleasedAtUtc, long replayHorizonSeconds, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Sample the clock after acquiring the row: time spent waiting for a concurrent writer
        // must not turn a pre-deadline timestamp into permission to commit a late success.
        _ = await db.TemplateAssetRetirementDecisions.FromSqlInterpolated(
            $"SELECT * FROM barcode.template_asset_retirement_decisions WHERE id = {decision.Id.Id} FOR UPDATE")
            .AsNoTracking().SingleAsync(ct);
        var now = clock.GetUtcNow();
        await ExpireAsync(now, ct, decision.Id);
        // The deadline belongs to the result CAS itself: an in-flight success cannot clear the hold.
        var changed = await db.TemplateAssetRetirementDecisions
            .Where(x => x.Id == decision.Id && x.ExecutionLeaseId == decision.ExecutionLeaseId
                && x.Status == TemplateAssetRetirementDecision.PendingStatus && now < x.RecoveryUntilUtc)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, TemplateAssetRetirementDecision.QuotaReleasedStatus)
                .SetProperty(x => x.QuotaReleasedAtUtc, quotaReleasedAtUtc)
                .SetProperty(x => x.ReplayHorizonSeconds, replayHorizonSeconds)
                .SetProperty(x => x.CompletedAtUtc, now)
                .SetProperty(x => x.ReplayUntilUtc, now.AddSeconds(replayHorizonSeconds))
                .SetProperty(x => x.ExecutionLeaseId, (Guid?)null)
                .SetProperty(x => x.NextAttemptAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.UpdatedAtUtc, now), ct);
        if (changed != 0)
        {
            db.TemplateAssetRetirementReplayFences.Add(new(decision, now.AddSeconds(replayHorizonSeconds)));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public Task<int> CleanupAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.TemplateAssetRetirementDecisions
            .Where(x => x.Status == TemplateAssetRetirementDecision.QuotaReleasedStatus && now >= x.ReplayUntilUtc)
            .ExecuteDeleteAsync(ct);
    }

    public async Task RetryAsync(TemplateAssetRetirementDecision decision, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await ExpireAsync(now, ct, decision.Id);
        await db.TemplateAssetRetirementDecisions
            .Where(x => x.Id == decision.Id && x.ExecutionLeaseId == decision.ExecutionLeaseId
                && x.Status == TemplateAssetRetirementDecision.PendingStatus && now < x.RecoveryUntilUtc)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ExecutionLeaseId, (Guid?)null)
                .SetProperty(x => x.NextAttemptAtUtc, now.AddSeconds(decision.ExecutorMaxBackoffSeconds!.Value))
                .SetProperty(x => x.UpdatedAtUtc, now), ct);
    }

    private Task<int> ExpireAsync(DateTimeOffset now, CancellationToken ct,
        TemplateAssetRetirementDecisionId? decisionId = null) =>
        db.TemplateAssetRetirementDecisions
            .Where(x => (decisionId == null || x.Id == decisionId)
                && x.Status == TemplateAssetRetirementDecision.PendingStatus && now >= x.RecoveryUntilUtc)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, TemplateAssetRetirementDecision.ExecutionOutcomeUnknownStatus)
                .SetProperty(x => x.ExecutionLeaseId, (Guid?)null)
                .SetProperty(x => x.NextAttemptAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.UpdatedAtUtc, now), ct);
}
