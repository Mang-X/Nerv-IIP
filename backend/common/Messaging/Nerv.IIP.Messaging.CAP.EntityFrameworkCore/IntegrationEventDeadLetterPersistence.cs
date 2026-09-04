using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nerv.IIP.Messaging.CAP;

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

    public async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> AddRangeAsync(
        IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            return [];
        }

        dbContext.Set<IntegrationEventDeadLetter>().AddRange(messages.Select(x => new IntegrationEventDeadLetter(x)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return messages.ToArray();
    }

    public async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        string? consumerName,
        IntegrationEventDeadLetterStatus? status,
        CancellationToken cancellationToken)
    {
        return await ListAsync(new IntegrationEventDeadLetterQuery(consumerName, status, EventType: null), cancellationToken);
    }

    public async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
        IntegrationEventDeadLetterQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.Set<IntegrationEventDeadLetter>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.ConsumerName))
        {
            queryable = queryable.Where(x => x.ConsumerName == query.ConsumerName);
        }

        if (query.Status is not null)
        {
            queryable = queryable.Where(x => x.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            queryable = queryable.Where(x => x.EventType == query.EventType);
        }

        var skip = Math.Max(query.Skip, 0);
        var take = Math.Clamp(query.Take, 1, 500);

        // SQLite cannot translate DateTimeOffset ordering; production providers keep sorting in SQL.
        IEnumerable<IntegrationEventDeadLetter> rows = IsSqliteProvider()
            ? (await queryable.ToArrayAsync(cancellationToken)).OrderBy(x => x.DeadLetteredAtUtc).Skip(skip).Take(take)
            : await queryable.OrderBy(x => x.DeadLetteredAtUtc).Skip(skip).Take(take).ToArrayAsync(cancellationToken);

        return rows
            .Select(x => x.ToMessage())
            .ToArray();
    }

    public async Task<IntegrationEventDeadLetterMetrics> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<IntegrationEventDeadLetter>()
            .AsNoTracking()
            .GroupBy(x => new
            {
                EventType = x.EventType ?? "(unknown)",
                x.Status
            })
            .Select(group => new IntegrationEventDeadLetterMetricsRow(
                group.Key.EventType,
                group.Key.Status,
                group.Count()))
            .ToArrayAsync(cancellationToken);
        return IntegrationEventDeadLetterMetrics.FromRows(rows);
    }

    public async Task<IntegrationEventDeadLetterMessage?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return (await dbContext.Set<IntegrationEventDeadLetter>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken))
            ?.ToMessage();
    }

    private bool IsSqliteProvider()
    {
        return dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) is true;
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

    public async Task MarkFailedAsync(
        Guid id,
        string failureCode,
        string failureMessage,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.Set<IntegrationEventDeadLetter>()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.MarkFailed(failureCode, failureMessage, failedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkIgnoredAsync(
        Guid id,
        string reason,
        DateTimeOffset ignoredAtUtc,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.Set<IntegrationEventDeadLetter>()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.MarkIgnored(reason, ignoredAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class IntegrationEventDeadLetter
{
    private IntegrationEventDeadLetter()
    {
    }

    /// <remarks>
    /// Identity and failure columns are fixed-length while their values come from producer-controlled wire JSON and
    /// transport exception headers (#3101). Persist truncated values rather than letting a 22001 escape from the CAP
    /// failed-threshold callback, where it would be swallowed and the dead letter lost — the very failure class this
    /// store exists to prevent. Limits mirror <see cref="IntegrationEventDeadLetterModelBuilderExtensions"/>.
    /// </remarks>
    public IntegrationEventDeadLetter(IntegrationEventDeadLetterMessage message)
    {
        Id = message.Id;
        ConsumerName = Truncate(message.ConsumerName, ConsumerNameMaxLength);
        EventId = TruncateOptional(message.EventId, EventIdMaxLength);
        EventType = TruncateOptional(message.EventType, EventTypeMaxLength);
        EventVersion = message.EventVersion;
        SourceService = TruncateOptional(message.SourceService, SourceServiceMaxLength);
        IdempotencyKey = TruncateOptional(message.IdempotencyKey, IdempotencyKeyMaxLength);
        EventClrType = Truncate(message.EventClrType, EventClrTypeMaxLength);
        EventJson = message.EventJson;
        FailureCode = Truncate(message.FailureCode, FailureCodeMaxLength);
        FailureMessage = Truncate(message.FailureMessage, FailureMessageMaxLength);
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

    public void MarkFailed(string failureCode, string failureMessage, DateTimeOffset failedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        Status = IntegrationEventDeadLetterStatus.Failed;
        FailureCode = failureCode;
        FailureMessage = Truncate(failureMessage);
        ReplayedAtUtc = failedAtUtc;
    }

    public void MarkIgnored(string reason, DateTimeOffset ignoredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = IntegrationEventDeadLetterStatus.Ignored;
        FailureCode = "ignored";
        FailureMessage = Truncate(reason);
        ReplayedAtUtc = ignoredAtUtc;
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

    public const int ConsumerNameMaxLength = 200;
    public const int EventIdMaxLength = 200;
    public const int EventTypeMaxLength = 300;
    public const int SourceServiceMaxLength = 150;
    public const int IdempotencyKeyMaxLength = 500;
    public const int EventClrTypeMaxLength = 500;
    public const int FailureCodeMaxLength = 100;
    public const int FailureMessageMaxLength = 1000;

    private static string Truncate(string value) => Truncate(value, FailureMessageMaxLength);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateOptional(string? value, int maxLength) =>
        value is null ? null : Truncate(value, maxLength);
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
        builder.Property(x => x.ConsumerName).HasColumnName("consumer_name").IsRequired().HasMaxLength(IntegrationEventDeadLetter.ConsumerNameMaxLength).HasComment("Integration event consumer name that rejected the message.");
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(IntegrationEventDeadLetter.EventIdMaxLength).HasComment("Rejected integration event id when present.");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(IntegrationEventDeadLetter.EventTypeMaxLength).HasComment("Rejected integration event type when present.");
        builder.Property(x => x.EventVersion).HasColumnName("event_version").HasComment("Rejected integration event envelope version when present.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").HasMaxLength(IntegrationEventDeadLetter.SourceServiceMaxLength).HasComment("Source service from the rejected event envelope when present.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(IntegrationEventDeadLetter.IdempotencyKeyMaxLength).HasComment("Rejected integration event idempotency key when present.");
        builder.Property(x => x.EventClrType).HasColumnName("event_clr_type").IsRequired().HasMaxLength(IntegrationEventDeadLetter.EventClrTypeMaxLength).HasComment("CLR contract type captured for replay diagnostics.");
        builder.Property(x => x.EventJson).HasColumnName("event_json").IsRequired().HasColumnType("jsonb").HasComment("Serialized rejected integration event envelope and payload.");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").IsRequired().HasMaxLength(IntegrationEventDeadLetter.FailureCodeMaxLength).HasComment("Machine-readable reason the consumer rejected the message.");
        builder.Property(x => x.FailureMessage).HasColumnName("failure_message").IsRequired().HasMaxLength(IntegrationEventDeadLetter.FailureMessageMaxLength).HasComment("Operator-readable rejection detail.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Dead-letter status: Pending, Replayed, Failed, or Ignored.");
        builder.Property(x => x.DeadLetteredAtUtc).HasColumnName("dead_lettered_at_utc").IsRequired().HasComment("UTC time when the service stored the dead-letter message.");
        builder.Property(x => x.ReplayedAtUtc).HasColumnName("replayed_at_utc").HasComment("UTC time when the dead-letter message was marked replayed.");
        builder.HasIndex(x => new { x.ConsumerName, x.Status, x.DeadLetteredAtUtc });
        builder.HasIndex(x => new { x.ConsumerName, x.EventId });
    }
}
