using System.Text.Json;

namespace Nerv.IIP.Messaging.CAP;

public sealed record IntegrationEventConsumerOptions(
    string ConsumerName,
    string ExpectedEventType,
    int SupportedEventVersion);

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
    private static readonly string[] RequiredStringProperties =
    [
        "EventId",
        "EventType",
        "SourceService",
        "CorrelationId",
        "CausationId",
        "OrganizationId",
        "EnvironmentId",
        "Actor",
        "IdempotencyKey"
    ];

    public IntegrationEventEnvelopeValidationResult Validate<TIntegrationEvent>(
        TIntegrationEvent integrationEvent,
        IntegrationEventConsumerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (integrationEvent is null)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "missing-envelope",
                "Integration event envelope is required.");
        }

        foreach (var propertyName in RequiredStringProperties)
        {
            if (string.IsNullOrWhiteSpace(ReadString(integrationEvent, propertyName)))
            {
                return IntegrationEventEnvelopeValidationResult.Invalid(
                    "missing-envelope-field",
                    $"Integration event envelope field '{propertyName}' is required.");
            }
        }

        var occurredAtUtc = ReadDateTimeOffset(integrationEvent, "OccurredAtUtc");
        if (occurredAtUtc is null || occurredAtUtc == default(DateTimeOffset))
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "missing-envelope-field",
                "Integration event envelope field 'OccurredAtUtc' is required.");
        }

        var payload = ReadObject(integrationEvent, "Payload");
        if (payload is null)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "missing-payload",
                "Integration event payload is required.");
        }

        var eventType = ReadString(integrationEvent, "EventType");
        if (!string.Equals(eventType, options.ExpectedEventType, StringComparison.Ordinal))
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "unexpected-event-type",
                $"Integration event type '{eventType}' does not match expected '{options.ExpectedEventType}'.");
        }

        var eventVersion = ReadInt(integrationEvent, "EventVersion");
        if (eventVersion is null || eventVersion <= 0)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "missing-envelope-field",
                "Integration event envelope field 'EventVersion' is required.");
        }

        if (eventVersion != options.SupportedEventVersion)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "unsupported-version",
                $"Integration event version '{eventVersion}' is not supported by consumer '{options.ConsumerName}'.");
        }

        return IntegrationEventEnvelopeValidationResult.Valid;
    }

    private static string? ReadString<TIntegrationEvent>(TIntegrationEvent integrationEvent, string propertyName)
    {
        return ReadObject(integrationEvent, propertyName) as string;
    }

    private static int? ReadInt<TIntegrationEvent>(TIntegrationEvent integrationEvent, string propertyName)
    {
        return ReadObject(integrationEvent, propertyName) is int value ? value : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset<TIntegrationEvent>(TIntegrationEvent integrationEvent, string propertyName)
    {
        return ReadObject(integrationEvent, propertyName) is DateTimeOffset value ? value : null;
    }

    private static object? ReadObject<TIntegrationEvent>(TIntegrationEvent integrationEvent, string propertyName)
    {
        return integrationEvent?.GetType().GetProperty(propertyName)?.GetValue(integrationEvent);
    }
}

public sealed class IntegrationEventConsumerGuard<TIntegrationEvent>(
    IntegrationEventEnvelopeValidator validator,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IntegrationEventConsumerOptions options)
{
    public async Task HandleAsync(
        TIntegrationEvent integrationEvent,
        Func<TIntegrationEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

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
}

public interface IIntegrationEventDeadLetterStore
{
    Task<IntegrationEventDeadLetterMessage> AddAsync(
        IntegrationEventDeadLetterMessage message,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        string? consumerName,
        IntegrationEventDeadLetterStatus? status,
        CancellationToken cancellationToken);

    Task MarkReplayedAsync(
        Guid id,
        DateTimeOffset replayedAtUtc,
        CancellationToken cancellationToken);
}

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

    public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        string? consumerName,
        IntegrationEventDeadLetterStatus? status,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>(
                messages
                    .Where(message => consumerName is null || message.ConsumerName == consumerName)
                    .Where(message => status is null || message.Status == status)
                    .ToArray());
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        return new IntegrationEventDeadLetterMessage(
            Guid.CreateVersion7(),
            consumerName,
            ReadString(integrationEvent, "EventId"),
            ReadString(integrationEvent, "EventType"),
            ReadInt(integrationEvent, "EventVersion"),
            ReadString(integrationEvent, "SourceService"),
            ReadString(integrationEvent, "IdempotencyKey"),
            integrationEvent?.GetType().FullName ?? typeof(TIntegrationEvent).FullName ?? typeof(TIntegrationEvent).Name,
            JsonSerializer.Serialize(integrationEvent),
            failureCode,
            failureMessage,
            IntegrationEventDeadLetterStatus.Pending,
            DateTimeOffset.UtcNow,
            null);
    }

    private static string? ReadString<TIntegrationEvent>(TIntegrationEvent integrationEvent, string propertyName)
    {
        return integrationEvent?.GetType().GetProperty(propertyName)?.GetValue(integrationEvent) as string;
    }

    private static int? ReadInt<TIntegrationEvent>(TIntegrationEvent integrationEvent, string propertyName)
    {
        return integrationEvent?.GetType().GetProperty(propertyName)?.GetValue(integrationEvent) is int value ? value : null;
    }
}

public enum IntegrationEventDeadLetterStatus
{
    Pending = 0,
    Replayed = 1
}
