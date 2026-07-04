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
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            failureCode,
            failureMessage,
            IntegrationEventDeadLetterStatus.Pending,
            DateTimeOffset.UtcNow,
            null);
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

        var integrationEvent = failedInfo.Message.Value as IIntegrationEventEnvelope
            ?? TryDeserializeEnvelope(failedInfo.Message);
        if (integrationEvent is null)
        {
            return;
        }

        var consumerName = ReadHeader(failedInfo.Message, Headers.Group)
            ?? ReadHeader(failedInfo.Message, Headers.MessageName)
            ?? "unknown.consumer";
        var failureMessage = ReadHeader(failedInfo.Message, Headers.Exception)
            ?? "CAP subscriber exhausted retry attempts.";
        await deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                consumerName,
                integrationEvent,
                HandlerRetryExhaustedFailureCode,
                failureMessage),
            cancellationToken);
    }

    private static IIntegrationEventEnvelope? TryDeserializeEnvelope(Message message)
    {
        var eventType = ResolveEventType(ReadHeader(message, Headers.Type))
            ?? ResolveEventType(ReadHeader(message, Headers.MessageName));
        if (eventType is null)
        {
            return null;
        }

        var json = ExtractJson(message.Value);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize(json, eventType, SerializerOptions) as IIntegrationEventEnvelope;
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

    private static string? ExtractJson(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            JsonElement element => element.GetRawText(),
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            IIntegrationEventEnvelope envelope => JsonSerializer.Serialize(envelope, envelope.GetType(), SerializerOptions),
            _ => JsonSerializer.Serialize(value, value.GetType(), SerializerOptions)
        };
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
