using System.Text.Json;
using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.Contracts.MasterData.Tests;

public sealed class MasterDataContractJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Sku_changed_event_serializes_with_adr0011_envelope_shape()
    {
        var integrationEvent = new SkuChangedIntegrationEvent(
            EventId: "evt-001",
            EventType: MasterDataIntegrationEventTypes.SkuChanged,
            EventVersion: MasterDataIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-21T12:00:00Z"),
            SourceService: MasterDataIntegrationEventSources.BusinessMasterData,
            CorrelationId: "corr-001",
            CausationId: "cmd-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            Actor: "user:planner-001",
            IdempotencyKey: "masterdata:sku-changed:org-001:env-dev:SKU-FG-1000",
            Payload: new MasterDataChangedPayload("sku", "SKU-FG-1000", "active", DateTimeOffset.Parse("2026-05-21T12:00:00Z")));

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("evt-001", root.GetProperty("eventId").GetString());
        Assert.Equal("masterData.SkuChanged", root.GetProperty("eventType").GetString());
        Assert.Equal(1, root.GetProperty("eventVersion").GetInt32());
        Assert.Equal("business-masterdata", root.GetProperty("sourceService").GetString());
        Assert.Equal("corr-001", root.GetProperty("correlationId").GetString());
        Assert.Equal("cmd-001", root.GetProperty("causationId").GetString());
        Assert.Equal("user:planner-001", root.GetProperty("actor").GetString());
        Assert.Equal("SKU-FG-1000", root.GetProperty("payload").GetProperty("code").GetString());
    }
}
