using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Maintenance.Infrastructure;

public sealed class MaintenanceIntegrationEventDeadLetterStore(ApplicationDbContext dbContext)
    : IIntegrationEventDeadLetterStore
{
    public async Task<IntegrationEventDeadLetterMessage> AddAsync(
        IntegrationEventDeadLetterMessage message,
        CancellationToken cancellationToken)
    {
        dbContext.IntegrationEventDeadLetters.Add(new IntegrationEventDeadLetter(message));
        await dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> AddRangeAsync(
        IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            return [];
        }

        dbContext.IntegrationEventDeadLetters.AddRange(messages.Select(x => new IntegrationEventDeadLetter(x)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return messages.ToArray();
    }

    public async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        string? consumerName,
        IntegrationEventDeadLetterStatus? status,
        CancellationToken cancellationToken)
    {
        var query = dbContext.IntegrationEventDeadLetters.AsNoTracking();
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
        var message = await dbContext.IntegrationEventDeadLetters
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

public sealed class IntegrationEventDeadLetterEntityTypeConfiguration : IEntityTypeConfiguration<IntegrationEventDeadLetter>
{
    public void Configure(EntityTypeBuilder<IntegrationEventDeadLetter> builder)
    {
        builder.ToTable("integration_event_dead_letters", table => table.HasComment("Maintenance integration events rejected before business handling and retained for replay triage."));
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
        builder.Property(x => x.DeadLetteredAtUtc).HasColumnName("dead_lettered_at_utc").IsRequired().HasComment("UTC time when Maintenance stored the dead-letter message.");
        builder.Property(x => x.ReplayedAtUtc).HasColumnName("replayed_at_utc").HasComment("UTC time when the dead-letter message was marked replayed.");
        builder.HasIndex(x => new { x.ConsumerName, x.Status, x.DeadLetteredAtUtc });
        builder.HasIndex(x => new { x.ConsumerName, x.EventId });
    }
}
