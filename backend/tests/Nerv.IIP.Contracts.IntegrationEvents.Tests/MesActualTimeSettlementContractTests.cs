using System.Text.Json;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

public sealed class MesActualTimeSettlementContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Settlement_v2_json_distinguishes_available_zero_from_unavailable()
    {
        var completedAtUtc = DateTimeOffset.Parse("2026-08-26T03:00:00Z");
        var integrationEvent = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-settled-2", MesIntegrationEventTypes.OperationActualTimeSettled,
            MesIntegrationEventVersions.V2, completedAtUtc, MesIntegrationEventSources.BusinessMes,
            "corr-settled-2", "cause-settled-2", "org-001", "env-dev", "system:mes",
            "operation-actual-time-settled:org-001:env-dev:OP-001:3",
            new OperationActualTimeSettledPayload(
                "WO-001", "OP-001", "WC-001", 3, completedAtUtc,
                0, 0, [], "DEVICE-001", MesMachineTimeFactStatus.Available, 0,
                MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1));

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<MesOperationActualTimeSettledIntegrationEvent>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(MesIntegrationEventVersions.V2, roundTripped.EventVersion);
        Assert.Equal("DEVICE-001", roundTripped.Payload.DeviceAssetId);
        Assert.Equal(MesMachineTimeFactStatus.Available, roundTripped.Payload.MachineTimeStatus);
        Assert.Contains("\"machineTimeStatus\":\"available\"", json, StringComparison.Ordinal);
        Assert.Equal(0, roundTripped.Payload.BillableMachineTicks);
        Assert.Equal(MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1, roundTripped.Payload.MachineTimeBasisCode);
        Assert.Contains("\"billableMachineTicks\":0", json, StringComparison.Ordinal);

        var unavailableEvent = integrationEvent with
        {
            EventId = "evt-settled-unavailable-2",
            Payload = integrationEvent.Payload with
            {
                DeviceAssetId = null,
                MachineTimeStatus = MesMachineTimeFactStatus.Unavailable,
                BillableMachineTicks = null,
                MachineTimeBasisCode = null,
            },
        };
        var unavailableJson = JsonSerializer.Serialize(unavailableEvent, JsonOptions);
        var unavailableRoundTripped = JsonSerializer.Deserialize<MesOperationActualTimeSettledIntegrationEvent>(unavailableJson, JsonOptions);

        Assert.NotNull(unavailableRoundTripped);
        Assert.Equal(MesMachineTimeFactStatus.Unavailable, unavailableRoundTripped.Payload.MachineTimeStatus);
        Assert.Null(unavailableRoundTripped.Payload.BillableMachineTicks);
        Assert.Contains("\"machineTimeStatus\":\"unavailable\"", unavailableJson, StringComparison.Ordinal);
        Assert.Contains("\"billableMachineTicks\":null", unavailableJson, StringComparison.Ordinal);

        var notApplicableJson = JsonSerializer.Serialize(
            unavailableEvent with
            {
                EventId = "evt-settled-not-applicable-2",
                Payload = unavailableEvent.Payload with
                {
                    MachineTimeStatus = MesMachineTimeFactStatus.NotApplicable,
                },
            },
            JsonOptions);
        var notApplicableRoundTripped = JsonSerializer.Deserialize<MesOperationActualTimeSettledIntegrationEvent>(
            notApplicableJson,
            JsonOptions);

        Assert.NotNull(notApplicableRoundTripped);
        Assert.Equal(MesMachineTimeFactStatus.NotApplicable, notApplicableRoundTripped.Payload.MachineTimeStatus);
        Assert.Contains("\"machineTimeStatus\":\"notApplicable\"", notApplicableJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_v1_json_without_machine_fact_fields_remains_readable_as_unavailable()
    {
        const string json = """
            {"eventId":"evt-v1","eventType":"mes.OperationActualTimeSettled","eventVersion":1,"occurredAtUtc":"2026-08-26T03:00:00Z","sourceService":"business-mes","correlationId":"corr-v1","causationId":"cause-v1","organizationId":"org-001","environmentId":"env-dev","actor":"system:mes","idempotencyKey":"idem-v1","payload":{"workOrderId":"WO-001","operationTaskId":"OP-001","workCenterId":"WC-001","settlementRevision":1,"completedAtUtc":"2026-08-26T03:00:00Z","actualLaborTicks":36000000000,"actualMachineTicks":36000000000,"coveredProductionReportNos":[]}}
            """;

        var roundTripped = JsonSerializer.Deserialize<MesOperationActualTimeSettledIntegrationEvent>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(MesIntegrationEventVersions.V1, roundTripped.EventVersion);
        Assert.Null(roundTripped.Payload.DeviceAssetId);
        Assert.Null(roundTripped.Payload.MachineTimeStatus);
        Assert.Null(roundTripped.Payload.BillableMachineTicks);
        Assert.Null(roundTripped.Payload.MachineTimeBasisCode);
    }

    [Fact]
    public void Void_v2_json_preserves_the_original_billable_machine_snapshot()
    {
        var completedAtUtc = DateTimeOffset.Parse("2026-08-26T03:00:00Z");
        var voidedAtUtc = completedAtUtc.AddMinutes(10);
        var integrationEvent = new MesOperationActualTimeSettlementVoidedIntegrationEvent(
            "evt-voided-2", MesIntegrationEventTypes.OperationActualTimeSettlementVoided,
            MesIntegrationEventVersions.V2, voidedAtUtc, MesIntegrationEventSources.BusinessMes,
            "corr-voided-2", "cause-voided-2", "org-001", "env-dev", "system:mes",
            "operation-actual-time-settlement-voided:org-001:env-dev:OP-001:3",
            new OperationActualTimeSettlementVoidedPayload(
                "WO-001", "OP-001", "WC-001", 3, completedAtUtc, voidedAtUtc,
                36_000_000_000, 36_000_000_000, [], "DEVICE-001",
                MesMachineTimeFactStatus.Available, 36_000_000_000,
                MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1));

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<MesOperationActualTimeSettlementVoidedIntegrationEvent>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal("DEVICE-001", roundTripped.Payload.DeviceAssetId);
        Assert.Equal(MesMachineTimeFactStatus.Available, roundTripped.Payload.MachineTimeStatus);
        Assert.Equal(36_000_000_000, roundTripped.Payload.BillableMachineTicks);
        Assert.Equal(MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1, roundTripped.Payload.MachineTimeBasisCode);
    }

    [Fact]
    public void Settlement_v2_rejects_numeric_machine_time_status()
    {
        const string json = """
            {"eventId":"evt-v2","eventType":"mes.OperationActualTimeSettled","eventVersion":2,"occurredAtUtc":"2026-08-26T03:00:00Z","sourceService":"business-mes","correlationId":"corr-v2","causationId":"cause-v2","organizationId":"org-001","environmentId":"env-dev","actor":"system:mes","idempotencyKey":"idem-v2","payload":{"workOrderId":"WO-001","operationTaskId":"OP-001","workCenterId":"WC-001","settlementRevision":1,"completedAtUtc":"2026-08-26T03:00:00Z","actualLaborTicks":0,"actualMachineTicks":0,"coveredProductionReportNos":[],"machineTimeStatus":0}}
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<MesOperationActualTimeSettledIntegrationEvent>(json, JsonOptions));
    }

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
        Assert.Equal(36_000_000_000, roundTripped.Payload.ActualMachineTicks);
        Assert.Equal(["PR-001", "PR-002"], roundTripped.Payload.CoveredProductionReportNos);
        Assert.Contains("\"voidedAtUtc\":\"2026-08-26T03:10:00+00:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"actualMachineTicks\":36000000000", json, StringComparison.Ordinal);
    }
}
