using System.Text.Json;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed partial class MesInventoryProducedLotPostgresRedisAcceptanceTests
{
    [Fact]
    public void Cap_transport_evidence_matches_a_typed_cap_message_envelope()
    {
        const string eventId = "evt-typed-cap-envelope";
        const string idempotencyKey = "idem-typed-cap-envelope";
        const string topic = nameof(InventoryMovementRequestedIntegrationEvent);
        const string eventType = InventoryIntegrationEventTypes.InventoryMovementRequested;
        var integrationEvent = new InventoryMovementRequestedIntegrationEvent(
            eventId,
            eventType,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UnixEpoch,
            InventoryIntegrationEventSources.BusinessMes,
            "corr-typed-cap-envelope",
            "cause-typed-cap-envelope",
            "org-typed-cap-envelope",
            "env-typed-cap-envelope",
            "system:acceptance-probe",
            idempotencyKey,
            new InventoryMovementRequestedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                "FGR-typed-cap-envelope",
                "WO-typed-cap-envelope",
                idempotencyKey,
                "SKU-typed-cap-envelope",
                "EA",
                "finished-goods",
                "receiving",
                "LOT-typed-cap-envelope",
                null,
                InventoryQualityStatuses.Unrestricted,
                "production",
                null,
                1m,
                DateTimeOffset.UnixEpoch,
                UnitCostAuthorityReference: InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt));
        var capContent = JsonSerializer.Serialize(new DotNetCore.CAP.Messages.Message(
            new Dictionary<string, string?> { ["cap-msg-name"] = topic },
            integrationEvent));

        Assert.True(ContentMatchesEvent(capContent, eventId, idempotencyKey, topic, eventType, topic));
    }
}
