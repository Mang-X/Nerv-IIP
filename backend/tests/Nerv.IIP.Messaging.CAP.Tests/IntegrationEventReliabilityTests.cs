using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

public sealed class IntegrationEventReliabilityTests
{
    [Fact]
    public async Task Consumer_guard_dead_letters_unsupported_event_version_without_invoking_handler()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var guard = new IntegrationEventConsumerGuard<SampleIntegrationEvent>(
            new IntegrationEventEnvelopeValidator(),
            store,
            new IntegrationEventConsumerOptions(
                ConsumerName: "sample.consumer",
                ExpectedEventType: "sample.Event",
                SupportedEventVersion: 1));
        var invoked = false;

        await guard.HandleAsync(
            new SampleIntegrationEvent(
                EventId: "event-001",
                EventType: "sample.Event",
                EventVersion: 2,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                SourceService: "sample",
                CorrelationId: "corr-001",
                CausationId: "cause-001",
                OrganizationId: "org-001",
                EnvironmentId: "env-001",
                Actor: "system:test",
                IdempotencyKey: "sample:event-001",
                Payload: new SamplePayload("value")),
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var messages = await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None);
        var message = Assert.Single(messages);
        Assert.False(invoked);
        Assert.Equal("unsupported-version", message.FailureCode);
        Assert.Equal("event-001", message.EventId);
        Assert.Equal("sample.Event", message.EventType);
        Assert.Equal(2, message.EventVersion);
    }

    [Fact]
    public async Task Consumer_guard_invokes_handler_and_skips_dead_letter_for_valid_envelope()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var guard = new IntegrationEventConsumerGuard<SampleIntegrationEvent>(
            new IntegrationEventEnvelopeValidator(),
            store,
            new IntegrationEventConsumerOptions(
                ConsumerName: "sample.consumer",
                ExpectedEventType: "sample.Event",
                SupportedEventVersion: 1));
        var handledEventIds = new List<string>();

        await guard.HandleAsync(
            CreateValidEvent("event-002"),
            (integrationEvent, _) =>
            {
                handledEventIds.Add(integrationEvent.EventId);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(["event-002"], handledEventIds);
        Assert.Empty(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Consumer_guard_dead_letters_unexpected_event_type_by_default_without_invoking_handler()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var guard = new IntegrationEventConsumerGuard<SampleIntegrationEvent>(
            new IntegrationEventEnvelopeValidator(),
            store,
            new IntegrationEventConsumerOptions(
                ConsumerName: "sample.consumer",
                ExpectedEventType: "sample.Event",
                SupportedEventVersion: 1));
        var invoked = false;

        await guard.HandleAsync(
            CreateValidEvent("event-shared-topic-001") with { EventType = "sample.OtherEvent" },
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var message = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        Assert.False(invoked);
        Assert.Equal(IntegrationEventEnvelopeValidator.UnexpectedEventTypeFailureCode, message.FailureCode);
        Assert.Equal("sample.OtherEvent", message.EventType);
    }

    [Fact]
    public async Task Consumer_guard_ignores_unexpected_event_type_when_shared_topic_option_is_enabled()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var guard = new IntegrationEventConsumerGuard<SampleIntegrationEvent>(
            new IntegrationEventEnvelopeValidator(),
            store,
            new IntegrationEventConsumerOptions(
                ConsumerName: "sample.consumer",
                ExpectedEventType: "sample.Event",
                SupportedEventVersion: 1)
            {
                IgnoreUnsupportedEventTypes = true
            });
        var invoked = false;

        await guard.HandleAsync(
            CreateValidEvent("event-shared-topic-002") with
            {
                EventType = "sample.OtherEvent",
                Payload = null!
            },
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(invoked);
        Assert.Empty(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Dead_letter_store_marks_pending_message_as_replayed()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var message = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-003"),
                "manual-replay-test",
                "Stored for replay."),
            CancellationToken.None);

        await store.MarkReplayedAsync(message.Id, DateTimeOffset.Parse("2026-05-25T00:00:00Z"), CancellationToken.None);

        var replayed = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Replayed, CancellationToken.None));
        Assert.Equal(message.Id, replayed.Id);
        Assert.NotNull(replayed.ReplayedAtUtc);
        Assert.Empty(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Dead_letter_store_filters_by_event_type_and_marks_failed_or_ignored()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var first = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-filter-001"),
                "manual-replay-test",
                "Stored for replay."),
            CancellationToken.None);
        await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-filter-002") with { EventType = "sample.OtherEvent" },
                "manual-replay-test",
                "Stored for replay."),
            CancellationToken.None);

        var filtered = await store.ListAsync(
            new IntegrationEventDeadLetterQuery(
                ConsumerName: "sample.consumer",
                Status: IntegrationEventDeadLetterStatus.Pending,
                EventType: "sample.Event",
                Skip: 0,
                Take: 10),
            CancellationToken.None);
        await store.MarkFailedAsync(
            first.Id,
            "replay-handler-failed",
            "The downstream handler still rejects the event.",
            DateTimeOffset.Parse("2026-07-03T00:00:00Z"),
            CancellationToken.None);
        await store.MarkIgnoredAsync(
            first.Id,
            "Operator confirmed this stale event should not be replayed.",
            DateTimeOffset.Parse("2026-07-03T01:00:00Z"),
            CancellationToken.None);

        Assert.Equal(first.Id, Assert.Single(filtered).Id);
        var ignored = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Ignored, CancellationToken.None));
        Assert.Equal(first.Id, ignored.Id);
        Assert.Equal("ignored", ignored.FailureCode);
        Assert.Contains("stale event", ignored.FailureMessage);
        Assert.NotNull(ignored.ReplayedAtUtc);
    }

    [Fact]
    public async Task Dead_letter_replay_executor_marks_success_replayed_and_failed_attempt_failed()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var success = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-replay-001"),
                "manual-replay-test",
                "Stored for replay."),
            CancellationToken.None);
        var failure = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-replay-002"),
                "manual-replay-test",
                "Stored for replay."),
            CancellationToken.None);
        var handler = new SampleReplayHandler(exceptionEventId: "event-replay-002");
        var executor = new IntegrationEventDeadLetterReplayExecutor(
            store,
            [handler],
            new StaticTimeProvider(DateTimeOffset.Parse("2026-07-03T02:00:00Z")));

        var successResult = await executor.ReplayAsync(success.Id, CancellationToken.None);
        var failureResult = await executor.ReplayAsync(failure.Id, CancellationToken.None);

        Assert.True(successResult.Succeeded);
        Assert.False(failureResult.Succeeded);
        Assert.Equal(["event-replay-001", "event-replay-002"], handler.ReplayedEventIds);
        Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Replayed, CancellationToken.None));
        var failed = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Failed, CancellationToken.None));
        Assert.Equal(failure.Id, failed.Id);
        Assert.Equal("replay-handler-failed", failed.FailureCode);
    }

    [Fact]
    public async Task Dead_letter_replay_executor_marks_failed_when_handler_resolution_throws()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var message = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-replay-resolution-001"),
                "manual-replay-test",
                "Stored for replay."),
            CancellationToken.None);
        var executor = new IntegrationEventDeadLetterReplayExecutor(
            store,
            [new ThrowingCanReplayHandler()],
            new StaticTimeProvider(DateTimeOffset.Parse("2026-05-26T00:00:00Z")));

        var result = await executor.ReplayAsync(message.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(IntegrationEventDeadLetterStatus.Failed.ToString(), result.Status);
        var failed = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Failed, CancellationToken.None));
        Assert.Equal(message.Id, failed.Id);
        Assert.Equal("replay-handler-failed", failed.FailureCode);
    }

    [Fact]
    public async Task Cap_retry_exhausted_subscribe_failure_dead_letters_handler_exception_without_throwing_from_callback()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var services = new ServiceCollection()
            .AddSingleton<IIntegrationEventDeadLetterStore>(store)
            .AddSingleton<IntegrationEventCapFailureDeadLetterer>()
            .BuildServiceProvider();
        var capMessage = new DotNetCore.CAP.Messages.Message(
            new Dictionary<string, string?>
            {
                [DotNetCore.CAP.Messages.Headers.Group] = "sample.consumer",
                [DotNetCore.CAP.Messages.Headers.MessageName] = "sample.Event",
                [DotNetCore.CAP.Messages.Headers.Exception] = "KnownException-->Business rule failed."
            },
            CreateValidEvent("event-handler-failed-001"));

        var failure = new DotNetCore.CAP.Messages.FailedInfo
        {
            ServiceProvider = services,
            MessageType = DotNetCore.CAP.Messages.MessageType.Subscribe,
            Message = capMessage
        };

        var exception = await Record.ExceptionAsync(() =>
            new IntegrationEventCapFailureDeadLetterer(store).HandleAsync(failure, CancellationToken.None));

        Assert.Null(exception);
        var deadLetter = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        Assert.Equal("event-handler-failed-001", deadLetter.EventId);
        Assert.Equal("handler-retry-exhausted", deadLetter.FailureCode);
        Assert.Contains("Business rule failed", deadLetter.FailureMessage);
    }

    [Fact]
    public async Task Cap_retry_exhausted_subscribe_failure_dead_letters_raw_json_value_after_cap_persistence()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var services = new ServiceCollection()
            .AddSingleton<IIntegrationEventDeadLetterStore>(store)
            .AddSingleton<IntegrationEventCapFailureDeadLetterer>()
            .BuildServiceProvider();
        var sourceEvent = CreateValidEvent("event-handler-failed-json-001");
        var capMessage = new DotNetCore.CAP.Messages.Message(
            new Dictionary<string, string?>
            {
                [DotNetCore.CAP.Messages.Headers.Group] = "sample.consumer",
                [DotNetCore.CAP.Messages.Headers.MessageName] = typeof(SampleIntegrationEvent).FullName,
                [DotNetCore.CAP.Messages.Headers.Exception] = "KnownException-->Business rule failed."
            },
            JsonSerializer.Serialize(sourceEvent, sourceEvent.GetType()));

        var failure = new DotNetCore.CAP.Messages.FailedInfo
        {
            ServiceProvider = services,
            MessageType = DotNetCore.CAP.Messages.MessageType.Subscribe,
            Message = capMessage
        };

        var exception = await Record.ExceptionAsync(() =>
            new IntegrationEventCapFailureDeadLetterer(store).HandleAsync(failure, CancellationToken.None));

        Assert.Null(exception);
        var deadLetter = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        Assert.Equal("event-handler-failed-json-001", deadLetter.EventId);
        Assert.Equal("handler-retry-exhausted", deadLetter.FailureCode);
        Assert.Equal("value", JsonSerializer.Deserialize<SampleIntegrationEvent>(deadLetter.EventJson)?.Payload.Value);
    }

    [Fact]
    public async Task Persistent_dead_letter_store_marks_pending_message_as_replayed_with_relational_mapping()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestDeadLetterDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new TestDeadLetterDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var entity = dbContext.Model.FindEntityType(typeof(IntegrationEventDeadLetter));
        Assert.NotNull(entity);
        Assert.Equal("integration_event_dead_letters", entity.GetTableName());
        // SQLite accepts the annotation, but only PostgreSQL exercises actual jsonb storage.
        Assert.Equal("jsonb", entity.FindProperty(nameof(IntegrationEventDeadLetter.EventJson))?.GetColumnType());
        Assert.Contains(
            entity.GetIndexes(),
            index => IndexProperties(index)
                .SequenceEqual([
                    nameof(IntegrationEventDeadLetter.ConsumerName),
                    nameof(IntegrationEventDeadLetter.Status),
                    nameof(IntegrationEventDeadLetter.DeadLetteredAtUtc)
                ]));

        var store = new PersistentIntegrationEventDeadLetterStore<TestDeadLetterDbContext>(dbContext);
        var message = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-004"),
                "manual-replay-test",
                "Stored for replay."),
            CancellationToken.None);

        await store.MarkReplayedAsync(message.Id, DateTimeOffset.Parse("2026-05-26T00:00:00Z"), CancellationToken.None);

        var replayed = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Replayed, CancellationToken.None));
        Assert.Equal(message.Id, replayed.Id);
        Assert.Equal("event-004", replayed.EventId);
        Assert.NotNull(replayed.ReplayedAtUtc);
        Assert.Empty(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Processed_integration_event_inbox_uses_idempotency_key_not_random_event_id()
    {
        var options = new DbContextOptionsBuilder<TestProcessedEventDbContext>()
            .UseInMemoryDatabase($"processed-inbox-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new TestProcessedEventDbContext(options);
        var first = CreateValidEvent("event-random-001");
        var replay = first with { EventId = "event-random-002" };

        var firstRecorded = await ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            "sample.consumer",
            first,
            SampleProcessedIntegrationEvent.FromInboxRecord,
            CancellationToken.None);
        var replayRecorded = await ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            "sample.consumer",
            replay,
            SampleProcessedIntegrationEvent.FromInboxRecord,
            CancellationToken.None);

        Assert.True(firstRecorded);
        Assert.False(replayRecorded);
        var processed = Assert.Single(dbContext.ProcessedIntegrationEvents.Local);
        Assert.Equal("event-random-001", processed.EventId);
        Assert.Equal("sample:event-random-001", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Processed_integration_event_inbox_classifies_sqlite_unique_conflict_as_already_processed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestProcessedEventDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var setup = new TestProcessedEventDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        await using var firstContext = new TestProcessedEventDbContext(options);
        await using var secondContext = new TestProcessedEventDbContext(options);
        await ProcessedIntegrationEventInbox.TryRecordAsync(
            firstContext,
            firstContext.ProcessedIntegrationEvents,
            "sample.consumer",
            CreateValidEvent("event-conflict-001"),
            SampleProcessedIntegrationEvent.FromInboxRecord,
            CancellationToken.None);
        await ProcessedIntegrationEventInbox.TryRecordAsync(
            secondContext,
            secondContext.ProcessedIntegrationEvents,
            "sample.consumer",
            CreateValidEvent("event-conflict-001"),
            SampleProcessedIntegrationEvent.FromInboxRecord,
            CancellationToken.None);

        await firstContext.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());

        Assert.True(ProcessedIntegrationEventInbox.IsUniqueConflict(
            exception,
            secondContext,
            ProcessedIntegrationEventInbox.UniqueIndexName));
    }

    [Fact]
    public async Task Processed_integration_event_inbox_save_wrapper_ignores_concurrent_duplicate_loser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestProcessedEventDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var setup = new TestProcessedEventDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        await using var firstContext = new TestProcessedEventDbContext(options);
        await using var secondContext = new TestProcessedEventDbContext(options);
        await ProcessedIntegrationEventInbox.TryRecordAsync(
            firstContext,
            firstContext.ProcessedIntegrationEvents,
            "sample.consumer",
            CreateValidEvent("event-race-001"),
            SampleProcessedIntegrationEvent.FromInboxRecord,
            CancellationToken.None);
        await ProcessedIntegrationEventInbox.TryRecordAsync(
            secondContext,
            secondContext.ProcessedIntegrationEvents,
            "sample.consumer",
            CreateValidEvent("event-race-001"),
            SampleProcessedIntegrationEvent.FromInboxRecord,
            CancellationToken.None);

        await firstContext.SaveChangesAsync();
        var saved = await ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync<SampleProcessedIntegrationEvent>(
            secondContext,
            token => secondContext.SaveChangesAsync(token),
            CancellationToken.None);

        Assert.Equal(0, saved);
        await using var assertionContext = new TestProcessedEventDbContext(options);
        Assert.Equal(1, await assertionContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Processed_integration_event_inbox_sync_save_wrapper_ignores_concurrent_duplicate_loser()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TestProcessedEventDbContext>()
            .UseSqlite(connection)
            .Options;
        using (var setup = new TestProcessedEventDbContext(options))
        {
            setup.Database.EnsureCreated();
        }

        using var firstContext = new TestProcessedEventDbContext(options);
        using var secondContext = new TestProcessedEventDbContext(options);
        await ProcessedIntegrationEventInbox.TryRecordAsync(
            firstContext,
            firstContext.ProcessedIntegrationEvents,
            "sample.consumer",
            CreateValidEvent("event-sync-race-001"),
            SampleProcessedIntegrationEvent.FromInboxRecord,
            CancellationToken.None);
        await ProcessedIntegrationEventInbox.TryRecordAsync(
            secondContext,
            secondContext.ProcessedIntegrationEvents,
            "sample.consumer",
            CreateValidEvent("event-sync-race-001"),
            SampleProcessedIntegrationEvent.FromInboxRecord,
            CancellationToken.None);

        firstContext.SaveChanges();
        var saved = ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicate<SampleProcessedIntegrationEvent>(
            secondContext,
            secondContext.SaveChanges);

        Assert.Equal(0, saved);
        using var assertionContext = new TestProcessedEventDbContext(options);
        Assert.Equal(1, assertionContext.ProcessedIntegrationEvents.Count());
    }

    private static string[] IndexProperties(IIndex index)
    {
        return index.Properties.Select(property => property.Name).ToArray();
    }

    private static SampleIntegrationEvent CreateValidEvent(string eventId)
    {
        return new SampleIntegrationEvent(
            EventId: eventId,
            EventType: "sample.Event",
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            SourceService: "sample",
            CorrelationId: $"corr:{eventId}",
            CausationId: $"cause:{eventId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:test",
            IdempotencyKey: $"sample:{eventId}",
            Payload: new SamplePayload("value"));
    }

    private sealed record SampleIntegrationEvent(
        string EventId,
        string EventType,
        int EventVersion,
        DateTimeOffset OccurredAtUtc,
        string SourceService,
        string CorrelationId,
        string CausationId,
        string OrganizationId,
        string EnvironmentId,
        string Actor,
        string IdempotencyKey,
        SamplePayload Payload) : IIntegrationEventEnvelope
    {
        object? IIntegrationEventEnvelope.PayloadObject => Payload;
    }

    private sealed record SamplePayload(string Value);

    private sealed class TestDeadLetterDbContext(DbContextOptions<TestDeadLetterDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureIntegrationEventDeadLetters();
        }
    }

    private sealed class TestProcessedEventDbContext(DbContextOptions<TestProcessedEventDbContext> options)
        : DbContext(options)
    {
        public DbSet<SampleProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<SampleProcessedIntegrationEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SampleProcessedIntegrationEvent>(builder =>
            {
                builder.ToTable("processed_integration_events");
                builder.HasKey(x => x.Id);
                builder.Property(x => x.ConsumerName).IsRequired().HasMaxLength(256);
                builder.Property(x => x.EventId).IsRequired().HasMaxLength(256);
                builder.Property(x => x.EventType).IsRequired().HasMaxLength(256);
                builder.Property(x => x.SourceService).IsRequired().HasMaxLength(128);
                builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(512);
                builder.HasIndex(x => new { x.ConsumerName, x.IdempotencyKey })
                    .IsUnique()
                    .HasDatabaseName(ProcessedIntegrationEventInbox.UniqueIndexName);
            });
        }
    }

    private sealed class SampleProcessedIntegrationEvent
    {
        private SampleProcessedIntegrationEvent()
        {
        }

        private SampleProcessedIntegrationEvent(ProcessedIntegrationEventInboxRecord record)
        {
            Id = Guid.CreateVersion7();
            ConsumerName = record.ConsumerName;
            EventId = record.EventId;
            EventType = record.EventType;
            EventVersion = record.EventVersion;
            SourceService = record.SourceService;
            IdempotencyKey = record.IdempotencyKey;
            ProcessedAtUtc = record.ProcessedAtUtc;
        }

        public Guid Id { get; private set; }
        public string ConsumerName { get; private set; } = string.Empty;
        public string EventId { get; private set; } = string.Empty;
        public string EventType { get; private set; } = string.Empty;
        public int EventVersion { get; private set; }
        public string SourceService { get; private set; } = string.Empty;
        public string IdempotencyKey { get; private set; } = string.Empty;
        public DateTimeOffset ProcessedAtUtc { get; private set; }

        public static SampleProcessedIntegrationEvent FromInboxRecord(ProcessedIntegrationEventInboxRecord record)
        {
            return new SampleProcessedIntegrationEvent(record);
        }
    }

    private sealed class SampleReplayHandler(string? exceptionEventId = null) : IIntegrationEventDeadLetterReplayHandler
    {
        public List<string> ReplayedEventIds { get; } = [];

        public bool CanReplay(IntegrationEventDeadLetterMessage message) => message.EventClrType.Contains(nameof(SampleIntegrationEvent), StringComparison.Ordinal);

        public Task ReplayAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken)
        {
            var integrationEvent = JsonSerializer.Deserialize<SampleIntegrationEvent>(message.EventJson)
                ?? throw new InvalidOperationException("Could not deserialize sample event.");
            ReplayedEventIds.Add(integrationEvent.EventId);
            if (integrationEvent.EventId == exceptionEventId)
            {
                throw new InvalidOperationException("The downstream handler still rejects the event.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCanReplayHandler : IIntegrationEventDeadLetterReplayHandler
    {
        public bool CanReplay(IntegrationEventDeadLetterMessage message) => throw new InvalidOperationException("Handler registry is ambiguous.");

        public Task ReplayAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Replay should not be called when handler matching fails.");
        }
    }

    private sealed class StaticTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
