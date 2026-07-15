using MediatR;
using Microsoft.EntityFrameworkCore;
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
        string reasonCode = "device-cleared") =>
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
}
