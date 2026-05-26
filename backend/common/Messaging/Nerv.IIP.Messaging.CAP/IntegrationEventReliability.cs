using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Contracts.IntegrationEvents;

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

        if (!string.Equals(integrationEvent.EventType, options.ExpectedEventType, StringComparison.Ordinal))
        {
            return IntegrationEventEnvelopeValidationResult.Invalid(
                "unexpected-event-type",
                $"Integration event type '{integrationEvent.EventType}' does not match expected '{options.ExpectedEventType}'.");
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

public sealed class PersistentIntegrationEventDeadLetterStore<TDbContext>(TDbContext dbContext)
    : IIntegrationEventDeadLetterStore
    where TDbContext : DbContext
{
    public async Task<IntegrationEventDeadLetterMessage> AddAsync(
        IntegrationEventDeadLetterMessage message,
        CancellationToken cancellationToken)
    {
        dbContext.Set<IntegrationEventDeadLetter>().Add(new IntegrationEventDeadLetter(message));
        await dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        string? consumerName,
        IntegrationEventDeadLetterStatus? status,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Set<IntegrationEventDeadLetter>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(consumerName))
        {
            query = query.Where(x => x.ConsumerName == consumerName);
        }

        if (status is not null)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query
            .OrderBy(x => x.DeadLetteredAtUtc)
            .Select(x => x.ToMessage())
            .ToArrayAsync(cancellationToken);
    }

    public async Task MarkReplayedAsync(
        Guid id,
        DateTimeOffset replayedAtUtc,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.Set<IntegrationEventDeadLetter>()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.MarkReplayed(replayedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class IntegrationEventDeadLetter
{
    private IntegrationEventDeadLetter()
    {
    }

    public IntegrationEventDeadLetter(IntegrationEventDeadLetterMessage message)
    {
        Id = message.Id;
        ConsumerName = message.ConsumerName;
        EventId = message.EventId;
        EventType = message.EventType;
        EventVersion = message.EventVersion;
        SourceService = message.SourceService;
        IdempotencyKey = message.IdempotencyKey;
        EventClrType = message.EventClrType;
        EventJson = message.EventJson;
        FailureCode = message.FailureCode;
        FailureMessage = message.FailureMessage;
        Status = message.Status;
        DeadLetteredAtUtc = message.DeadLetteredAtUtc;
        ReplayedAtUtc = message.ReplayedAtUtc;
    }

    public Guid Id { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public string? EventId { get; private set; }
    public string? EventType { get; private set; }
    public int? EventVersion { get; private set; }
    public string? SourceService { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string EventClrType { get; private set; } = string.Empty;
    public string EventJson { get; private set; } = string.Empty;
    public string FailureCode { get; private set; } = string.Empty;
    public string FailureMessage { get; private set; } = string.Empty;
    public IntegrationEventDeadLetterStatus Status { get; private set; }
    public DateTimeOffset DeadLetteredAtUtc { get; private set; }
    public DateTimeOffset? ReplayedAtUtc { get; private set; }

    public void MarkReplayed(DateTimeOffset replayedAtUtc)
    {
        Status = IntegrationEventDeadLetterStatus.Replayed;
        ReplayedAtUtc = replayedAtUtc;
    }

    public IntegrationEventDeadLetterMessage ToMessage()
    {
        return new IntegrationEventDeadLetterMessage(
            Id,
            ConsumerName,
            EventId,
            EventType,
            EventVersion,
            SourceService,
            IdempotencyKey,
            EventClrType,
            EventJson,
            FailureCode,
            FailureMessage,
            Status,
            DeadLetteredAtUtc,
            ReplayedAtUtc);
    }
}

public static class IntegrationEventDeadLetterModelBuilderExtensions
{
    public static void ConfigureIntegrationEventDeadLetters(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationEventDeadLetter>(Configure);
    }

    private static void Configure(EntityTypeBuilder<IntegrationEventDeadLetter> builder)
    {
        builder.ToTable("integration_event_dead_letters", table => table.HasComment("Integration events rejected before business handling and retained for replay triage."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasComment("Dead-letter message id.");
        builder.Property(x => x.ConsumerName).HasColumnName("consumer_name").IsRequired().HasMaxLength(200).HasComment("Integration event consumer name that rejected the message.");
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(200).HasComment("Rejected integration event id when present.");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(300).HasComment("Rejected integration event type when present.");
        builder.Property(x => x.EventVersion).HasColumnName("event_version").HasComment("Rejected integration event envelope version when present.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").HasMaxLength(150).HasComment("Source service from the rejected event envelope when present.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(500).HasComment("Rejected integration event idempotency key when present.");
        builder.Property(x => x.EventClrType).HasColumnName("event_clr_type").IsRequired().HasMaxLength(500).HasComment("CLR contract type captured for replay diagnostics.");
        builder.Property(x => x.EventJson).HasColumnName("event_json").IsRequired().HasColumnType("jsonb").HasComment("Serialized rejected integration event envelope and payload.");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").IsRequired().HasMaxLength(100).HasComment("Machine-readable reason the consumer rejected the message.");
        builder.Property(x => x.FailureMessage).HasColumnName("failure_message").IsRequired().HasMaxLength(1000).HasComment("Operator-readable rejection detail.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Dead-letter status: Pending or Replayed.");
        builder.Property(x => x.DeadLetteredAtUtc).HasColumnName("dead_lettered_at_utc").IsRequired().HasComment("UTC time when the service stored the dead-letter message.");
        builder.Property(x => x.ReplayedAtUtc).HasColumnName("replayed_at_utc").HasComment("UTC time when the dead-letter message was marked replayed.");
        builder.HasIndex(x => new { x.ConsumerName, x.Status, x.DeadLetteredAtUtc });
        builder.HasIndex(x => new { x.ConsumerName, x.EventId });
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
