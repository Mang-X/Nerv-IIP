using System.Text;
using System.Text.Json;
using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Messaging.CAP;

public sealed record IntegrationEventConsumerOptions(
    string ConsumerName,
    string ExpectedEventType,
    int SupportedEventVersion)
{
    public IReadOnlyCollection<string> SupportedEventTypes { get; init; } = [ExpectedEventType];
    public bool IgnoreUnsupportedEventTypes { get; init; }

    public IntegrationEventConsumerOptions(
        string consumerName,
        IReadOnlyCollection<string> supportedEventTypes,
        int supportedEventVersion)
        : this(
            consumerName,
            supportedEventTypes.FirstOrDefault() ?? throw new ArgumentException("At least one supported event type is required.", nameof(supportedEventTypes)),
            supportedEventVersion)
    {
        SupportedEventTypes = supportedEventTypes
            .Where(eventType => !string.IsNullOrWhiteSpace(eventType))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (SupportedEventTypes.Count == 0)
        {
            throw new ArgumentException("At least one supported event type is required.", nameof(supportedEventTypes));
        }
    }
}

public sealed record IntegrationEventEnvelopeValidationResult(
    bool IsValid,
    string FailureCode,
    string Message)
{
    public static readonly IntegrationEventEnvelopeValidationResult Valid = new(
        true,
        string.Empty,
        string.Empty);

    public static IntegrationEventEnvelopeValidationResult Invalid(string failureCode, string message)
    {
        return new IntegrationEventEnvelopeValidationResult(false, failureCode, message);
    }
}

public sealed class IntegrationEventEnvelopeValidator
{
    public const string MissingEnvelopeFailureCode = "missing-envelope";
    public const string MissingEnvelopeFieldFailureCode = "missing-envelope-field";
    public const string MissingPayloadFailureCode = "missing-payload";
    public const string UnexpectedEventTypeFailureCode = "unexpected-event-type";
    public const string UnsupportedVersionFailureCode = "unsupported-version";

    public IntegrationEventEnvelopeValidationResult Validate<TIntegrationEvent>(
        TIntegrationEvent integrationEvent,
        IntegrationEventConsumerOptions options)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        ArgumentNullException.ThrowIfNull(options);

        if (integrationEvent is null)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                MissingEnvelopeFailureCode,
                "Integration event envelope is required.");
        }

        foreach (var (fieldName, value) in GetRequiredStringFields(integrationEvent))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return IntegrationEventEnvelopeValidationResult.Invalid(
                    MissingEnvelopeFieldFailureCode,
                    $"Integration event envelope field '{fieldName}' is required.");
            }
        }

        if (integrationEvent.OccurredAtUtc == default)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                MissingEnvelopeFieldFailureCode,
                "Integration event envelope field 'OccurredAtUtc' is required.");
        }

        if (integrationEvent.PayloadObject is null)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                MissingPayloadFailureCode,
                "Integration event payload is required.");
        }

        if (!options.SupportedEventTypes.Contains(integrationEvent.EventType, StringComparer.Ordinal))
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                UnexpectedEventTypeFailureCode,
                $"Integration event type '{integrationEvent.EventType}' is not supported by consumer '{options.ConsumerName}'.");
        }

        if (integrationEvent.EventVersion <= 0)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                MissingEnvelopeFieldFailureCode,
                "Integration event envelope field 'EventVersion' is required.");
        }

        if (integrationEvent.EventVersion != options.SupportedEventVersion)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                UnsupportedVersionFailureCode,
                $"Integration event version '{integrationEvent.EventVersion}' is not supported by consumer '{options.ConsumerName}'.");
        }

        return IntegrationEventEnvelopeValidationResult.Valid;
    }

    private static (string FieldName, string? Value)[] GetRequiredStringFields(IIntegrationEventEnvelope integrationEvent) =>
    [
        (nameof(IIntegrationEventEnvelope.EventId), integrationEvent.EventId),
        (nameof(IIntegrationEventEnvelope.EventType), integrationEvent.EventType),
        (nameof(IIntegrationEventEnvelope.SourceService), integrationEvent.SourceService),
        (nameof(IIntegrationEventEnvelope.CorrelationId), integrationEvent.CorrelationId),
        (nameof(IIntegrationEventEnvelope.CausationId), integrationEvent.CausationId),
        (nameof(IIntegrationEventEnvelope.OrganizationId), integrationEvent.OrganizationId),
        (nameof(IIntegrationEventEnvelope.EnvironmentId), integrationEvent.EnvironmentId),
        (nameof(IIntegrationEventEnvelope.Actor), integrationEvent.Actor),
        (nameof(IIntegrationEventEnvelope.IdempotencyKey), integrationEvent.IdempotencyKey)
    ];
}

public sealed class IntegrationEventConsumerGuard<TIntegrationEvent>(
    IntegrationEventEnvelopeValidator validator,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IntegrationEventConsumerOptions options)
    where TIntegrationEvent : IIntegrationEventEnvelope
{
    public async Task HandleAsync(
        TIntegrationEvent integrationEvent,
        Func<TIntegrationEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (ShouldIgnoreUnsupportedEventType(integrationEvent))
        {
            return;
        }

        var validation = validator.Validate(integrationEvent, options);
        if (!validation.IsValid)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    options.ConsumerName,
                    integrationEvent,
                    validation.FailureCode,
                    validation.Message),
                cancellationToken);
            return;
        }

        await handler(integrationEvent, cancellationToken);
    }

    private bool ShouldIgnoreUnsupportedEventType(TIntegrationEvent integrationEvent)
    {
        return options.IgnoreUnsupportedEventTypes
            && integrationEvent is not null
            && !string.IsNullOrWhiteSpace(integrationEvent.EventType)
            && !options.SupportedEventTypes.Contains(integrationEvent.EventType, StringComparer.Ordinal);
    }
}

public interface IIntegrationEventDeadLetterStore
{
    Task<IntegrationEventDeadLetterMessage> AddAsync(
        IntegrationEventDeadLetterMessage message,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> AddRangeAsync(
        IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        string? consumerName,
        IntegrationEventDeadLetterStatus? status,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        IntegrationEventDeadLetterQuery query,
        CancellationToken cancellationToken);

    Task<IntegrationEventDeadLetterMetrics> GetMetricsAsync(CancellationToken cancellationToken);

    Task<IntegrationEventDeadLetterMessage?> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task MarkReplayedAsync(
        Guid id,
        DateTimeOffset replayedAtUtc,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid id,
        string failureCode,
        string failureMessage,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken);

    Task MarkIgnoredAsync(
        Guid id,
        string reason,
        DateTimeOffset ignoredAtUtc,
        CancellationToken cancellationToken);
}

public sealed record IntegrationEventDeadLetterQuery(
    string? ConsumerName,
    IntegrationEventDeadLetterStatus? Status,
    string? EventType,
    int Skip = 0,
    int Take = 100);

public sealed record IntegrationEventDeadLetterMetrics(
    int PendingCount,
    int FailedCount,
    int IgnoredCount,
    int ReplayedCount,
    IReadOnlyCollection<IntegrationEventDeadLetterEventTypeMetrics> EventTypes)
{
    public int ActionableCount => PendingCount + FailedCount;

    public static IntegrationEventDeadLetterMetrics FromMessages(IEnumerable<IntegrationEventDeadLetterMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return FromRows(messages.Select(message => new IntegrationEventDeadLetterMetricsRow(
            string.IsNullOrWhiteSpace(message.EventType) ? "(unknown)" : message.EventType,
            message.Status)));
    }

    public static IntegrationEventDeadLetterMetrics FromRows(IEnumerable<IntegrationEventDeadLetterMetricsRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var materialized = rows.ToArray();
        return new IntegrationEventDeadLetterMetrics(
            CountStatus(materialized, IntegrationEventDeadLetterStatus.Pending),
            CountStatus(materialized, IntegrationEventDeadLetterStatus.Failed),
            CountStatus(materialized, IntegrationEventDeadLetterStatus.Ignored),
            CountStatus(materialized, IntegrationEventDeadLetterStatus.Replayed),
            materialized
                .GroupBy(row => string.IsNullOrWhiteSpace(row.EventType) ? "(unknown)" : row.EventType, StringComparer.Ordinal)
                .Select(group => IntegrationEventDeadLetterEventTypeMetrics.FromRows(group.Key, group))
                .OrderByDescending(metric => metric.ActionableCount)
                .ThenBy(metric => metric.EventType, StringComparer.Ordinal)
                .ToArray());
    }

    private static int CountStatus(
        IReadOnlyCollection<IntegrationEventDeadLetterMetricsRow> rows,
        IntegrationEventDeadLetterStatus status) =>
        rows.Where(row => row.Status == status).Sum(row => row.Count);
}

public sealed record IntegrationEventDeadLetterEventTypeMetrics(
    string EventType,
    int PendingCount,
    int FailedCount,
    int IgnoredCount,
    int ReplayedCount)
{
    public int ActionableCount => PendingCount + FailedCount;

    public static IntegrationEventDeadLetterEventTypeMetrics FromRows(
        string eventType,
        IEnumerable<IntegrationEventDeadLetterMetricsRow> rows)
    {
        var materialized = rows.ToArray();
        return new IntegrationEventDeadLetterEventTypeMetrics(
            eventType,
            materialized.Where(row => row.Status == IntegrationEventDeadLetterStatus.Pending).Sum(row => row.Count),
            materialized.Where(row => row.Status == IntegrationEventDeadLetterStatus.Failed).Sum(row => row.Count),
            materialized.Where(row => row.Status == IntegrationEventDeadLetterStatus.Ignored).Sum(row => row.Count),
            materialized.Where(row => row.Status == IntegrationEventDeadLetterStatus.Replayed).Sum(row => row.Count));
    }
}

public sealed record IntegrationEventDeadLetterMetricsRow(string EventType, IntegrationEventDeadLetterStatus Status, int Count = 1);

public sealed class InMemoryIntegrationEventDeadLetterStore : IIntegrationEventDeadLetterStore
{
    private readonly Lock syncRoot = new();
    private readonly List<IntegrationEventDeadLetterMessage> messages = [];

    public Task<IntegrationEventDeadLetterMessage> AddAsync(
        IntegrationEventDeadLetterMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            messages.Add(message);
        }

        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> AddRangeAsync(
        IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            this.messages.AddRange(messages);
        }

        return Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>(messages.ToArray());
    }

    public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        string? consumerName,
        IntegrationEventDeadLetterStatus? status,
        CancellationToken cancellationToken)
    {
        return ListAsync(new IntegrationEventDeadLetterQuery(consumerName, status, EventType: null), cancellationToken);
    }

    public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        IntegrationEventDeadLetterQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);
        var skip = Math.Max(query.Skip, 0);
        var take = Math.Clamp(query.Take, 1, 500);
        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>(
                messages
                    .Where(message => string.IsNullOrWhiteSpace(query.ConsumerName) || message.ConsumerName == query.ConsumerName)
                    .Where(message => query.Status is null || message.Status == query.Status)
                    .Where(message => string.IsNullOrWhiteSpace(query.EventType) || message.EventType == query.EventType)
                    .OrderBy(message => message.DeadLetteredAtUtc)
                    .Skip(skip)
                    .Take(take)
                    .ToArray());
        }
    }

    public Task<IntegrationEventDeadLetterMetrics> GetMetricsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            return Task.FromResult(IntegrationEventDeadLetterMetrics.FromMessages(messages));
        }
    }

    public Task<IntegrationEventDeadLetterMessage?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            return Task.FromResult(messages.SingleOrDefault(message => message.Id == id));
        }
    }

    public Task MarkReplayedAsync(
        Guid id,
        DateTimeOffset replayedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            var index = messages.FindIndex(message => message.Id == id);
            if (index >= 0)
            {
                messages[index] = messages[index] with
                {
                    Status = IntegrationEventDeadLetterStatus.Replayed,
                    ReplayedAtUtc = replayedAtUtc
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        Guid id,
        string failureCode,
        string failureMessage,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            var index = messages.FindIndex(message => message.Id == id);
            if (index >= 0)
            {
                messages[index] = messages[index] with
                {
                    Status = IntegrationEventDeadLetterStatus.Failed,
                    FailureCode = failureCode,
                    FailureMessage = Truncate(failureMessage, 1000),
                    ReplayedAtUtc = failedAtUtc
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkIgnoredAsync(
        Guid id,
        string reason,
        DateTimeOffset ignoredAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            var index = messages.FindIndex(message => message.Id == id);
            if (index >= 0)
            {
                messages[index] = messages[index] with
                {
                    Status = IntegrationEventDeadLetterStatus.Ignored,
                    FailureCode = "ignored",
                    FailureMessage = Truncate(reason, 1000),
                    ReplayedAtUtc = ignoredAtUtc
                };
            }
        }

        return Task.CompletedTask;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

public sealed record IntegrationEventDeadLetterMessage(
    Guid Id,
    string ConsumerName,
    string? EventId,
    string? EventType,
    int? EventVersion,
    string? SourceService,
    string? IdempotencyKey,
    string EventClrType,
    string EventJson,
    string FailureCode,
    string FailureMessage,
    IntegrationEventDeadLetterStatus Status,
    DateTimeOffset DeadLetteredAtUtc,
    DateTimeOffset? ReplayedAtUtc)
{
    public static IntegrationEventDeadLetterMessage Create<TIntegrationEvent>(
        string consumerName,
        TIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new IntegrationEventDeadLetterMessage(
            Guid.CreateVersion7(),
            consumerName,
            integrationEvent.EventId,
            integrationEvent.EventType,
            integrationEvent.EventVersion,
            integrationEvent.SourceService,
            integrationEvent.IdempotencyKey,
            integrationEvent.GetType().FullName ?? typeof(TIntegrationEvent).FullName ?? typeof(TIntegrationEvent).Name,
            SerializeForForensics(integrationEvent),
            failureCode,
            failureMessage,
            IntegrationEventDeadLetterStatus.Pending,
            DateTimeOffset.UtcNow,
            null);
    }

    /// <summary>
    /// Dead letters must be writable for exactly the objects that violate their own contract. Typed serialization
    /// runs the contract converters (type-level and property-level), and converters in this repository validate on
    /// write too, so an envelope that breaks its wire contract would make <see cref="Create{TIntegrationEvent}"/>
    /// itself throw (#3101). Fall back to <see cref="ForensicJson"/>, which walks the object graph with no converter
    /// at all, so nested property converters cannot re-introduce the failure.
    /// </summary>
    private static string SerializeForForensics(IIntegrationEventEnvelope integrationEvent)
    {
        try
        {
            return JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
        }
        catch (Exception typedFailure)
        {
            try
            {
                return ForensicJson.Serialize(integrationEvent);
            }
            catch (Exception forensicFailure)
            {
                // 结构性兜底：无论投影因何失败（含 OutOfMemoryException——这里能捕获且必须捕获，
                // 否则"越该进 DLQ 越写不进"原样复现），死信都以只含信封身份列的字面 JSON 写入。
                return ForensicJson.SerializeIdentityOnly(integrationEvent, typedFailure, forensicFailure);
            }
        }
    }
}

public static class ForensicJson
{
    private const int MaxDepth = 32;

    public static string Serialize(object? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteValue(writer, value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Last-resort dead-letter body: only the <see cref="IIntegrationEventEnvelope"/> scalar members (each read
    /// defensively) plus the two failures that got us here. Built with the raw writer so nothing here can throw
    /// for a reason that depends on the payload.
    /// </summary>
    public static string SerializeIdentityOnly(IIntegrationEventEnvelope integrationEvent, Exception typedFailure, Exception forensicFailure)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WriteIdentity(writer, "eventId", () => integrationEvent.EventId);
            WriteIdentity(writer, "eventType", () => integrationEvent.EventType);
            WriteIdentity(writer, "eventVersion", () => integrationEvent.EventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WriteIdentity(writer, "occurredAtUtc", () => integrationEvent.OccurredAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            WriteIdentity(writer, "sourceService", () => integrationEvent.SourceService);
            WriteIdentity(writer, "correlationId", () => integrationEvent.CorrelationId);
            WriteIdentity(writer, "causationId", () => integrationEvent.CausationId);
            WriteIdentity(writer, "organizationId", () => integrationEvent.OrganizationId);
            WriteIdentity(writer, "environmentId", () => integrationEvent.EnvironmentId);
            WriteIdentity(writer, "actor", () => integrationEvent.Actor);
            WriteIdentity(writer, "idempotencyKey", () => integrationEvent.IdempotencyKey);
            writer.WriteString("payload", "<unserializable>");
            writer.WriteString("typedSerializationFailure", Describe(typedFailure));
            writer.WriteString("forensicSerializationFailure", Describe(forensicFailure));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteIdentity(Utf8JsonWriter writer, string name, Func<string?> read)
    {
        string? value;
        try
        {
            value = read();
        }
        catch (Exception exception)
        {
            value = $"<unreadable: {exception.GetType().Name}>";
        }

        writer.WriteString(name, value);
    }

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";

    private static void WriteValue(Utf8JsonWriter writer, object? value, int depth, HashSet<object> ancestors)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case string text:
                writer.WriteStringValue(text);
                return;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                return;
            case float single:
                WriteFloating(writer, single, float.IsFinite(single));
                return;
            case double @double:
                WriteFloating(writer, @double, double.IsFinite(@double));
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                writer.WriteRawValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, skipInputValidation: false);
                return;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset);
                return;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                return;
            case DateOnly dateOnly:
                writer.WriteStringValue(dateOnly.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                return;
            case TimeOnly timeOnly:
                writer.WriteStringValue(timeOnly.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                return;
            case TimeSpan timeSpan:
                writer.WriteStringValue(timeSpan.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
                return;
            case Guid guid:
                writer.WriteStringValue(guid);
                return;
            case Enum enumeration:
                writer.WriteStringValue(enumeration.ToString());
                return;
            case JsonElement element:
                element.WriteTo(writer);
                return;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                return;
            // 框架/反射类型不展开：`Type`/`MemberInfo` 的反射图深不见底（展开到 OOM），`Uri`/`Exception`/`Delegate`
            // 等展开后体积失控且无取证价值；统一写 ToString。
            case Type or System.Reflection.MemberInfo or System.Reflection.Assembly or Uri or Exception or Delegate
                or System.Globalization.CultureInfo or System.Text.RegularExpressions.Regex or Version or Stream:
                writer.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                return;
        }

        if (depth >= MaxDepth || !ancestors.Add(value))
        {
            writer.WriteStringValue($"<{value.GetType().Name}: depth or cycle limit>");
            return;
        }

        try
        {
            switch (value)
            {
                case System.Collections.IDictionary dictionary:
                    WriteDictionary(writer, dictionary, depth, ancestors);
                    return;
                case System.Collections.IEnumerable enumerable:
                    WriteEnumerable(writer, enumerable, depth, ancestors);
                    return;
            }

            writer.WriteStartObject();
            foreach (var property in value.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? propertyValue;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch (Exception exception)
                {
                    propertyValue = $"<unreadable: {exception.GetType().Name}>";
                }

                writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
                WriteValue(writer, propertyValue, depth + 1, ancestors);
            }

            writer.WriteEndObject();
        }
        finally
        {
            ancestors.Remove(value);
        }
    }

    private static void WriteFloating(Utf8JsonWriter writer, double value, bool isFinite)
    {
        // NaN / ±Infinity 不是 JSON 数字，writer 不放行；显式写成字符串。
        if (isFinite)
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void WriteDictionary(Utf8JsonWriter writer, System.Collections.IDictionary dictionary, int depth, HashSet<object> ancestors)
    {
        writer.WriteStartObject();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                var name = Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "null";
                // 两个键 ToString 相同会产出重复属性名，jsonb 会静默丢前者：冲突时追加序号。
                var unique = name;
                for (var suffix = 2; !seenNames.Add(unique); suffix++)
                {
                    unique = $"{name}#{suffix}";
                }

                writer.WritePropertyName(unique);
                WriteValue(writer, entry.Value, depth + 1, ancestors);
            }
        }
        catch (Exception exception)
        {
            writer.WriteString("<enumeration failed>", $"{exception.GetType().Name}: {exception.Message}");
        }

        writer.WriteEndObject();
    }

    private static void WriteEnumerable(Utf8JsonWriter writer, System.Collections.IEnumerable enumerable, int depth, HashSet<object> ancestors)
    {
        writer.WriteStartArray();
        try
        {
            foreach (var item in enumerable)
            {
                WriteValue(writer, item, depth + 1, ancestors);
            }
        }
        catch (Exception exception)
        {
            // 枚举中途抛（惰性序列、已释放的 reader 等）：保留已写出的元素，追加失败说明，不让整份取证丢失。
            writer.WriteStringValue($"<enumeration failed: {exception.GetType().Name}: {exception.Message}>");
        }

        writer.WriteEndArray();
    }
}

public enum IntegrationEventDeadLetterStatus
{
    Pending = 0,
    Replayed = 1,
    Failed = 2,
    Ignored = 3
}

public sealed record IntegrationEventDeadLetterReplayResult(
    Guid Id,
    bool Succeeded,
    string Status,
    string? Message);

public interface IIntegrationEventDeadLetterReplayHandler
{
    bool CanReplay(IntegrationEventDeadLetterMessage message);

    Task ReplayAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken);
}

public sealed class IntegrationEventDeadLetterReplayExecutor(
    IIntegrationEventDeadLetterStore deadLetterStore,
    IEnumerable<IIntegrationEventDeadLetterReplayHandler> handlers,
    TimeProvider timeProvider)
{
    private const string ReplayHandlerFailedCode = "replay-handler-failed";
    private readonly IReadOnlyList<IIntegrationEventDeadLetterReplayHandler> handlers = handlers.ToArray();

    public async Task<IntegrationEventDeadLetterReplayResult> ReplayAsync(Guid id, CancellationToken cancellationToken)
    {
        var message = await deadLetterStore.GetAsync(id, cancellationToken);
        if (message is null)
        {
            return new IntegrationEventDeadLetterReplayResult(id, false, "NotFound", "Dead-letter message was not found.");
        }

        try
        {
            var handler = handlers.FirstOrDefault(handler => handler.CanReplay(message));
            if (handler is null)
            {
                await deadLetterStore.MarkFailedAsync(
                    id,
                    "replay-handler-not-found",
                    $"No replay handler is registered for '{message.EventClrType}'.",
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return new IntegrationEventDeadLetterReplayResult(id, false, IntegrationEventDeadLetterStatus.Failed.ToString(), "No replay handler is registered.");
            }

            await handler.ReplayAsync(message, cancellationToken);
            await deadLetterStore.MarkReplayedAsync(id, timeProvider.GetUtcNow(), cancellationToken);
            return new IntegrationEventDeadLetterReplayResult(id, true, IntegrationEventDeadLetterStatus.Replayed.ToString(), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await deadLetterStore.MarkFailedAsync(
                id,
                ReplayHandlerFailedCode,
                ex.Message,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return new IntegrationEventDeadLetterReplayResult(id, false, IntegrationEventDeadLetterStatus.Failed.ToString(), ex.Message);
        }
    }

    public async Task<IReadOnlyList<IntegrationEventDeadLetterReplayResult>> ReplayBatchAsync(
        IntegrationEventDeadLetterQuery query,
        CancellationToken cancellationToken)
    {
        var candidates = await deadLetterStore.ListAsync(
            query with { Status = query.Status ?? IntegrationEventDeadLetterStatus.Pending },
            cancellationToken);
        var results = new List<IntegrationEventDeadLetterReplayResult>(candidates.Count);
        foreach (var candidate in candidates)
        {
            results.Add(await ReplayAsync(candidate.Id, cancellationToken));
        }

        return results;
    }
}

public sealed class IntegrationEventCapFailureDeadLetterer(IIntegrationEventDeadLetterStore deadLetterStore)
{
    public const string HandlerRetryExhaustedFailureCode = "handler-retry-exhausted";

    /// <summary>
    /// The transport delivered a message that the typed contract rejected before any handler ran (for example a
    /// wire-contract <see cref="JsonException"/> thrown by a contract converter on read). The original wire body is
    /// preserved verbatim: re-serialising through the same contract is exactly what cannot work for it (#3101).
    /// </summary>
    public const string ContractRejectedFailureCode = "contract-rejected";

    /// <summary>
    /// Wrapper property used when the wire body is not JSON at all; <c>EventJson</c> is stored in a JSON column on
    /// PostgreSQL, so a non-JSON body is kept verbatim as a string inside a JSON object instead of being dropped.
    /// </summary>
    public const string RawBodyPropertyName = "rawBody";

    private const string DataUriBase64Marker = ";base64,";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task HandleAsync(FailedInfo failedInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failedInfo);
        if (failedInfo.MessageType != MessageType.Subscribe)
        {
            return;
        }

        var consumerName = ReadHeader(failedInfo.Message, Headers.Group)
            ?? ReadHeader(failedInfo.Message, Headers.MessageName)
            ?? "unknown.consumer";
        var failureMessage = ReadHeader(failedInfo.Message, Headers.Exception)
            ?? "CAP subscriber exhausted retry attempts.";

        if (failedInfo.Message.Value is IIntegrationEventEnvelope typedValue)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(consumerName, typedValue, HandlerRetryExhaustedFailureCode, failureMessage),
                cancellationToken);
            return;
        }

        var json = ExtractJson(failedInfo.Message.Value);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var eventType = ResolveEventType(ReadHeader(failedInfo.Message, Headers.Type))
            ?? ResolveEventType(ReadHeader(failedInfo.Message, Headers.MessageName));
        JsonException? contractRejection = null;
        if (eventType is not null)
        {
            try
            {
                if (JsonSerializer.Deserialize(json, eventType, SerializerOptions) is IIntegrationEventEnvelope integrationEvent)
                {
                    await deadLetterStore.AddAsync(
                        IntegrationEventDeadLetterMessage.Create(consumerName, integrationEvent, HandlerRetryExhaustedFailureCode, failureMessage),
                        cancellationToken);
                    return;
                }
            }
            catch (JsonException exception)
            {
                contractRejection = exception;
            }
        }

        var eventClrType = eventType?.FullName
            ?? ReadHeader(failedInfo.Message, Headers.Type)
            ?? ReadHeader(failedInfo.Message, Headers.MessageName)
            ?? "unknown";
        await deadLetterStore.AddAsync(
            CreateRawDeadLetter(consumerName, eventClrType, json, contractRejection, failureMessage),
            cancellationToken);
    }

    /// <summary>
    /// Builds a dead letter for a body the typed contract refused: identity columns are read leniently from the raw
    /// JSON (missing ones stay <c>null</c>) and <see cref="IntegrationEventDeadLetterMessage.EventJson"/> is the
    /// original wire body, so replay tooling and operators see exactly what the transport delivered.
    /// </summary>
    private static IntegrationEventDeadLetterMessage CreateRawDeadLetter(
        string consumerName,
        string eventClrType,
        string json,
        JsonException? contractRejection,
        string transportFailureMessage)
    {
        string? eventId = null, eventTypeName = null, sourceService = null, idempotencyKey = null;
        int? eventVersion = null;
        string eventJson;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                eventId = ReadString(document.RootElement, nameof(IIntegrationEventEnvelope.EventId));
                eventTypeName = ReadString(document.RootElement, nameof(IIntegrationEventEnvelope.EventType));
                sourceService = ReadString(document.RootElement, nameof(IIntegrationEventEnvelope.SourceService));
                idempotencyKey = ReadString(document.RootElement, nameof(IIntegrationEventEnvelope.IdempotencyKey));
                eventVersion = ReadInt32(document.RootElement, nameof(IIntegrationEventEnvelope.EventVersion));
            }

            eventJson = json;
        }
        catch (JsonException)
        {
            // Not JSON at all: keep the body verbatim inside a JSON wrapper so the JSON column still accepts it.
            eventJson = JsonSerializer.Serialize(new Dictionary<string, string> { [RawBodyPropertyName] = json });
        }

        var failureMessage = contractRejection is null
            ? transportFailureMessage
            : $"{contractRejection.Message} ({transportFailureMessage})";
        return new IntegrationEventDeadLetterMessage(
            Guid.CreateVersion7(),
            consumerName,
            eventId,
            eventTypeName,
            eventVersion,
            sourceService,
            idempotencyKey,
            eventClrType,
            eventJson,
            ContractRejectedFailureCode,
            failureMessage,
            IntegrationEventDeadLetterStatus.Pending,
            DateTimeOffset.UtcNow,
            null);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
            }
        }

        return null;
    }

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value)
                    ? value
                    : null;
            }
        }

        return null;
    }

    private static Type? ResolveEventType(string? eventTypeName)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            return null;
        }

        return AsIntegrationEventEnvelope(Type.GetType(eventTypeName, throwOnError: false))
            ?? AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(GetTypesSafely)
                .Select(type => AsIntegrationEventEnvelope(type))
                .FirstOrDefault(type =>
                    type is not null
                    && (string.Equals(type.FullName, eventTypeName, StringComparison.Ordinal)
                        || string.Equals(type.Name, eventTypeName, StringComparison.Ordinal)));
    }

    private static IEnumerable<Type> GetTypesSafely(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static Type? AsIntegrationEventEnvelope(Type? type)
    {
        return type is not null
            && !type.IsAbstract
            && !type.IsInterface
            && typeof(IIntegrationEventEnvelope).IsAssignableFrom(type)
                ? type
                : null;
    }

    /// <summary>
    /// CAP persists a received message whose body could not be bound to the subscriber parameter as a
    /// <c>data:&lt;name&gt;;base64,&lt;payload&gt;</c> string. Decode it so the dead letter carries the wire JSON, not the
    /// storage encoding.
    /// </summary>
    private static string? ExtractJson(object? value)
    {
        return value switch
        {
            null => null,
            string text => DecodeDataUri(text),
            JsonElement element => element.GetRawText(),
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            IIntegrationEventEnvelope envelope => JsonSerializer.Serialize(envelope, envelope.GetType(), SerializerOptions),
            _ => JsonSerializer.Serialize(value, value.GetType(), SerializerOptions)
        };
    }

    private static string DecodeDataUri(string text)
    {
        if (!text.StartsWith("data:", StringComparison.Ordinal))
        {
            return text;
        }

        var marker = text.IndexOf(DataUriBase64Marker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return text;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(text[(marker + DataUriBase64Marker.Length)..]));
        }
        catch (FormatException)
        {
            return text;
        }
    }

    private static string? ReadHeader(Message message, string name)
    {
        return message.Headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}

public static class CapDeadLetterOptionsExtensions
{
    public static CapOptions UseIntegrationEventDeadLetterOnFailedThreshold(this CapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var previous = options.FailedThresholdCallback;
        options.FailedThresholdCallback = failedInfo =>
        {
            previous?.Invoke(failedInfo);
            using var scope = failedInfo.ServiceProvider.CreateScope();
            var deadLetterer = scope.ServiceProvider.GetRequiredService<IntegrationEventCapFailureDeadLetterer>();
            deadLetterer.HandleAsync(failedInfo, CancellationToken.None).GetAwaiter().GetResult();
        };
        return options;
    }
}
