using System.Text.Json;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Messaging.CAP;

public sealed record IntegrationEventConsumerOptions(
    string ConsumerName,
    string ExpectedEventType,
    int SupportedEventVersion)
{
    public IReadOnlyCollection<string> SupportedEventTypes { get; init; } = [ExpectedEventType];

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
    public IntegrationEventEnvelopeValidationResult Validate<TIntegrationEvent>(
        TIntegrationEvent integrationEvent,
        IntegrationEventConsumerOptions options)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        ArgumentNullException.ThrowIfNull(options);

        if (integrationEvent is null)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "missing-envelope",
                "Integration event envelope is required.");
        }

        foreach (var (fieldName, value) in GetRequiredStringFields(integrationEvent))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return IntegrationEventEnvelopeValidationResult.Invalid(
                    "missing-envelope-field",
                    $"Integration event envelope field '{fieldName}' is required.");
            }
        }

        if (integrationEvent.OccurredAtUtc == default)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "missing-envelope-field",
                "Integration event envelope field 'OccurredAtUtc' is required.");
        }

        if (integrationEvent.PayloadObject is null)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "missing-payload",
                "Integration event payload is required.");
        }

        if (!options.SupportedEventTypes.Contains(integrationEvent.EventType, StringComparer.Ordinal))
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "unexpected-event-type",
                $"Integration event type '{integrationEvent.EventType}' is not supported by consumer '{options.ConsumerName}'.");
        }

        if (integrationEvent.EventVersion <= 0)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "missing-envelope-field",
                "Integration event envelope field 'EventVersion' is required.");
        }

        if (integrationEvent.EventVersion != options.SupportedEventVersion)
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "unsupported-version",
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
            JsonSerializer.Serialize(integrationEvent),
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
    Replayed = 1
}
