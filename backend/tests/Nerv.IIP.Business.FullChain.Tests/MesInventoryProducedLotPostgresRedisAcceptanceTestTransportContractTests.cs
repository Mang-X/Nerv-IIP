using System.Text;
using System.Text.Json;
using Nerv.IIP.Contracts.Inventory;
using StackExchange.Redis;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed partial class MesInventoryProducedLotPostgresRedisAcceptanceTests
{
    [Fact]
    public void Cap_transport_evidence_parses_official_pg_envelope_and_redis_body_shapes()
    {
        const string eventId = "evt-parser-shape";
        const string idempotencyKey = "idem-parser-shape";
        const string topic = nameof(InventoryMovementRequestedIntegrationEvent);
        const string eventType = InventoryIntegrationEventTypes.InventoryMovementRequested;
        const string payload = "{\"EventId\":\"evt-parser-shape\",\"IdempotencyKey\":\"idem-parser-shape\",\"EventType\":\"inventory.InventoryMovementRequested\"}";

        using var payloadDocument = JsonDocument.Parse(payload);
        var capContent = JsonSerializer.Serialize(new
        {
            Headers = new Dictionary<string, string> { ["cap-msg-name"] = topic },
            Value = payloadDocument.RootElement,
        });
        Assert.True(ContentMatchesEvent(
            capContent,
            eventId,
            idempotencyKey,
            topic,
            eventType,
            tableName: topic));
        Assert.False(ContentMatchesEvent(
            capContent,
            eventId,
            idempotencyKey,
            "OtherTopic",
            eventType,
            tableName: "OtherTopic"));

        var capStringValue = JsonSerializer.Serialize(new
        {
            Headers = new Dictionary<string, string> { ["cap-msg-name"] = topic },
            Value = payload,
        });
        Assert.True(ContentMatchesEvent(
            capStringValue,
            eventId,
            idempotencyKey,
            topic,
            eventType,
            tableName: topic));

        var capBase64Value = JsonSerializer.Serialize(new
        {
            Headers = new Dictionary<string, string> { ["cap-msg-name"] = topic },
            Value = JsonSerializer.Serialize(Encoding.UTF8.GetBytes(payload)),
        });
        Assert.True(ContentMatchesEvent(
            capBase64Value,
            eventId,
            idempotencyKey,
            topic,
            eventType,
            tableName: topic));

        Assert.True(ContentMatchesEvent(
            JsonSerializer.Serialize(capContent),
            eventId,
            idempotencyKey,
            topic,
            eventType,
            tableName: topic));

        var utf8Payload = Encoding.UTF8.GetBytes(payload);
        var redisHeaders = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["cap-msg-name"] = topic,
        });
        var redisBase64Body = JsonSerializer.Serialize(utf8Payload);
        var redisArrayBody = JsonSerializer.Serialize(utf8Payload.Select(static value => (int)value).ToArray());
        var entry = new StreamEntry(
            "1-0",
            [
                new NameValueEntry("headers", redisHeaders),
                new NameValueEntry("body", redisBase64Body),
            ]);

        Assert.Equal(payload, RedisValueToUtf8(redisBase64Body));
        Assert.Equal(payload, RedisValueToUtf8(redisArrayBody));
        Assert.Equal(payload, RedisValueToUtf8(payload));
        Assert.True(RedisStreamEntryMatchesEvent(entry, eventId, idempotencyKey, topic, eventType));
        Assert.False(RedisStreamEntryMatchesEvent(entry, eventId, idempotencyKey, "OtherTopic", eventType));
    }

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

    [Fact]
    public void Cap_received_pending_evidence_requires_failed_retry_for_the_exact_group()
    {
        const string eventId = "evt-pending-cap-row";
        const string group = "business-inventory.movement-requested.v1";
        var observed = new Dictionary<string, CapReceivedEventFact>(StringComparer.Ordinal);

        Assert.False(CaptureObservedPendingCapReceivedFact(
            new CapReceivedEventFact(eventId, 1L, nameof(InventoryMovementRequestedIntegrationEvent), group, "Scheduled", 0),
            eventId,
            group,
            observed));
        Assert.True(CaptureObservedPendingCapReceivedFact(
            new CapReceivedEventFact(eventId, 1L, nameof(InventoryMovementRequestedIntegrationEvent), group, "Failed", 2),
            eventId,
            group,
            observed));
    }
}
