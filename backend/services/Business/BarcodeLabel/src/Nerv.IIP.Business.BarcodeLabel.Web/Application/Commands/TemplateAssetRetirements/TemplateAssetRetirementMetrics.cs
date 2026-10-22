using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Prometheus;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;

/// <summary>Owner-fact gauges survive restart and detail cleanup; hit counters count observations, never attempts.</summary>
public sealed class TemplateAssetRetirementMetrics(CollectorRegistry registry,
    ILogger<TemplateAssetRetirementMetrics> logger)
{
    private readonly Gauge accepted = Metrics.WithCustomRegistry(registry).CreateGauge(
        "nerv_iip_barcode_retirement_accepted_decisions", "Accepted retirement decisions, including permanent successful fences.");
    private readonly Gauge terminal = Metrics.WithCustomRegistry(registry).CreateGauge(
        "nerv_iip_barcode_retirement_terminal_decisions", "Retirement terminal owner facts; unknown is not failure.",
        new GaugeConfiguration { LabelNames = ["outcome"] });
    private readonly Gauge pending = Metrics.WithCustomRegistry(registry).CreateGauge(
        "nerv_iip_barcode_retirement_pending_decisions", "Retirement decisions still awaiting an authoritative outcome.");
    private readonly Gauge unknown = Metrics.WithCustomRegistry(registry).CreateGauge(
        "nerv_iip_barcode_retirement_execution_outcome_unknown_decisions", "Permanent unknown outcomes excluded from execution.");
    private readonly Gauge expired = Metrics.WithCustomRegistry(registry).CreateGauge(
        "nerv_iip_barcode_retirement_replay_window_expired_decisions", "Permanent successful fences at or beyond their frozen replay deadline.");
    private readonly Counter fenceHits = Metrics.WithCustomRegistry(registry).CreateCounter(
        "nerv_iip_barcode_retirement_zero_reexecution_fence_hits_total", "Expired request rejections or unknown decisions excluded by idle execution scans; not execution attempts.",
        new CounterConfiguration { LabelNames = ["cause"] });

    public async Task RefreshAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        // One SQL statement gives one snapshot. A successful detail and its fence are one fact.
        var rows = await db.TemplateAssetRetirementDecisions
            .Where(x => x.Status != TemplateAssetRetirementDecision.QuotaReleasedStatus)
            .Select(x => x.Status)
            .Concat(db.TemplateAssetRetirementReplayFences.Select(x => now >= x.ReplayUntilUtc
                ? "replay-window-expired" : TemplateAssetRetirementDecision.QuotaReleasedStatus))
            .GroupBy(status => status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        accepted.Set(rows.Values.Sum());
        pending.Set(rows.GetValueOrDefault(TemplateAssetRetirementDecision.PendingStatus));
        unknown.Set(rows.GetValueOrDefault(TemplateAssetRetirementDecision.ExecutionOutcomeUnknownStatus));
        expired.Set(rows.GetValueOrDefault("replay-window-expired"));
        terminal.WithLabels("success").Set(rows.GetValueOrDefault(TemplateAssetRetirementDecision.QuotaReleasedStatus)
            + rows.GetValueOrDefault("replay-window-expired"));
        // There is currently no terminal-failure producer. Transport failures remain pending/unknown.
        terminal.WithLabels("failure").Set(0);
    }

    public void RecordExpiredReplay()
    {
        fenceHits.WithLabels("replay-window-expired").Inc();
        logger.LogInformation("Retirement execution fenced: {RetirementSignal}; count={DecisionCount}.", "replay-window-expired", 1);
    }

    public void RecordUnknownExclusions(int count)
    {
        if (count == 0) return;
        fenceHits.WithLabels("execution-outcome-unknown").Inc(count);
        logger.LogDebug("Retirement execution fenced: {RetirementSignal}; count={DecisionCount}.", "execution-outcome-unknown", count);
    }
}
