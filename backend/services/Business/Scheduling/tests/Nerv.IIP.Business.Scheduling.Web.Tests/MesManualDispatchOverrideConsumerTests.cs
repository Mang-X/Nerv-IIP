using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class MesManualDispatchOverrideConsumerTests
{
    [Fact]
    public async Task Handle_persists_real_mes_dispatch_as_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-override-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var handler = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(
            db, new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db));
        var start = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var integrationEvent = new MesOperationTaskManuallyDispatchedIntegrationEvent(
            "evt-1", MesIntegrationEventTypes.OperationTaskManuallyDispatched, 1, start,
            MesIntegrationEventSources.BusinessMes, "corr-1", "cause-1", "org-1", "env-1",
            "user:dispatcher", "mes:dispatch:OP-1", new OperationTaskManuallyDispatchedPayload(
                "WO-1", "OP-1", 10, "DEV-1", "WC-1", start, start.AddHours(1), start, 1));

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.Equal("OP-1", persisted.OperationId);
        Assert.Equal("DEV-1", persisted.ResourceId);
        Assert.Equal(start, persisted.StartUtc);
        Assert.Equal(1, persisted.SourceRevision);
        var processed = await db.ProcessedIntegrationEvents.SingleAsync();
        Assert.Equal(integrationEvent.EventId, processed.EventId);
        Assert.Equal(integrationEvent.SourceService, processed.SourceService);
        Assert.Equal(integrationEvent.IdempotencyKey, processed.IdempotencyKey);
    }

    [Fact]
    public async Task Clear_before_older_dispatch_creates_tombstone_and_rejects_stale_dispatch()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-clear-first-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var dispatchHandler = CreateDispatchHandler(db, deadLetters);
        var clearHandler = CreateClearHandler(db, deadLetters);
        var now = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

        await clearHandler.HandleAsync(CreateClearEvent("evt-clear-2", now, revision: 2), CancellationToken.None);
        await dispatchHandler.HandleAsync(CreateEvent("evt-dispatch-1", now.AddMinutes(-1), "DEV-OLD", revision: 1), CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.False(persisted.IsActive);
        Assert.Equal(2, persisted.SourceRevision);
        Assert.Equal("device-cleared", persisted.ClearedReasonCode);
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Duplicate_clear_is_processed_once()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-clear-duplicate-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var handler = CreateClearHandler(db, new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = CreateClearEvent("evt-clear", At(8), revision: 2);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.False(persisted.IsActive);
        Assert.Equal(2, persisted.SourceRevision);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Newer_dispatch_reactivates_a_cleared_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-reactivate-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var dispatchHandler = CreateDispatchHandler(db, deadLetters);
        var clearHandler = CreateClearHandler(db, deadLetters);
        var now = At(8);

        await dispatchHandler.HandleAsync(CreateEvent("evt-dispatch-1", now, "DEV-1", revision: 1), CancellationToken.None);
        await clearHandler.HandleAsync(CreateClearEvent("evt-clear-2", now.AddMinutes(1), revision: 2), CancellationToken.None);
        await dispatchHandler.HandleAsync(CreateEvent("evt-dispatch-3", now.AddMinutes(2), "DEV-3", revision: 3), CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal(3, persisted.SourceRevision);
        Assert.Equal("DEV-3", persisted.ResourceId);
        Assert.Null(persisted.ClearedReasonCode);
    }

    [Fact]
    public async Task Clear_does_not_deactivate_a_scheduling_api_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-clear-lineage-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        db.ScheduleOperationOverrides.Add(ScheduleOperationOverride.Create(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-API", "WC-1",
            At(8), At(9), "manual-override", "scheduling-api", null,
            "user:planner", At(7), At(7)));
        await db.SaveChangesAsync();
        var handler = CreateClearHandler(db, new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateClearEvent("evt-clear", At(10), revision: 2), CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal("scheduling-api", persisted.SourceType);
        Assert.Null(persisted.SourceRevision);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Invalid_clear_enters_dead_letter_once_without_mutating_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-clear-invalid-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateClearHandler(db, deadLetters);
        var invalid = CreateClearEvent("evt-clear-invalid", At(8), revision: 0);

        await handler.HandleAsync(invalid, CancellationToken.None);
        await handler.HandleAsync(invalid, CancellationToken.None);

        Assert.Empty(db.ScheduleOperationOverrides);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Negative_clear_revision_enters_dead_letter_once_without_mutating_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-clear-negative-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateClearHandler(db, deadLetters);
        var invalid = CreateClearEvent("evt-clear-negative", At(8), revision: -1);

        await handler.HandleAsync(invalid, CancellationToken.None);
        await handler.HandleAsync(invalid, CancellationToken.None);

        Assert.Empty(db.ScheduleOperationOverrides);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Overlength_clear_identity_enters_dead_letter_once_without_mutating_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-clear-overlength-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateClearHandler(db, deadLetters);
        var integrationEvent = CreateClearEvent("evt-clear-overlength", At(8), revision: 2);
        var invalid = integrationEvent with
        {
            Payload = integrationEvent.Payload with { WorkOrderId = new string('W', 129) }
        };

        await handler.HandleAsync(invalid, CancellationToken.None);
        await handler.HandleAsync(invalid, CancellationToken.None);

        Assert.Empty(db.ScheduleOperationOverrides);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Padded_clear_identity_enters_dead_letter_once_without_mutating_existing_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-clear-padded-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var baseline = At(8);
        var existing = ScheduleOperationOverride.Create(
            "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-1", "WC-1",
            baseline, baseline.AddHours(1), "mes-manual-dispatch", "mes-dispatch",
            "evt-dispatch-1", "user:dispatcher", baseline, baseline);
        Assert.True(existing.TryApplyMesDispatch(
            "DEV-1", "WC-1", baseline, baseline.AddHours(1), "evt-dispatch-1",
            "user:dispatcher", 1, baseline, baseline));
        db.ScheduleOperationOverrides.Add(existing);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateClearHandler(db, deadLetters);
        var integrationEvent = CreateClearEvent("evt-clear-padded", At(9), revision: 2);
        var invalid = integrationEvent with
        {
            Payload = integrationEvent.Payload with { OperationTaskId = " OP-1 " }
        };

        await handler.HandleAsync(invalid, CancellationToken.None);
        await handler.HandleAsync(invalid, CancellationToken.None);

        var persisted = Assert.Single(await db.ScheduleOperationOverrides.ToArrayAsync());
        Assert.True(persisted.IsActive);
        Assert.Equal(1, persisted.SourceRevision);
        Assert.Equal("evt-dispatch-1", persisted.SourceEventId);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_dead_letters_invalid_payload_once_without_throwing()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-invalid-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(db, deadLetters);
        var now = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var integrationEvent = CreateEvent("evt-invalid", now, resourceId: "");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Empty(db.ScheduleOperationOverrides);
        Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Negative_dispatch_revision_enters_dead_letter_once_without_mutating_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-negative-revision-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateDispatchHandler(db, deadLetters);
        var invalid = CreateEvent("evt-negative-revision", At(8), "DEV-1", revision: -1);

        await handler.HandleAsync(invalid, CancellationToken.None);
        await handler.HandleAsync(invalid, CancellationToken.None);

        Assert.Empty(db.ScheduleOperationOverrides);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Overlength_dispatch_actor_enters_dead_letter_once_without_mutating_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-overlength-actor-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateDispatchHandler(db, deadLetters);
        var invalid = CreateEvent("evt-overlength-actor", At(8), "DEV-1") with
        {
            Actor = $"user:{new string('a', 124)}"
        };

        await handler.HandleAsync(invalid, CancellationToken.None);
        await handler.HandleAsync(invalid, CancellationToken.None);

        Assert.Empty(db.ScheduleOperationOverrides);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Overlength_clear_actor_enters_dead_letter_once_without_mutating_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-clear-overlength-actor-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateClearHandler(db, deadLetters);
        var invalid = CreateClearEvent("evt-clear-overlength-actor", At(8), revision: 1) with
        {
            Actor = $"user:{new string('a', 124)}"
        };

        await handler.HandleAsync(invalid, CancellationToken.None);
        await handler.HandleAsync(invalid, CancellationToken.None);

        Assert.Empty(db.ScheduleOperationOverrides);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Overlength_dispatch_envelope_storage_identities_are_safely_deduplicated()
    {
        var variants = new MesOperationTaskManuallyDispatchedIntegrationEvent[]
        {
            CreateEvent(new string('e', 257), At(8), "DEV-1"),
            CreateEvent("evt-overlength-source", At(8), "DEV-1") with
            {
                SourceService = new string('s', 129)
            },
            CreateEvent("evt-overlength-idempotency", At(8), "DEV-1") with
            {
                IdempotencyKey = new string('i', 513)
            },
            CreateEvent(" padded-event-id ", At(8), "DEV-1"),
            CreateEvent("evt-padded-source", At(8), "DEV-1") with
            {
                SourceService = " business-mes "
            },
            CreateEvent("evt-padded-idempotency", At(8), "DEV-1") with
            {
                IdempotencyKey = " mes:dispatch:padded "
            }
        };

        foreach (var invalid in variants)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"mes-dispatch-overlength-envelope-{Guid.NewGuid():N}").Options;
            await using var db = new ApplicationDbContext(options, new NoopMediator());
            var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
            var handler = CreateDispatchHandler(db, deadLetters);

            await handler.HandleAsync(invalid, CancellationToken.None);
            await handler.HandleAsync(invalid, CancellationToken.None);

            Assert.Empty(db.ScheduleOperationOverrides);
            var processed = Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());
            Assert.InRange(processed.EventId.Length, 1, 128);
            Assert.InRange(processed.SourceService.Length, 1, 128);
            Assert.InRange(processed.IdempotencyKey.Length, 1, 512);
            Assert.Equal(processed.EventId.Trim(), processed.EventId);
            Assert.Equal(processed.SourceService.Trim(), processed.SourceService);
            Assert.Equal(processed.IdempotencyKey.Trim(), processed.IdempotencyKey);
            var deadLetter = Assert.Single(await deadLetters.ListAsync(
                MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
            Assert.InRange(deadLetter.EventId!.Length, 1, 200);
            Assert.InRange(deadLetter.SourceService!.Length, 1, 150);
            Assert.InRange(deadLetter.IdempotencyKey!.Length, 1, 500);
        }
    }

    [Fact]
    public async Task Overlength_clear_envelope_storage_identities_are_safely_deduplicated()
    {
        var variants = new MesOperationTaskManualDispatchClearedIntegrationEvent[]
        {
            CreateClearEvent(new string('e', 257), At(8), revision: 1),
            CreateClearEvent("evt-clear-overlength-source", At(8), revision: 1) with
            {
                SourceService = new string('s', 129)
            },
            CreateClearEvent("evt-clear-overlength-idempotency", At(8), revision: 1) with
            {
                IdempotencyKey = new string('i', 513)
            },
            CreateClearEvent(" padded-clear-event-id ", At(8), revision: 1),
            CreateClearEvent("evt-clear-padded-source", At(8), revision: 1) with
            {
                SourceService = " business-mes "
            },
            CreateClearEvent("evt-clear-padded-idempotency", At(8), revision: 1) with
            {
                IdempotencyKey = " mes:dispatch-clear:padded "
            }
        };

        foreach (var invalid in variants)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"mes-clear-overlength-envelope-{Guid.NewGuid():N}").Options;
            await using var db = new ApplicationDbContext(options, new NoopMediator());
            var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
            var handler = CreateClearHandler(db, deadLetters);

            await handler.HandleAsync(invalid, CancellationToken.None);
            await handler.HandleAsync(invalid, CancellationToken.None);

            Assert.Empty(db.ScheduleOperationOverrides);
            var processed = Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());
            Assert.InRange(processed.EventId.Length, 1, 128);
            Assert.InRange(processed.SourceService.Length, 1, 128);
            Assert.InRange(processed.IdempotencyKey.Length, 1, 512);
            Assert.Equal(processed.EventId.Trim(), processed.EventId);
            Assert.Equal(processed.SourceService.Trim(), processed.SourceService);
            Assert.Equal(processed.IdempotencyKey.Trim(), processed.IdempotencyKey);
            var deadLetter = Assert.Single(await deadLetters.ListAsync(
                MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
            Assert.InRange(deadLetter.EventId!.Length, 1, 200);
            Assert.InRange(deadLetter.SourceService!.Length, 1, 150);
            Assert.InRange(deadLetter.IdempotencyKey!.Length, 1, 500);
        }
    }

    [Fact]
    public async Task Non_unique_database_failure_is_not_misclassified_as_override_insert_race()
    {
        var interceptor = new FailFirstSaveInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-non-unique-failure-{Guid.NewGuid():N}")
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var handler = CreateDispatchHandler(db, new InMemoryIntegrationEventDeadLetterStore());

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(
            CreateEvent("evt-non-unique-failure", At(8), "DEV-1"), CancellationToken.None));

        Assert.Equal(FailFirstSaveInterceptor.FailureMessage, exception.Message);
    }

    [Fact]
    public async Task Distinct_invalid_idempotency_keys_get_distinct_safe_inbox_identities()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-distinct-invalid-identities-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateDispatchHandler(db, deadLetters);
        var first = CreateEvent("evt-invalid-idempotency", At(8), "DEV-1") with
        {
            IdempotencyKey = new string('a', 513)
        };
        var second = first with { IdempotencyKey = new string('b', 513) };

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(second, CancellationToken.None);

        var identities = await db.ProcessedIntegrationEvents
            .Select(x => x.IdempotencyKey)
            .ToArrayAsync();
        Assert.Equal(2, identities.Length);
        Assert.Equal(2, identities.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, (await deadLetters.ListAsync(
            MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Valid_512_character_inbox_key_is_safely_projected_to_invalid_payload_dead_letter()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-dead-letter-key-limit-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateDispatchHandler(db, deadLetters);
        var idempotencyKey = new string('i', 512);
        var invalid = CreateEvent("evt-dead-letter-key-limit", At(8), "DEV-1", revision: -1) with
        {
            IdempotencyKey = idempotencyKey
        };

        await handler.HandleAsync(invalid, CancellationToken.None);
        await handler.HandleAsync(invalid, CancellationToken.None);

        var processed = await db.ProcessedIntegrationEvents.SingleAsync();
        Assert.Equal(idempotencyKey, processed.IdempotencyKey);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
        Assert.InRange(deadLetter.IdempotencyKey!.Length, 1, 500);
        Assert.NotEqual(idempotencyKey, deadLetter.IdempotencyKey);
    }

    [Fact]
    public async Task Guard_invalid_dispatch_envelopes_use_safe_deduplicated_persistence()
    {
        var variants = new[]
        {
            CreateEvent("evt-overlength-event-type", At(8), "DEV-1") with
            {
                EventType = new string('t', 2000)
            },
            CreateEvent("evt-padded-event-type", At(8), "DEV-1") with
            {
                EventType = $" {MesIntegrationEventTypes.OperationTaskManuallyDispatched} "
            },
            CreateEvent("evt-unsupported-version", At(8), "DEV-1") with
            {
                EventVersion = 2
            },
            CreateEvent("evt-missing-source", At(8), "DEV-1") with
            {
                SourceService = null!
            },
            CreateEvent("evt-missing-idempotency", At(8), "DEV-1") with
            {
                IdempotencyKey = null!
            }
        };

        foreach (var invalid in variants)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"mes-dispatch-invalid-event-type-{Guid.NewGuid():N}").Options;
            await using var db = new ApplicationDbContext(options, new NoopMediator());
            var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
            var handler = CreateDispatchHandler(db, deadLetters);

            await handler.HandleAsync(invalid, CancellationToken.None);
            await handler.HandleAsync(invalid, CancellationToken.None);

            Assert.Empty(db.ScheduleOperationOverrides);
            var processed = Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());
            Assert.InRange(processed.EventType.Length, 1, 256);
            Assert.InRange(processed.SourceService.Length, 1, 128);
            Assert.InRange(processed.IdempotencyKey.Length, 1, 512);
            var deadLetter = Assert.Single(await deadLetters.ListAsync(
                MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
            Assert.InRange(deadLetter.EventType!.Length, 1, 300);
            Assert.InRange(deadLetter.SourceService!.Length, 1, 150);
            Assert.InRange(deadLetter.IdempotencyKey!.Length, 1, 500);
            Assert.InRange(deadLetter.FailureMessage.Length, 1, 1000);
        }
    }

    [Fact]
    public async Task Guard_invalid_clear_envelopes_use_safe_deduplicated_persistence()
    {
        var variants = new[]
        {
            CreateClearEvent("evt-clear-overlength-event-type", At(8), revision: 1) with
            {
                EventType = new string('t', 2000)
            },
            CreateClearEvent("evt-clear-padded-event-type", At(8), revision: 1) with
            {
                EventType = $" {MesIntegrationEventTypes.OperationTaskManualDispatchCleared} "
            },
            CreateClearEvent("evt-clear-unsupported-version", At(8), revision: 1) with
            {
                EventVersion = 2
            },
            CreateClearEvent("evt-clear-missing-source", At(8), revision: 1) with
            {
                SourceService = null!
            },
            CreateClearEvent("evt-clear-missing-idempotency", At(8), revision: 1) with
            {
                IdempotencyKey = null!
            }
        };

        foreach (var invalid in variants)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"mes-clear-invalid-event-type-{Guid.NewGuid():N}").Options;
            await using var db = new ApplicationDbContext(options, new NoopMediator());
            var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
            var handler = CreateClearHandler(db, deadLetters);

            await handler.HandleAsync(invalid, CancellationToken.None);
            await handler.HandleAsync(invalid, CancellationToken.None);

            Assert.Empty(db.ScheduleOperationOverrides);
            var processed = Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());
            Assert.InRange(processed.EventType.Length, 1, 256);
            Assert.InRange(processed.SourceService.Length, 1, 128);
            Assert.InRange(processed.IdempotencyKey.Length, 1, 512);
            var deadLetter = Assert.Single(await deadLetters.ListAsync(
                MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
            Assert.InRange(deadLetter.EventType!.Length, 1, 300);
            Assert.InRange(deadLetter.SourceService!.Length, 1, 150);
            Assert.InRange(deadLetter.IdempotencyKey!.Length, 1, 500);
            Assert.InRange(deadLetter.FailureMessage.Length, 1, 1000);
        }
    }

    [Fact]
    public async Task Same_invalid_idempotency_key_on_distinct_envelopes_does_not_collide()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-invalid-key-envelope-fingerprint-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateDispatchHandler(db, deadLetters);
        var invalidKey = new string('i', 513);
        var first = CreateEvent("evt-invalid-key-first", At(8), "DEV-1") with
        {
            IdempotencyKey = invalidKey
        };
        var second = CreateEvent("evt-invalid-key-second", At(9), "DEV-2") with
        {
            IdempotencyKey = invalidKey
        };

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(second, CancellationToken.None);

        var identities = await db.ProcessedIntegrationEvents
            .Select(x => x.IdempotencyKey)
            .ToArrayAsync();
        Assert.Equal(2, identities.Length);
        Assert.Equal(2, identities.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, (await deadLetters.ListAsync(
            MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Legacy_revision_zero_dispatches_converge_by_source_time()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-stale-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var handler = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(
            db, new InMemoryIntegrationEventDeadLetterStore());
        var older = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var newer = older.AddMinutes(10);

        await handler.HandleAsync(CreateEvent("evt-new", newer, "DEV-NEW", revision: 0), CancellationToken.None);
        await handler.HandleAsync(CreateEvent("evt-old", older, "DEV-OLD", revision: 0), CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.Equal("DEV-NEW", persisted.ResourceId);
        Assert.Equal(newer, persisted.SourceOccurredAtUtc);
        Assert.Null(persisted.SourceRevision);
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Legacy_revision_zero_cannot_replace_an_active_positive_revision_with_a_later_timestamp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-legacy-after-versioned-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var handler = CreateDispatchHandler(db, new InMemoryIntegrationEventDeadLetterStore());
        var versionedAt = At(8);

        await handler.HandleAsync(
            CreateEvent("evt-versioned-3", versionedAt, "DEV-VERSIONED", revision: 3),
            CancellationToken.None);
        await handler.HandleAsync(
            CreateEvent("evt-legacy-late", versionedAt.AddHours(2), "DEV-LEGACY", revision: 0),
            CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal(3, persisted.SourceRevision);
        Assert.Equal("DEV-VERSIONED", persisted.ResourceId);
        Assert.Equal("evt-versioned-3", persisted.SourceEventId);
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Concurrent_stale_clear_snapshot_reloads_and_converges_to_newer_dispatch()
    {
        var databaseName = $"mes-dispatch-clear-concurrency-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot).Options;
        var baseline = At(8);
        await using (var seed = new ApplicationDbContext(Options(), new NoopMediator()))
        {
            var fact = ScheduleOperationOverride.Create(
                "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-1", "WC-1",
                baseline, baseline.AddHours(1), "mes-manual-dispatch", "mes-dispatch",
                "evt-dispatch-1", "user:dispatcher", baseline, baseline);
            Assert.True(fact.TryApplyMesDispatch(
                "DEV-1", "WC-1", baseline, baseline.AddHours(1), "evt-dispatch-1",
                "user:dispatcher", 1, baseline, baseline));
            seed.ScheduleOperationOverrides.Add(fact);
            await seed.SaveChangesAsync();
        }

        await using var staleDb = new ApplicationDbContext(Options(), new NoopMediator());
        await using var newerDb = new ApplicationDbContext(Options(), new NoopMediator());
        await staleDb.ScheduleOperationOverrides.SingleAsync();
        await newerDb.ScheduleOperationOverrides.SingleAsync();
        var clearHandler = CreateClearHandler(staleDb, new InMemoryIntegrationEventDeadLetterStore());
        var dispatchHandler = CreateDispatchHandler(newerDb, new InMemoryIntegrationEventDeadLetterStore());

        await dispatchHandler.HandleAsync(
            CreateEvent("evt-dispatch-3", baseline.AddMinutes(2), "DEV-3", revision: 3), CancellationToken.None);
        await clearHandler.HandleAsync(
            CreateClearEvent("evt-clear-2", baseline.AddMinutes(1), revision: 2), CancellationToken.None);

        await using var verificationDb = new ApplicationDbContext(Options(), new NoopMediator());
        var persisted = await verificationDb.ScheduleOperationOverrides.SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal(3, persisted.SourceRevision);
        Assert.Equal("DEV-3", persisted.ResourceId);
        Assert.Equal(2, await verificationDb.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Concurrent_equal_timestamp_revision_updates_reload_and_keep_highest_revision()
    {
        var databaseName = $"mes-dispatch-revision-concurrency-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot).Options;
        var occurredAtUtc = At(8);
        await using (var seed = new ApplicationDbContext(Options(), new NoopMediator()))
        {
            var fact = ScheduleOperationOverride.Create(
                "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-1", "WC-1",
                occurredAtUtc, occurredAtUtc.AddHours(1), "mes-manual-dispatch", "mes-dispatch",
                "evt-dispatch-1", "user:dispatcher", occurredAtUtc, occurredAtUtc);
            Assert.True(fact.TryApplyMesDispatch(
                "DEV-1", "WC-1", occurredAtUtc, occurredAtUtc.AddHours(1), "evt-dispatch-1",
                "user:dispatcher", 1, occurredAtUtc, occurredAtUtc));
            seed.ScheduleOperationOverrides.Add(fact);
            await seed.SaveChangesAsync();
        }

        await using var revisionTwoDb = new ApplicationDbContext(Options(), new NoopMediator());
        await using var revisionThreeDb = new ApplicationDbContext(Options(), new NoopMediator());
        await revisionTwoDb.ScheduleOperationOverrides.SingleAsync();
        await revisionThreeDb.ScheduleOperationOverrides.SingleAsync();
        var revisionTwoHandler = CreateClearHandler(revisionTwoDb, new InMemoryIntegrationEventDeadLetterStore());
        var revisionThreeHandler = CreateDispatchHandler(revisionThreeDb, new InMemoryIntegrationEventDeadLetterStore());

        await revisionThreeHandler.HandleAsync(
            CreateEvent("evt-dispatch-3", occurredAtUtc, "DEV-3", revision: 3), CancellationToken.None);
        await revisionTwoHandler.HandleAsync(
            CreateClearEvent("evt-clear-2", occurredAtUtc, revision: 2), CancellationToken.None);

        await using var verificationDb = new ApplicationDbContext(Options(), new NoopMediator());
        var persisted = await verificationDb.ScheduleOperationOverrides.SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal(3, persisted.SourceRevision);
        Assert.Equal("DEV-3", persisted.ResourceId);
        Assert.Equal(2, await verificationDb.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Concurrent_stale_snapshot_reloads_and_converges_to_newer_event()
    {
        var databaseName = $"mes-dispatch-concurrency-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot).Options;
        var baseline = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        await using (var seed = new ApplicationDbContext(Options(), new NoopMediator()))
        {
            seed.ScheduleOperationOverrides.Add(ScheduleOperationOverride.Create(
                "org-1", "env-1", "WO-1", "OP-1", 10, "DEV-BASE", "WC-1",
                baseline, baseline.AddHours(1), "mes-manual-dispatch", "mes-dispatch",
                "evt-base", "user:dispatcher", baseline, baseline));
            await seed.SaveChangesAsync();
        }

        await using var staleDb = new ApplicationDbContext(Options(), new NoopMediator());
        await using var newerDb = new ApplicationDbContext(Options(), new NoopMediator());
        await staleDb.ScheduleOperationOverrides.SingleAsync();
        await newerDb.ScheduleOperationOverrides.SingleAsync();
        var staleHandler = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(
            staleDb, new InMemoryIntegrationEventDeadLetterStore());
        var newerHandler = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(
            newerDb, new InMemoryIntegrationEventDeadLetterStore());

        await newerHandler.HandleAsync(CreateEvent("evt-newer", baseline.AddMinutes(10), "DEV-NEWER", revision: 0), CancellationToken.None);
        await staleHandler.HandleAsync(CreateEvent("evt-stale", baseline.AddMinutes(5), "DEV-STALE", revision: 0), CancellationToken.None);

        await using var verificationDb = new ApplicationDbContext(Options(), new NoopMediator());
        var persisted = await verificationDb.ScheduleOperationOverrides.SingleAsync();
        Assert.Equal("DEV-NEWER", persisted.ResourceId);
        Assert.Equal(baseline.AddMinutes(10), persisted.SourceOccurredAtUtc);
        Assert.Equal(2, await verificationDb.ProcessedIntegrationEvents.CountAsync());
    }

    private static MesOperationTaskManuallyDispatchedIntegrationEvent CreateEvent(
        string eventId, DateTimeOffset occurredAtUtc, string resourceId, long revision = 1) =>
        new(eventId, MesIntegrationEventTypes.OperationTaskManuallyDispatched, 1, occurredAtUtc,
            MesIntegrationEventSources.BusinessMes, $"corr-{eventId}", "cause-1", "org-1", "env-1",
            "user:dispatcher", $"mes:dispatch:{eventId}", new OperationTaskManuallyDispatchedPayload(
                "WO-1", "OP-1", 10, resourceId, "WC-1", occurredAtUtc,
                occurredAtUtc.AddHours(1), occurredAtUtc, revision));

    private static MesOperationTaskManualDispatchClearedIntegrationEvent CreateClearEvent(
        string eventId, DateTimeOffset occurredAtUtc, long revision,
        string reasonCode = MesManualDispatchClearReasonCodes.DeviceCleared) =>
        new(eventId, MesIntegrationEventTypes.OperationTaskManualDispatchCleared, 1, occurredAtUtc,
            MesIntegrationEventSources.BusinessMes, $"corr-{eventId}", "cause-1", "org-1", "env-1",
            "user:dispatcher", $"mes:dispatch-clear:{eventId}", new OperationTaskManualDispatchClearedPayload(
                "WO-1", "OP-1", 10, "DEV-1", "WC-1", At(8), At(9), revision,
                reasonCode, occurredAtUtc));

    private static MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride CreateDispatchHandler(
        ApplicationDbContext db, IIntegrationEventDeadLetterStore deadLetters) => new(db, deadLetters);

    private static MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride CreateClearHandler(
        ApplicationDbContext db, IIntegrationEventDeadLetterStore deadLetters) => new(db, deadLetters);

    private static DateTimeOffset At(int hour) =>
        new(2026, 7, 15, hour, 0, 0, TimeSpan.Zero);

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FailFirstSaveInterceptor : SaveChangesInterceptor
    {
        public const string FailureMessage = "non-unique constraint failure";
        private bool hasFailed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!hasFailed)
            {
                hasFailed = true;
                throw new DbUpdateException(FailureMessage);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
