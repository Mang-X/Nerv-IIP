using System.Data;
using System.Text;
using System.Text.Json;
using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;
using StackExchange.Redis;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;


public sealed partial class MesInventoryProducedLotPostgresRedisAcceptanceTests
{
    private static async Task<MessagingFacts> ReadMessagingFactsAsync(
        string mesConnectionString,
        string inventoryConnectionString,
        string redisConnectionString,
        string capVersion,
        string pendingEventId,
        string pendingEventIdempotencyKey,
        IReadOnlyDictionary<string, string> eventIdempotencyKeys,
        params string[] eventIds)
    {
        var eventFacts = new Dictionary<string, EventMessageFact>(StringComparer.Ordinal);
        await using (var mesConnection = new NpgsqlConnection(mesConnectionString))
        {
            await mesConnection.OpenAsync();
            foreach (var eventId in eventIds.Distinct(StringComparer.Ordinal))
            {
                await using var published = mesConnection.CreateCommand();
                published.CommandText = "SELECT \"Name\", \"Content\" FROM mes.cap_published_messages WHERE \"Content\" IS NOT NULL;";
                var publishedCount = 0L;
                await using var publishedReader = await published.ExecuteReaderAsync();
                while (await publishedReader.ReadAsync())
                {
                    if (ContentMatchesEvent(
                        publishedReader.GetString(1),
                        eventId,
                        eventIdempotencyKeys[eventId],
                        nameof(InventoryMovementRequestedIntegrationEvent),
                        InventoryIntegrationEventTypes.InventoryMovementRequested,
                        publishedReader.IsDBNull(0) ? null : publishedReader.GetString(0)))
                    {
                        publishedCount++;
                    }
                }
                eventFacts[eventId] = new EventMessageFact(publishedCount, 0L);
            }
        }

        long inventoryDeadLetterCount;
        string? pendingAuthorityStatus = null;
        string? pendingAuthorityReason = null;
        await using (var inventoryConnection = new NpgsqlConnection(inventoryConnectionString))
        {
            await inventoryConnection.OpenAsync();
            foreach (var eventId in eventIds.Distinct(StringComparer.Ordinal))
            {
                await using var consumed = inventoryConnection.CreateCommand();
                consumed.CommandText = "SELECT \"Name\", \"Content\" FROM inventory.cap_received_messages WHERE \"Content\" IS NOT NULL;";
                var receivedCount = 0L;
                await using var receivedReader = await consumed.ExecuteReaderAsync();
                while (await receivedReader.ReadAsync())
                {
                    if (ContentMatchesEvent(
                        receivedReader.GetString(1),
                        eventId,
                        eventIdempotencyKeys[eventId],
                        nameof(InventoryMovementRequestedIntegrationEvent),
                        InventoryIntegrationEventTypes.InventoryMovementRequested,
                        receivedReader.IsDBNull(0) ? null : receivedReader.GetString(0)))
                    {
                        receivedCount++;
                    }
                }
                eventFacts[eventId] = eventFacts[eventId] with { ReceivedCount = receivedCount };
            }
            await using var deadLetters = inventoryConnection.CreateCommand();
            deadLetters.CommandText = "SELECT COUNT(*) FROM inventory.integration_event_dead_letters WHERE event_id = @event_id;";
            deadLetters.Parameters.AddWithValue("event_id", pendingEventId);
            inventoryDeadLetterCount = (long)(await deadLetters.ExecuteScalarAsync() ?? 0L);
            await using var authority = inventoryConnection.CreateCommand();
            authority.CommandText = """
                SELECT status, reason_code FROM inventory.authority_resolution_pending_audits
                WHERE event_id = @pending_event_id LIMIT 1;
                """;
            authority.Parameters.AddWithValue("pending_event_id", pendingEventId);
            await using var reader = await authority.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                pendingAuthorityStatus = reader.GetString(0);
                pendingAuthorityReason = reader.GetString(1);
            }
        }

        var pendingEventRedisFact = await ReadPendingRedisFactAsync(
            redisConnectionString,
            capVersion,
            pendingEventId,
            pendingEventIdempotencyKey);

        return new MessagingFacts(
            eventFacts,
            pendingEventRedisFact,
            pendingAuthorityStatus,
            pendingAuthorityReason,
            inventoryDeadLetterCount);
    }

    private static bool CaptureObservedTransportFacts(
        MessagingFacts messaging,
        IReadOnlySet<string> requiredEventIds,
        IDictionary<string, EventMessageFact> observedFacts)
    {
        foreach (var eventId in requiredEventIds)
        {
            if (messaging.EventFacts.TryGetValue(eventId, out var fact) &&
                fact.PublishedCount == 1L && fact.ReceivedCount == 1L)
            {
                observedFacts[eventId] = fact;
            }
        }

        return observedFacts.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(requiredEventIds);
    }

    private static void AssertEventTransport(
        IReadOnlyDictionary<string, EventMessageFact> eventFacts,
        string eventId)
    {
        var eventFact = eventFacts[eventId];
        Assert.Equal(1L, eventFact.PublishedCount);
        Assert.Equal(1L, eventFact.ReceivedCount);
    }

    private static async Task<PendingRedisFact> ReadPendingRedisFactAsync(
        string redisConnectionString,
        string capVersion,
        string eventId,
        string idempotencyKey)
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var database = connection.GetDatabase();
        var stream = (RedisKey)nameof(InventoryMovementRequestedIntegrationEvent);
        var group = (RedisValue)$"business-inventory.movement-requested.{capVersion}";
        var pending = await database.StreamPendingMessagesAsync(
            stream,
            group,
            100,
            RedisValue.Null,
            null,
            null);
        foreach (var pendingMessage in pending)
        {
            var entries = await database.StreamRangeAsync(
                stream,
                pendingMessage.MessageId,
                pendingMessage.MessageId,
                1);
            if (entries.Any(entry => RedisStreamEntryMatchesEvent(
                    entry,
                    eventId,
                    idempotencyKey,
                    nameof(InventoryMovementRequestedIntegrationEvent),
                    InventoryIntegrationEventTypes.InventoryMovementRequested)))
            {
                return new PendingRedisFact(
                    true,
                    eventId,
                    idempotencyKey,
                    pendingMessage.MessageId.ToString(),
                    pendingMessage.ConsumerName.ToString(),
                    pendingMessage.DeliveryCount);
            }
        }

        return new PendingRedisFact(false, null, null, null, null, 0);
    }

    private static bool ContentMatchesEvent(
        string content,
        string eventId,
        string idempotencyKey,
        string expectedTopic,
        string expectedEventType,
        string? tableName = null)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonMessageMatches(
                document.RootElement,
                eventId,
                idempotencyKey,
                expectedTopic,
                expectedEventType,
                tableName,
                headerTopic: null);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool RedisStreamEntryMatchesEvent(
        StreamEntry entry,
        string eventId,
        string idempotencyKey,
        string expectedTopic,
        string expectedEventType)
    {
        RedisValue headers = RedisValue.Null;
        RedisValue body = RedisValue.Null;
        var hasBody = false;
        foreach (var field in entry.Values)
        {
            if (field.Name.ToString().Equals("headers", StringComparison.OrdinalIgnoreCase))
            {
                headers = field.Value;
            }
            else if (field.Name.ToString().Equals("body", StringComparison.OrdinalIgnoreCase))
            {
                body = field.Value;
                hasBody = true;
            }
        }

        if (!hasBody)
        {
            return false;
        }

        try
        {
            using var bodyDocument = JsonDocument.Parse(RedisValueToUtf8(body));
            return JsonMessageMatches(
                bodyDocument.RootElement,
                eventId,
                idempotencyKey,
                expectedTopic,
                expectedEventType,
                tableName: null,
                headerTopic: ReadHeaderTopicJson(RedisValueToUtf8(headers)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string RedisValueToUtf8(RedisValue value)
    {
        var bytes = (byte[]?)value;
        if (bytes is null)
        {
            return string.Empty;
        }

        var serializedBytes = Encoding.UTF8.GetString(bytes);
        try
        {
            using var document = JsonDocument.Parse(serializedBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return serializedBytes;
            }

            var decodedBytes = new List<byte>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetInt32(out var byteValue) || byteValue is < byte.MinValue or > byte.MaxValue)
                {
                    return serializedBytes;
                }

                decodedBytes.Add((byte)byteValue);
            }

            return Encoding.UTF8.GetString(decodedBytes.ToArray());
        }
        catch (JsonException)
        {
            return serializedBytes;
        }
    }

    private static bool JsonMessageMatches(
        JsonElement element,
        string eventId,
        string idempotencyKey,
        string expectedTopic,
        string expectedEventType,
        string? tableName,
        string? headerTopic)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var messageHeaderTopic = ReadHeaderTopic(element) ?? headerTopic;
        if (JsonObjectMatches(
                element,
                eventId,
                idempotencyKey,
                expectedTopic,
                expectedEventType,
                tableName,
                messageHeaderTopic))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) &&
                !property.Name.Equals("Content", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (JsonPayloadMatches(
                    property.Value,
                    eventId,
                    idempotencyKey,
                    expectedTopic,
                    expectedEventType,
                    tableName,
                    messageHeaderTopic))
            {
                return true;
            }
        }

        return false;
    }

    private static bool JsonPayloadMatches(
        JsonElement element,
        string eventId,
        string idempotencyKey,
        string expectedTopic,
        string expectedEventType,
        string? tableName,
        string? headerTopic)
    {
        if (element.ValueKind == JsonValueKind.String &&
            element.GetString() is { } nested &&
            TryParseJson(nested, out var nestedDocument))
        {
            using (nestedDocument)
            {
                return JsonPayloadMatches(
                    nestedDocument!.RootElement,
                    eventId,
                    idempotencyKey,
                    expectedTopic,
                    expectedEventType,
                    tableName,
                    headerTopic);
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var payloadHeaderTopic = ReadHeaderTopic(element) ?? headerTopic;
        if (JsonObjectMatches(
                element,
                eventId,
                idempotencyKey,
                expectedTopic,
                expectedEventType,
                tableName,
                payloadHeaderTopic))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if ((property.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
                 property.Name.Equals("Content", StringComparison.OrdinalIgnoreCase)) &&
                JsonPayloadMatches(
                    property.Value,
                    eventId,
                    idempotencyKey,
                    expectedTopic,
                    expectedEventType,
                    tableName,
                    payloadHeaderTopic))
            {
                return true;
            }
        }

        return false;
    }

    private static bool JsonObjectMatches(
        JsonElement element,
        string eventId,
        string idempotencyKey,
        string expectedTopic,
        string expectedEventType,
        string? tableName,
        string? headerTopic)
    {
        var properties = element.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.OrdinalIgnoreCase);
        var candidateEventId = ReadDirectJsonString(properties, "EventId");
        var candidateIdempotencyKey = ReadDirectJsonString(properties, "IdempotencyKey");
        var candidateEventType = ReadDirectJsonString(properties, "EventType");
        var topicMatches = (tableName is null || tableName == expectedTopic) &&
            (headerTopic is null || headerTopic == expectedTopic) &&
            (tableName == expectedTopic || headerTopic == expectedTopic);

        return candidateEventId == eventId &&
            candidateIdempotencyKey == idempotencyKey &&
            candidateEventType == expectedEventType &&
            topicMatches;
    }

    private static string? ReadHeaderTopic(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("Headers", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    return ReadDirectJsonString(property.Value, "cap-msg-name");
                }

                if (property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() is { } serializedHeaders &&
                    TryParseJson(serializedHeaders, out var headersDocument))
                {
                    using (headersDocument)
                    {
                        return ReadHeaderTopic(headersDocument!.RootElement);
                    }
                }
            }

            if (property.Name.Equals("cap-msg-name", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string? ReadHeaderTopicJson(string serializedHeaders)
    {
        if (!TryParseJson(serializedHeaders, out var headersDocument))
        {
            return null;
        }

        using (headersDocument)
        {
            return ReadHeaderTopic(headersDocument!.RootElement);
        }
    }

    private static string? ReadDirectJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string? ReadDirectJsonString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string propertyName) =>
        properties.TryGetValue(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryParseJson(string value, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }


}
