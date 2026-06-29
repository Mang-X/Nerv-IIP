using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class CorrectiveActionTests
{
    [Fact]
    public void Capa_opened_from_ncr_tracks_root_cause_and_actions_until_effectiveness_is_verified()
    {
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-20260616-0001",
            "receiving",
            "RCV-001",
            "SKU-RM-1000",
            12m,
            "seal-leak",
            "BATCH-001",
            null,
            []);

        var capa = CorrectiveAction.OpenFromNcr(
            "org-001",
            "env-dev",
            "CAPA-20260616-0001",
            ncr,
            "5why supplier vulcanization process drift",
            "Contain suspect lots in IQC hold",
            ownerUserId: "qa-engineer-001",
            dueAtUtc: DateTimeOffset.Parse("2026-06-30T00:00:00Z"));

        capa.AddAction("corrective", "Supplier adjusts vulcanization temperature control", "supplier-quality-001", DateTimeOffset.Parse("2026-06-20T00:00:00Z"));
        capa.AddAction("preventive", "Add incoming COA temperature audit item", "qa-engineer-001", DateTimeOffset.Parse("2026-06-25T00:00:00Z"));

        Assert.Equal("open", capa.Status);
        Assert.Equal(ncr.Id.ToString(), capa.SourceNcrId);
        Assert.Equal(2, capa.Actions.Count);
        Assert.Throws<InvalidOperationException>(() => capa.Close("qa-manager-001"));

        Assert.Throws<InvalidOperationException>(() =>
            capa.VerifyEffectiveness("qa-manager-001", "No recurrence in three lots", DateTimeOffset.Parse("2026-07-10T00:00:00Z")));

        foreach (var action in capa.Actions)
        {
            capa.CompleteAction(action.Id, action.OwnerUserId, DateTimeOffset.Parse("2026-07-01T00:00:00Z"));
            Assert.Equal(action.OwnerUserId, action.CompletedByUserId);
            Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:00Z"), action.CompletedAtUtc);
        }

        capa.VerifyEffectiveness("qa-manager-001", "No recurrence in three lots", DateTimeOffset.Parse("2026-07-10T00:00:00Z"));
        capa.Close("qa-manager-001");

        Assert.Equal("closed", capa.Status);
        Assert.Equal("qa-manager-001", capa.ClosedByUserId);
    }

    [Fact]
    public void Capa_effectiveness_requires_all_corrective_and_preventive_actions_completed()
    {
        var capa = CorrectiveAction.OpenStandalone(
            "org-001",
            "env-dev",
            "CAPA-20260616-0003",
            "Recurring coating defects",
            "Hold suspect coating batches",
            ownerUserId: "qa-engineer-001",
            dueAtUtc: DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        capa.AddAction("corrective", "Adjust coating viscosity control", "process-owner-001", DateTimeOffset.Parse("2026-06-20T00:00:00Z"));
        capa.AddAction("preventive", "Add incoming viscosity audit", "qa-engineer-001", DateTimeOffset.Parse("2026-06-25T00:00:00Z"));
        var firstAction = capa.Actions[0];

        capa.CompleteAction(firstAction.Id, firstAction.OwnerUserId, DateTimeOffset.Parse("2026-06-21T00:00:00Z"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            capa.VerifyEffectiveness("qa-manager-001", "No recurrence", DateTimeOffset.Parse("2026-07-10T00:00:00Z")));
        Assert.Contains("complete", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capa_requires_corrective_or_preventive_action_before_effectiveness_verification()
    {
        var capa = CorrectiveAction.OpenStandalone(
            "org-001",
            "env-dev",
            "CAPA-20260616-0002",
            "Recurring torque defects",
            "Contain station output until torque tool check completes",
            ownerUserId: "qa-engineer-001",
            dueAtUtc: DateTimeOffset.Parse("2026-06-30T00:00:00Z"));

        Assert.Throws<InvalidOperationException>(() =>
            capa.VerifyEffectiveness("qa-manager-001", "No recurrence", DateTimeOffset.Parse("2026-07-10T00:00:00Z")));
    }
}
