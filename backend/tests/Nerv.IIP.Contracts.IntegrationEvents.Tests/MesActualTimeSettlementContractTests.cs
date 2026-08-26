using System.Text.Json;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

public sealed class MesActualTimeSettlementContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Settlement_v1_json_preserves_revision_ticks_and_covered_report_numbers()
    {
        var completedAtUtc = DateTimeOffset.Parse("2026-08-26T03:00:00Z");
        var integrationEvent = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-settled-1",
            MesIntegrationEventTypes.OperationActualTimeSettled,
            MesIntegrationEventVersions.V1,
            completedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            "corr-settled-1",
            "cause-settled-1",
            "org-001",
            "env-dev",
            "system:mes",
            "operation-actual-time-settled:org-001:env-dev:OP-001:2",
            new OperationActualTimeSettledPayload(
                "WO-001",
                "OP-001",
                "WC-001",
                2,
                completedAtUtc,
                72_000_000_000,
                36_000_000_000,
                ["PR-001", "PR-002"]));

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<MesOperationActualTimeSettledIntegrationEvent>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(MesIntegrationEventVersions.V1, roundTripped.EventVersion);
        Assert.Equal(2, roundTripped.Payload.SettlementRevision);
        Assert.Equal(72_000_000_000, roundTripped.Payload.ActualLaborTicks);
        Assert.Equal(36_000_000_000, roundTripped.Payload.ActualMachineTicks);
        Assert.Equal(["PR-001", "PR-002"], roundTripped.Payload.CoveredProductionReportNos);
        Assert.Contains("\"settlementRevision\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"actualLaborTicks\":72000000000", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Void_v1_json_references_the_original_settlement_snapshot()
    {
        var completedAtUtc = DateTimeOffset.Parse("2026-08-26T03:00:00Z");
        var voidedAtUtc = completedAtUtc.AddMinutes(10);
        var integrationEvent = new MesOperationActualTimeSettlementVoidedIntegrationEvent(
            "evt-voided-1",
            MesIntegrationEventTypes.OperationActualTimeSettlementVoided,
            MesIntegrationEventVersions.V1,
            voidedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            "corr-voided-1",
            "cause-voided-1",
            "org-001",
            "env-dev",
            "system:mes",
            "operation-actual-time-settlement-voided:org-001:env-dev:OP-001:2",
            new OperationActualTimeSettlementVoidedPayload(
                "WO-001",
                "OP-001",
                "WC-001",
                2,
                completedAtUtc,
                voidedAtUtc,
                72_000_000_000,
                36_000_000_000,
                ["PR-001", "PR-002"]));

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<MesOperationActualTimeSettlementVoidedIntegrationEvent>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped.Payload.SettlementRevision);
        Assert.Equal(completedAtUtc, roundTripped.Payload.CompletedAtUtc);
        Assert.Equal(voidedAtUtc, roundTripped.Payload.VoidedAtUtc);
        Assert.Equal(72_000_000_000, roundTripped.Payload.ActualLaborTicks);
        Assert.Equal(["PR-001", "PR-002"], roundTripped.Payload.CoveredProductionReportNos);
        Assert.Contains("\"voidedAtUtc\":\"2026-08-26T03:10:00+00:00\"", json, StringComparison.Ordinal);
    }
}
