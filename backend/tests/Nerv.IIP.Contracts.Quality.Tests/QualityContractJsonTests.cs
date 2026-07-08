using System.Text.Json;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Contracts.Quality.Tests;

public sealed class QualityContractJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Disposition_decided_event_serializes_quality_ncr_envelope_without_cross_service_side_effects()
    {
        var integrationEvent = new NcrDispositionDecidedIntegrationEvent(
            EventId: "evt-001",
            EventType: QualityIntegrationEventTypes.DispositionDecided,
            EventVersion: QualityIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-22T12:00:00Z"),
            SourceService: QualityIntegrationEventSources.BusinessQuality,
            CorrelationId: "corr-001",
            CausationId: "cmd-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            Actor: "user:quality-engineer-001",
            IdempotencyKey: "quality:ncr-disposition-decided:org-001:env-dev:NCR-20260522-0001",
            Payload: new NcrDispositionDecidedPayload(
                NcrId: "ncr-001",
                NcrCode: "NCR-20260522-0001",
                SkuCode: "SKU-RM-1000",
                DefectQuantity: 12.5m,
                DispositionType: "rework",
                DispositionApprovalChainId: "approval-chain-001",
                ReworkWorkOrderId: null,
                ScrapMovementId: null,
                ReturnDocumentId: null,
                ChangedAtUtc: DateTimeOffset.Parse("2026-05-22T12:00:00Z"))
            {
                SourceDocumentId = "DEF-001"
            });

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("quality.DispositionDecided", root.GetProperty("eventType").GetString());
        Assert.Equal("business-quality", root.GetProperty("sourceService").GetString());
        Assert.Equal("DEF-001", root.GetProperty("payload").GetProperty("sourceDocumentId").GetString());
        Assert.Equal("rework", root.GetProperty("payload").GetProperty("dispositionType").GetString());
        Assert.False(root.GetProperty("payload").TryGetProperty("inventoryAdjustment", out _));
        Assert.False(root.GetProperty("payload").TryGetProperty("workOrderMutation", out _));

        var deserialized = JsonSerializer.Deserialize<NcrDispositionDecidedIntegrationEvent>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("DEF-001", deserialized.Payload.SourceDocumentId);
    }

    [Fact]
    public void Spc_alert_event_serializes_quality_alert_without_industrial_alarm_contract_fields()
    {
        var integrationEvent = new SpcAlertRaisedIntegrationEvent(
            EventId: "evt-spc-001",
            EventType: QualityIntegrationEventTypes.SpcAlertRaised,
            EventVersion: QualityIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-07T08:00:00Z"),
            SourceService: QualityIntegrationEventSources.BusinessQuality,
            CorrelationId: "corr-spc-001",
            CausationId: "cmd-spc-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            Actor: "system:business-quality",
            IdempotencyKey: "quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01:trend-increasing",
            Payload: new SpcAlertRaisedPayload(
                AlertKey: "quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01",
                ResourceType: "quality-spc-alert",
                SkuCode: "SKU-RM-1000",
                CharacteristicCode: "length",
                WorkCenterId: "WC-MIX-01",
                RuleCodes: [QualitySpcRuleCodes.TrendIncreasing],
                Severity: "warning",
                LatestMeasuredAtUtc: DateTimeOffset.Parse("2026-07-07T07:59:00Z"),
                Summary: "SPC trend detected for SKU-RM-1000 length at WC-MIX-01."));

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("quality.SpcAlertRaised", root.GetProperty("eventType").GetString());
        Assert.Equal("business-quality", root.GetProperty("sourceService").GetString());
        var payload = root.GetProperty("payload");
        Assert.Equal("quality-spc-alert", payload.GetProperty("resourceType").GetString());
        Assert.Equal("trend-increasing", payload.GetProperty("ruleCodes")[0].GetString());
        Assert.False(payload.TryGetProperty("deviceAssetId", out _));
        Assert.False(payload.TryGetProperty("alarmCode", out _));

        var deserialized = JsonSerializer.Deserialize<SpcAlertRaisedIntegrationEvent>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("WC-MIX-01", deserialized.Payload.WorkCenterId);
    }
}
