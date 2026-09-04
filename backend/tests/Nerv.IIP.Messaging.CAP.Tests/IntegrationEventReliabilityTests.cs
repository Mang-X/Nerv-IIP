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
    public async Task Persistent_dead_letter_store_returns_metrics_by_status_and_event_type()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestDeadLetterDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new TestDeadLetterDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var store = new PersistentIntegrationEventDeadLetterStore<TestDeadLetterDbContext>(dbContext);
        var pending = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-metrics-001"),
                "manual-test",
                "Stored for metrics."),
            CancellationToken.None);
        var failed = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-metrics-002"),
                "manual-test",
                "Stored for metrics."),
            CancellationToken.None);
        var ignored = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-metrics-003"),
                "manual-test",
                "Stored for metrics."),
            CancellationToken.None);
        var replayed = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-metrics-004") with { EventType = "sample.OtherEvent" },
                "manual-test",
                "Stored for metrics."),
            CancellationToken.None);

        await store.MarkFailedAsync(failed.Id, "replay-handler-failed", "Replay failed.", DateTimeOffset.UtcNow, CancellationToken.None);
        await store.MarkIgnoredAsync(ignored.Id, "Stale event.", DateTimeOffset.UtcNow, CancellationToken.None);
        await store.MarkReplayedAsync(replayed.Id, DateTimeOffset.UtcNow, CancellationToken.None);

        var metrics = await store.GetMetricsAsync(CancellationToken.None);

        Assert.Equal(1, metrics.PendingCount);
        Assert.Equal(1, metrics.FailedCount);
        Assert.Equal(1, metrics.IgnoredCount);
        Assert.Equal(1, metrics.ReplayedCount);
        var sampleMetrics = Assert.Single(metrics.EventTypes, x => x.EventType == pending.EventType);
        Assert.Equal(1, sampleMetrics.PendingCount);
        Assert.Equal(1, sampleMetrics.FailedCount);
        Assert.Equal(1, sampleMetrics.IgnoredCount);
        Assert.Equal(0, sampleMetrics.ReplayedCount);
        var otherMetrics = Assert.Single(metrics.EventTypes, x => x.EventType == replayed.EventType);
        Assert.Equal(0, otherMetrics.PendingCount);
        Assert.Equal(1, otherMetrics.ReplayedCount);
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
    public async Task Processed_integration_event_inbox_supports_event_id_identity_and_acquires_lock_before_lookup()
    {
        var options = new DbContextOptionsBuilder<TestProcessedEventDbContext>()
            .UseInMemoryDatabase($"processed-event-id-inbox-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new TestProcessedEventDbContext(options);
        var first = CreateValidEvent("event-stable-001");
        var changedPayload = first with
        {
            IdempotencyKey = "sample:changed-business-key",
            Payload = new SamplePayload("changed"),
        };
        var locks = new List<string>();

        Task AcquireLock(DbContext _, string consumer, string identity, CancellationToken __)
        {
            locks.Add($"{consumer}:{identity}");
            return Task.CompletedTask;
        }

        var firstRecorded = await ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            "sample.consumer",
            first,
            SampleProcessedIntegrationEvent.FromInboxRecord,
            ProcessedIntegrationEventInboxIdentity.EventId,
            AcquireLock,
            CancellationToken.None);
        var replayRecorded = await ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            "sample.consumer",
            changedPayload,
            SampleProcessedIntegrationEvent.FromInboxRecord,
            ProcessedIntegrationEventInboxIdentity.EventId,
            AcquireLock,
            CancellationToken.None);

        Assert.True(firstRecorded);
        Assert.False(replayRecorded);
        Assert.Equal(["sample.consumer:event-stable-001", "sample.consumer:event-stable-001"], locks);
        var processed = Assert.Single(dbContext.ProcessedIntegrationEvents.Local);
        Assert.Equal("sample:event-stable-001", processed.IdempotencyKey);
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

    /// <summary>#3101 反向探针①：CAP 存下的原始 wire 体（data URI）被契约拒收后，死信保留解码后的原始 JSON 与可读身份。</summary>
    [Fact]
    public async Task Cap_retry_exhausted_contract_rejected_body_dead_letters_original_wire_json_with_lenient_identity()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        // eventVersion 是字符串而非整数：类型化反序列化必然抛 JsonException，模拟 wire contract converter 拒收。
        const string rawJson = """
            {"eventId":"event-contract-rejected-001","eventType":"sample.Event","eventVersion":"not-a-number",
             "sourceService":"sample","idempotencyKey":"sample:event-contract-rejected-001","payload":{"value":"raw"}}
            """;
        var failure = CreateSubscribeFailure(
            typeof(SampleIntegrationEvent).FullName!,
            "data:SampleIntegrationEvent;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rawJson)),
            "JsonException-->contract rejected.");

        var exception = await Record.ExceptionAsync(() =>
            new IntegrationEventCapFailureDeadLetterer(store).HandleAsync(failure, CancellationToken.None));

        Assert.Null(exception);
        var deadLetter = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        // 失败码是运维/工具可见面：与 handler-retry-exhausted 同族，钉硬字面量。
        Assert.Equal("contract-rejected", deadLetter.FailureCode);
        Assert.Equal(IntegrationEventCapFailureDeadLetterer.ContractRejectedFailureCode, deadLetter.FailureCode);
        Assert.Equal("event-contract-rejected-001", deadLetter.EventId);
        Assert.Equal("sample.Event", deadLetter.EventType);
        Assert.Null(deadLetter.EventVersion);
        Assert.Equal("sample", deadLetter.SourceService);
        Assert.Equal("sample:event-contract-rejected-001", deadLetter.IdempotencyKey);
        Assert.Equal(typeof(SampleIntegrationEvent).FullName, deadLetter.EventClrType);
        Assert.Equal(rawJson, deadLetter.EventJson);
        // 两段都要在：契约拒收的真实异常文本（类型化反序列化自己抛的）与 transport 头文本；缺任一即红。
        Assert.Contains("could not be converted", deadLetter.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("(JsonException-->contract rejected.)", deadLetter.FailureMessage, StringComparison.Ordinal);
        using var forensic = JsonDocument.Parse(deadLetter.EventJson);
        Assert.Equal("not-a-number", forensic.RootElement.GetProperty("eventVersion").GetString());
    }

    /// <summary>M12：原始体是合法 JSON 但根不是对象——身份列全 null，原文仍逐字保留。</summary>
    [Fact]
    public async Task Cap_retry_exhausted_contract_rejected_non_object_json_root_dead_letters_verbatim_without_identity()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        const string rawJson = "[\"not\",\"an\",\"envelope\"]";
        await new IntegrationEventCapFailureDeadLetterer(store).HandleAsync(
            CreateSubscribeFailure(typeof(SampleIntegrationEvent).FullName!, rawJson, "JsonException-->root is an array."),
            CancellationToken.None);

        var deadLetter = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        Assert.Equal("contract-rejected", deadLetter.FailureCode);
        Assert.Null(deadLetter.EventId);
        Assert.Null(deadLetter.EventType);
        Assert.Null(deadLetter.EventVersion);
        Assert.Null(deadLetter.SourceService);
        Assert.Null(deadLetter.IdempotencyKey);
        Assert.Equal(rawJson, deadLetter.EventJson);
    }

    /// <summary>M14：带 data: 前缀但 base64 非法——不解码、按原文处理（非 JSON → rawBody 包装），不得抛出丢消息。</summary>
    [Fact]
    public async Task Cap_retry_exhausted_invalid_base64_data_uri_dead_letters_verbatim_inside_json_wrapper()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        const string value = "data:SampleIntegrationEvent;base64,@@not-base64@@";
        var exception = await Record.ExceptionAsync(() => new IntegrationEventCapFailureDeadLetterer(store).HandleAsync(
            CreateSubscribeFailure(typeof(SampleIntegrationEvent).FullName!, value, "JsonException-->garbage."),
            CancellationToken.None));

        Assert.Null(exception);
        var deadLetter = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        using var wrapper = JsonDocument.Parse(deadLetter.EventJson);
        Assert.Equal(value, wrapper.RootElement.GetProperty("rawBody").GetString());
    }

    /// <summary>M15：无 data: 前缀的纯 JSON 字符串体被契约拒收——直接按 JSON 读身份并逐字保留。</summary>
    [Fact]
    public async Task Cap_retry_exhausted_plain_json_string_body_without_data_prefix_dead_letters_contract_rejected()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        const string rawJson = "{\"eventId\":\"event-plain-001\",\"eventVersion\":\"x\",\"sourceService\":\"sample\"}";
        await new IntegrationEventCapFailureDeadLetterer(store).HandleAsync(
            CreateSubscribeFailure(typeof(SampleIntegrationEvent).FullName!, rawJson, "JsonException-->plain."),
            CancellationToken.None);

        var deadLetter = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        Assert.Equal("contract-rejected", deadLetter.FailureCode);
        Assert.Equal("event-plain-001", deadLetter.EventId);
        Assert.Equal("sample", deadLetter.SourceService);
        Assert.Equal(rawJson, deadLetter.EventJson);
    }

    /// <summary>
    /// 阻断 2：身份列与失败信息取自生产者可控的 wire JSON / CAP 异常头，列是定长的；持久化 AddAsync 路径必须像
    /// MarkFailed* 一样截断，否则 22001 在阈值回调里被吞、死信丢失（与 #3101 根因同一失败类）。
    /// </summary>
    [Fact]
    public async Task Persistent_dead_letter_store_truncates_producer_controlled_columns_on_add()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestDeadLetterDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new TestDeadLetterDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var store = new PersistentIntegrationEventDeadLetterStore<TestDeadLetterDbContext>(dbContext);
        var overlong = new string('x', 5000);

        var stored = await store.AddAsync(
            new IntegrationEventDeadLetterMessage(
                Guid.CreateVersion7(),
                "c" + overlong,
                "e" + overlong,
                "t" + overlong,
                1,
                "s" + overlong,
                "k" + overlong,
                "clr" + overlong,
                "{\"raw\":true}",
                "f" + overlong,
                "m" + overlong,
                IntegrationEventDeadLetterStatus.Pending,
                DateTimeOffset.UtcNow,
                null),
            CancellationToken.None);

        var persisted = Assert.Single(await store.ListAsync(null, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        Assert.Equal(stored.Id, persisted.Id);
        Assert.Equal(IntegrationEventDeadLetter.ConsumerNameMaxLength, persisted.ConsumerName.Length);
        Assert.Equal(IntegrationEventDeadLetter.EventIdMaxLength, persisted.EventId!.Length);
        Assert.Equal(IntegrationEventDeadLetter.EventTypeMaxLength, persisted.EventType!.Length);
        Assert.Equal(IntegrationEventDeadLetter.SourceServiceMaxLength, persisted.SourceService!.Length);
        Assert.Equal(IntegrationEventDeadLetter.IdempotencyKeyMaxLength, persisted.IdempotencyKey!.Length);
        Assert.Equal(IntegrationEventDeadLetter.EventClrTypeMaxLength, persisted.EventClrType.Length);
        Assert.Equal(IntegrationEventDeadLetter.FailureCodeMaxLength, persisted.FailureCode.Length);
        Assert.Equal(IntegrationEventDeadLetter.FailureMessageMaxLength, persisted.FailureMessage.Length);
        Assert.StartsWith("e", persisted.EventId, StringComparison.Ordinal);
        Assert.Equal("{\"raw\":true}", persisted.EventJson);
        // 上限本身也是运维可见面（与 EF 列定义同源），钉硬字面量。
        Assert.Equal((200, 200, 300, 150, 500, 500, 100, 1000), (
            IntegrationEventDeadLetter.ConsumerNameMaxLength,
            IntegrationEventDeadLetter.EventIdMaxLength,
            IntegrationEventDeadLetter.EventTypeMaxLength,
            IntegrationEventDeadLetter.SourceServiceMaxLength,
            IntegrationEventDeadLetter.IdempotencyKeyMaxLength,
            IntegrationEventDeadLetter.EventClrTypeMaxLength,
            IntegrationEventDeadLetter.FailureCodeMaxLength,
            IntegrationEventDeadLetter.FailureMessageMaxLength));
    }

    /// <summary>#3101 反向探针②：根本不是 JSON 的 wire 体也不能被丢——原文进 JSON 包装，列仍可写入 jsonb。</summary>
    [Fact]
    public async Task Cap_retry_exhausted_non_json_body_dead_letters_verbatim_inside_json_wrapper()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var failure = CreateSubscribeFailure(
            typeof(SampleIntegrationEvent).FullName!,
            "data:SampleIntegrationEvent;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<not-json/>")),
            "JsonException-->body is not JSON.");

        await new IntegrationEventCapFailureDeadLetterer(store).HandleAsync(failure, CancellationToken.None);

        var deadLetter = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        Assert.Equal(IntegrationEventCapFailureDeadLetterer.ContractRejectedFailureCode, deadLetter.FailureCode);
        Assert.Null(deadLetter.EventId);
        using var wrapper = JsonDocument.Parse(deadLetter.EventJson);
        Assert.Equal("rawBody", IntegrationEventCapFailureDeadLetterer.RawBodyPropertyName);
        Assert.Equal("<not-json/>", wrapper.RootElement.GetProperty("rawBody").GetString());
    }

    /// <summary>
    /// #3101 反向探针③：违反自身 wire 契约（converter 在 Write 侧也校验）的 CLR 事件，<c>Create</c> 不再自己抛，
    /// 死信保留全部信封成员与 payload 用于取证；合法事件仍走类型化序列化（形状零漂移由既有用例钉住）。
    /// </summary>
    [Fact]
    public void Dead_letter_create_keeps_forensic_envelope_for_events_that_violate_their_own_write_contract()
    {
        var invalid = new StrictIntegrationEvent(
            EventId: "event-strict-001",
            EventType: "strict.Event",
            EventVersion: 1, // 契约要求 2
            OccurredAtUtc: DateTimeOffset.Parse("2026-09-03T00:00:00Z"),
            SourceService: "strict",
            CorrelationId: "corr:strict-001",
            CausationId: "cause:strict-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:test",
            IdempotencyKey: "strict:event-strict-001",
            Payload: new StrictPayload("strict-value"));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid));

        var deadLetter = IntegrationEventDeadLetterMessage.Create("strict.consumer", invalid, "unsupported-version", "version 1 is not accepted");

        Assert.Equal("event-strict-001", deadLetter.EventId);
        Assert.Equal(1, deadLetter.EventVersion);
        Assert.Equal(typeof(StrictIntegrationEvent).FullName, deadLetter.EventClrType);
        using var forensic = JsonDocument.Parse(deadLetter.EventJson);
        Assert.Equal("event-strict-001", forensic.RootElement.GetProperty("eventId").GetString());
        Assert.Equal(1, forensic.RootElement.GetProperty("eventVersion").GetInt32());
        Assert.Equal("strict:event-strict-001", forensic.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal("strict-value", forensic.RootElement.GetProperty("payload").GetProperty("value").GetString());

        var valid = invalid with { EventVersion = 2 };
        var typed = IntegrationEventDeadLetterMessage.Create("strict.consumer", valid, "handler-failed", "business rule");
        Assert.Equal(JsonSerializer.Serialize(valid), typed.EventJson);
    }

    /// <summary>
    /// 阻断 1（#3101 复审）：信封 payload 带**属性级** converter（本仓生产形态如 MesMachineTimeFactStatusJsonConverter：
    /// JsonStringEnumConverter&lt;T&gt;(allowIntegerValues:false)），枚举值未定义时类型化序列化在 EnumConverter.Write 抛。
    /// 回退投影必须完全不经 converter，否则 Create 依旧抛、死信依旧丢。
    /// </summary>
    [Fact]
    public void Dead_letter_create_keeps_forensic_envelope_when_a_nested_property_converter_throws()
    {
        var invalid = new NestedConverterIntegrationEvent(
            EventId: "event-nested-001",
            EventType: "nested.Event",
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-09-03T00:00:00Z"),
            SourceService: "nested",
            CorrelationId: "corr:nested-001",
            CausationId: "cause:nested-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:test",
            IdempotencyKey: "nested:event-nested-001",
            Payload: new NestedStatusPayload((NestedStatus)99, "line-1"));
        var typedFailure = Record.Exception(() => JsonSerializer.Serialize(invalid));
        Assert.IsAssignableFrom<JsonException>(typedFailure);

        var deadLetter = IntegrationEventDeadLetterMessage.Create("nested.consumer", invalid, "unsupported-version", "nested converter rejected");

        Assert.Equal("event-nested-001", deadLetter.EventId);
        Assert.Equal(typeof(NestedConverterIntegrationEvent).FullName, deadLetter.EventClrType);
        using var forensic = JsonDocument.Parse(deadLetter.EventJson);
        Assert.Equal("event-nested-001", forensic.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("nested:event-nested-001", forensic.RootElement.GetProperty("idempotencyKey").GetString());
        var payload = forensic.RootElement.GetProperty("payload");
        Assert.Equal("99", payload.GetProperty("status").GetString());
        Assert.Equal("line-1", payload.GetProperty("line").GetString());

        // 合法值仍走类型化路径：形状与类型化序列化逐字相同（零漂移）。
        var valid = invalid with { Payload = new NestedStatusPayload(NestedStatus.Active, "line-1") };
        Assert.Equal(JsonSerializer.Serialize(valid), IntegrationEventDeadLetterMessage.Create("nested.consumer", valid, "handler-failed", "x").EventJson);
    }

    private static DotNetCore.CAP.Messages.FailedInfo CreateSubscribeFailure(string typeHeader, object value, string exceptionHeader) =>
        new()
        {
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            MessageType = DotNetCore.CAP.Messages.MessageType.Subscribe,
            Message = new DotNetCore.CAP.Messages.Message(
                new Dictionary<string, string?>
                {
                    [DotNetCore.CAP.Messages.Headers.Group] = "sample.consumer",
                    [DotNetCore.CAP.Messages.Headers.Type] = typeHeader,
                    [DotNetCore.CAP.Messages.Headers.MessageName] = "sample.topic",
                    [DotNetCore.CAP.Messages.Headers.Exception] = exceptionHeader
                },
                value)
        };

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

[System.Text.Json.Serialization.JsonConverter(typeof(StrictIntegrationEventJsonConverter))]
public sealed record StrictIntegrationEvent(
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
    StrictPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

/// <summary>模拟本仓 Read/Write 双侧校验的 wire contract converter（如 AssetUnavailableV2WireContract）。</summary>
public sealed class StrictIntegrationEventJsonConverter : System.Text.Json.Serialization.JsonConverter<StrictIntegrationEvent>
{
    public override StrictIntegrationEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonSerializer.Deserialize<StrictDto>(ref reader, options) ?? throw new JsonException("envelope required");
        var integrationEvent = new StrictIntegrationEvent(value.EventId, value.EventType, value.EventVersion, value.OccurredAtUtc, value.SourceService, value.CorrelationId, value.CausationId, value.OrganizationId, value.EnvironmentId, value.Actor, value.IdempotencyKey, value.Payload);
        Validate(integrationEvent);
        return integrationEvent;
    }

    public override void Write(Utf8JsonWriter writer, StrictIntegrationEvent value, JsonSerializerOptions options)
    {
        Validate(value);
        JsonSerializer.Serialize(writer, new StrictDto(value.EventId, value.EventType, value.EventVersion, value.OccurredAtUtc, value.SourceService, value.CorrelationId, value.CausationId, value.OrganizationId, value.EnvironmentId, value.Actor, value.IdempotencyKey, value.Payload), options);
    }

    private static void Validate(StrictIntegrationEvent value)
    {
        if (value.EventVersion != 2)
        {
            throw new JsonException("strict envelope requires eventVersion 2.");
        }
    }

    private sealed record StrictDto(
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
        StrictPayload Payload);
}

public sealed record StrictPayload(string Value);

public enum NestedStatus
{
    Active = 1,
    Retired = 2,
}

/// <summary>与生产 MesMachineTimeFactStatusJsonConverter 同形：allowIntegerValues:false，未定义值在 Write 侧抛。</summary>
public sealed class NestedStatusJsonConverter() : System.Text.Json.Serialization.JsonStringEnumConverter<NestedStatus>(namingPolicy: null, allowIntegerValues: false);

public sealed record NestedStatusPayload(
    [property: System.Text.Json.Serialization.JsonConverter(typeof(NestedStatusJsonConverter))] NestedStatus Status,
    string Line);

public sealed record NestedConverterIntegrationEvent(
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
    NestedStatusPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}
