using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class TelemetryProductionReportCandidateTests
{
    [Fact]
    public void Confirm_pending_candidate_records_assignment_and_audit_transition()
    {
        var candidate = TelemetryProductionReportCandidate.CreatePendingConfirmation(
            "org-001", "env-dev", "source-001", "DEV-01", "parts_count", "posted", 3m,
            DateTimeOffset.Parse("2026-07-12T01:00:00Z"), DateTimeOffset.Parse("2026-07-12T01:01:00Z"),
            "WC-01", null, null, TelemetryProductionReportCandidate.NoCurrentWorkOrderSuspensionReason);

        candidate.Confirm("WO-001", "OP-10", "operator:001", DateTimeOffset.Parse("2026-07-12T01:02:00Z"), "report-id");

        Assert.Equal(TelemetryProductionReportCandidate.ConfirmedStatus, candidate.Status);
        Assert.Equal("WO-001", candidate.WorkOrderId);
        Assert.Equal("OP-10", candidate.OperationTaskId);
        Assert.Equal("operator:001", candidate.ResolvedBy);
        Assert.Single(candidate.Transitions);
        Assert.IsType<TelemetryProductionReportCandidateConfirmedDomainEvent>(candidate.GetDomainEvents().Single());
    }

    [Fact]
    public void Dismiss_requires_reason_and_terminal_candidate_cannot_transition_again()
    {
        var candidate = TelemetryProductionReportCandidate.CreateDraft(
            "org-001", "env-dev", "source-002", "DEV-01", "parts_count", 3m,
            DateTimeOffset.Parse("2026-07-12T01:00:00Z"), DateTimeOffset.Parse("2026-07-12T01:01:00Z"),
            "WC-01", "WO-001", "OP-10");

        Assert.Throws<ArgumentException>(() => candidate.Dismiss(" ", "operator:001", DateTimeOffset.UtcNow));
        candidate.Dismiss("counter reset", "operator:001", DateTimeOffset.Parse("2026-07-12T01:02:00Z"));

        Assert.Equal(TelemetryProductionReportCandidate.DismissedStatus, candidate.Status);
        Assert.IsType<TelemetryProductionReportCandidateDismissedDomainEvent>(candidate.GetDomainEvents().Single());
        Assert.Throws<InvalidOperationException>(() => candidate.Dismiss("again", "operator:001", DateTimeOffset.UtcNow));
    }
}
