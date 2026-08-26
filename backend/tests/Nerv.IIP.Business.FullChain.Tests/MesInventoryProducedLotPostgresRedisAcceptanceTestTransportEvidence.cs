using System.Collections.Concurrent;
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
        string inventoryConnectionString,
        string redisConnectionString,
        string capVersion,
        string pendingEventId,
        string pendingEventIdempotencyKey,
        IReadOnlyDictionary<string, string> eventIdempotencyKeys,
        InventoryReceivedMessageSnapshot receivedMessages,
        params string[] eventIds)
    {
        // CAP PostgreSQL retains the received row through its expiry window and updates StatusName/Retries
        // as subscriber execution retries. Published evidence comes from Redis stream history; received and
        // pending evidence comes from exact CAP row identity/content/status snapshots.
        var eventFacts = await ReadRedisStreamHistoryFactsAsync(
            redisConnectionString,
            eventIdempotencyKeys,
            eventIds);
        receivedMessages.ThrowIfFaulted();
        var pendingCapReceivedFact = receivedMessages.GetReceivedFact(pendingEventId);
        foreach (var eventId in eventIds.Distinct(StringComparer.Ordinal))
        {
            eventFacts[eventId] = eventFacts[eventId] with
            {
                ReceivedCount = receivedMessages.GetReceivedCount(eventId),
            };
        }

        long inventoryDeadLetterCount;
        string? pendingAuthorityStatus = null;
        string? pendingAuthorityReason = null;
        await using (var inventoryConnection = new NpgsqlConnection(inventoryConnectionString))
        {
            await inventoryConnection.OpenAsync();
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
            pendingCapReceivedFact,
            pendingAuthorityStatus,
            pendingAuthorityReason,
            inventoryDeadLetterCount);
    }

    private static async Task<Dictionary<string, EventMessageFact>> ReadRedisStreamHistoryFactsAsync(
        string redisConnectionString,
        IReadOnlyDictionary<string, string> eventIdempotencyKeys,
        IEnumerable<string> eventIds)
    {
        var eventFacts = eventIds
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                eventId => eventId,
                static _ => new EventMessageFact(0L, 0L),
                StringComparer.Ordinal);
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var database = connection.GetDatabase();
        var stream = (RedisKey)nameof(InventoryMovementRequestedIntegrationEvent);
        var entries = await database.StreamRangeAsync(stream, "-", "+");
        foreach (var entry in entries)
        {
            foreach (var (eventId, idempotencyKey) in eventIdempotencyKeys)
            {
                if (!eventFacts.ContainsKey(eventId) ||
                    !RedisStreamEntryMatchesEvent(
                        entry,
                        eventId,
                        idempotencyKey,
                        nameof(InventoryMovementRequestedIntegrationEvent),
                        InventoryIntegrationEventTypes.InventoryMovementRequested))
                {
                    continue;
                }

                eventFacts[eventId] = eventFacts[eventId] with
                {
                    PublishedCount = eventFacts[eventId].PublishedCount + 1L,
                };
            }
        }

        return eventFacts;
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

    internal static bool CaptureObservedPendingCapReceivedFact(
        CapReceivedEventFact? pendingFact,
        string expectedEventId,
        string expectedGroup,
        IDictionary<string, CapReceivedEventFact> observedFacts)
    {
        if (pendingFact is not null &&
            pendingFact.EventId == expectedEventId &&
            pendingFact.Name == nameof(InventoryMovementRequestedIntegrationEvent) &&
            pendingFact.Group == expectedGroup &&
            pendingFact.StatusName == "Failed" &&
            pendingFact.RetryCount >= 1)
        {
            observedFacts[expectedEventId] = pendingFact;
        }

        return observedFacts.ContainsKey(expectedEventId);
    }

    internal static void AssertEventTransport(
        IReadOnlyDictionary<string, EventMessageFact> eventFacts,
        string eventId)
    {
        var eventFact = eventFacts[eventId];
        Assert.Equal(1L, eventFact.PublishedCount);
        Assert.Equal(1L, eventFact.ReceivedCount);
    }

    internal sealed class InventoryReceivedMessageSnapshot : IAsyncDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

        private readonly string connectionString;
        private readonly string capVersion;
        private readonly string consumerGroup;
        private readonly IReadOnlyDictionary<string, string> eventIdempotencyKeys;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, CapReceivedEventFact>> receivedRowsByEventId =
            new(StringComparer.Ordinal);
        private readonly CancellationTokenSource stop = new();
        private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task captureTask;

        private InventoryReceivedMessageSnapshot(
            string connectionString,
            string capVersion,
            string consumerGroup,
            IReadOnlyDictionary<string, string> eventIdempotencyKeys)
        {
            this.connectionString = connectionString;
            this.capVersion = capVersion;
            this.consumerGroup = consumerGroup;
            this.eventIdempotencyKeys = eventIdempotencyKeys.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            captureTask = CaptureLoopAsync();
        }

        public static async Task<InventoryReceivedMessageSnapshot> StartAsync(
            string connectionString,
            string capVersion,
            string consumerGroup,
            IReadOnlyDictionary<string, string> eventIdempotencyKeys)
        {
            var snapshot = new InventoryReceivedMessageSnapshot(
                connectionString,
                capVersion,
                consumerGroup,
                eventIdempotencyKeys);
            try
            {
                await snapshot.ready.Task.ConfigureAwait(false);
                return snapshot;
            }
            catch
            {
                await snapshot.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public long GetReceivedCount(string eventId)
        {
            ThrowIfFaulted();
            return receivedRowsByEventId.TryGetValue(eventId, out var rows)
                ? CountReceivedRows(rows)
                : 0L;
        }

        public CapReceivedEventFact? GetReceivedFact(string eventId)
        {
            ThrowIfFaulted();
            return receivedRowsByEventId.TryGetValue(eventId, out var rows) && rows.Count == 1
                ? rows.Values.Single()
                : null;
        }

        public static long CountReceivedRows<T>(IReadOnlyDictionary<long, T>? rows) => rows?.Count ?? 0L;

        public void ThrowIfFaulted()
        {
            if (captureTask.IsFaulted)
            {
                throw new InvalidOperationException(
                    "CAP PostgreSQL Inventory received-message snapshot failed; received evidence is unavailable.",
                    captureTask.Exception?.GetBaseException());
            }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            try
            {
                await captureTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }
            finally
            {
                stop.Dispose();
            }
        }

        private async Task CaptureLoopAsync()
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stop.Token);
                // Complete the baseline capture before the caller is allowed to publish. This prevents
                // a first received row from racing the snapshot startup.
                await CaptureOnceAsync(connection, stop.Token);
                ready.TrySetResult(true);
                using var pollTimer = new PeriodicTimer(PollInterval);
                while (await pollTimer.WaitForNextTickAsync(stop.Token))
                {
                    await CaptureOnceAsync(connection, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                ready.TrySetCanceled(stop.Token);
                throw;
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
                throw;
            }
        }

        private async Task CaptureOnceAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "Id", "Version", "Name", "Group", "Content", "Retries", "StatusName"
                FROM "cap"."received"
                WHERE "Version" = @cap_version
                  AND "Group" = @consumer_group
                  AND "Content" IS NOT NULL;
                """;
            // Business Inventory registers DotNetCore.CAP.PostgreSql.UseEntityFramework. Its official
            // storage uses EFOptions' default schema/table ("cap"."received"). The inventory
            // cap_received_messages table is the separately mapped NetCorePal projection and is not the
            // CAP storage used by this production process; querying it yields a false all-zero result.
            command.Parameters.AddWithValue("cap_version", capVersion);
            command.Parameters.AddWithValue("consumer_group", consumerGroup);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var rowId = reader.GetInt64(0);
                var tableName = reader.IsDBNull(2) ? null : reader.GetString(2);
                var group = reader.IsDBNull(3) ? null : reader.GetString(3);
                var content = reader.GetString(4);
                var retryCount = reader.GetInt32(5);
                var statusName = reader.GetString(6);
                foreach (var (eventId, idempotencyKey) in eventIdempotencyKeys)
                {
                    if (!ContentMatchesEvent(
                            content,
                            eventId,
                            idempotencyKey,
                            nameof(InventoryMovementRequestedIntegrationEvent),
                            InventoryIntegrationEventTypes.InventoryMovementRequested,
                            tableName))
                    {
                        continue;
                    }

                    var receivedFact = new CapReceivedEventFact(
                        eventId,
                        rowId,
                        tableName,
                        group,
                        statusName,
                        retryCount);
                    receivedRowsByEventId
                        .GetOrAdd(eventId, static _ => new ConcurrentDictionary<long, CapReceivedEventFact>())
                        .AddOrUpdate(rowId, receivedFact, (_, _) => receivedFact);
                }
            }
        }
    }

    private static async Task<PendingRedisFact> ReadPendingRedisFactAsync(
        string redisConnectionString,
        string capVersion,
        string eventId,
        string idempotencyKey)
    {
        var streamName = nameof(InventoryMovementRequestedIntegrationEvent);
        var consumerGroup = $"business-inventory.movement-requested.{capVersion}";
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var database = connection.GetDatabase();
        var stream = (RedisKey)streamName;
        var group = (RedisValue)consumerGroup;
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
                    streamName,
                    consumerGroup,
                    true,
                    eventId,
                    idempotencyKey,
                    pendingMessage.MessageId.ToString(),
                    pendingMessage.ConsumerName.ToString(),
                    pendingMessage.DeliveryCount);
            }
        }

        return new PendingRedisFact(streamName, consumerGroup, false, null, null, null, null, 0);
    }

    internal static bool ContentMatchesEvent(
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
            return JsonContentMatches(
                document.RootElement,
                eventId,
                idempotencyKey,
                expectedTopic,
                expectedEventType,
                tableName);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool JsonContentMatches(
        JsonElement element,
        string eventId,
        string idempotencyKey,
        string expectedTopic,
        string expectedEventType,
        string? tableName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            JsonMessageMatches(
                element,
                eventId,
                idempotencyKey,
                expectedTopic,
                expectedEventType,
                tableName,
                headerTopic: null))
        {
            return true;
        }

        if (!TryDecodeSerializedJson(element, out var nestedDocument))
        {
            return false;
        }

        using (nestedDocument)
        {
            return JsonContentMatches(
                nestedDocument!.RootElement,
                eventId,
                idempotencyKey,
                expectedTopic,
                expectedEventType,
                tableName);
        }
    }

    internal static bool RedisStreamEntryMatchesEvent(
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

    internal static string RedisValueToUtf8(RedisValue value)
    {
        var bytes = (byte[]?)value;
        if (bytes is null)
        {
            return string.Empty;
        }

        var serializedBytes = Encoding.UTF8.GetString(bytes);
        try
        {
            // CAP 10.0.1 serializes TransportMessage.Body with
            // JsonSerializer.Serialize(byte[]). System.Text.Json encodes byte[] as a
            // base64 JSON string; older/alternate captures may expose a numeric JSON
            // array, while a raw UTF-8 body is also useful for diagnostics.
            var decodedBytes = JsonSerializer.Deserialize<byte[]>(serializedBytes);
            if (decodedBytes is not null)
            {
                return Encoding.UTF8.GetString(decodedBytes);
            }
        }
        catch (JsonException)
        {
            // Preserve raw UTF-8 compatibility when the stream value is already the
            // body JSON rather than CAP's serialized byte[].
        }
        catch (FormatException)
        {
            // A JSON string that is not valid base64 is likewise a raw diagnostic
            // value, not a CAP byte[] envelope.
        }

        try
        {
            using var document = JsonDocument.Parse(serializedBytes);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
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
        }
        catch (JsonException)
        {
            // Preserve raw UTF-8 compatibility when the stream value is already the
            // body JSON rather than a JSON byte[] shape.
        }

        return serializedBytes;
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
        if (TryDecodeSerializedJson(element, out var nestedDocument))
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

    private static bool TryDecodeSerializedJson(JsonElement element, out JsonDocument? document)
    {
        document = null;
        if (element.ValueKind == JsonValueKind.String && element.GetString() is { } serialized)
        {
            if (TryParseJson(serialized, out document))
            {
                return true;
            }

            try
            {
                var decoded = Convert.FromBase64String(serialized);
                document = JsonDocument.Parse(decoded);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var decodedBytes = new List<byte>();
        foreach (var item in element.EnumerateArray())
        {
            if (!item.TryGetInt32(out var byteValue) || byteValue is < byte.MinValue or > byte.MaxValue)
            {
                return false;
            }

            decodedBytes.Add((byte)byteValue);
        }

        try
        {
            document = JsonDocument.Parse(decodedBytes.ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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

                if (TryDecodeSerializedJson(property.Value, out var headersDocument))
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
