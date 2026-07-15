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
                "WO-1", "OP-1", 10, "DEV-1", "WC-1", start, start.AddHours(1), start));

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.Equal("OP-1", persisted.OperationId);
        Assert.Equal("DEV-1", persisted.ResourceId);
        Assert.Equal(start, persisted.StartUtc);
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
    public async Task Handle_does_not_allow_older_event_to_replace_newer_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-stale-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var handler = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(
            db, new InMemoryIntegrationEventDeadLetterStore());
        var older = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var newer = older.AddMinutes(10);

        await handler.HandleAsync(CreateEvent("evt-new", newer, "DEV-NEW"), CancellationToken.None);
        await handler.HandleAsync(CreateEvent("evt-old", older, "DEV-OLD"), CancellationToken.None);

        var persisted = await db.ScheduleOperationOverrides.SingleAsync();
        Assert.Equal("DEV-NEW", persisted.ResourceId);
        Assert.Equal(newer, persisted.SourceOccurredAtUtc);
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
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

        await newerHandler.HandleAsync(CreateEvent("evt-newer", baseline.AddMinutes(10), "DEV-NEWER"), CancellationToken.None);
        await staleHandler.HandleAsync(CreateEvent("evt-stale", baseline.AddMinutes(5), "DEV-STALE"), CancellationToken.None);

        await using var verificationDb = new ApplicationDbContext(Options(), new NoopMediator());
        var persisted = await verificationDb.ScheduleOperationOverrides.SingleAsync();
        Assert.Equal("DEV-NEWER", persisted.ResourceId);
        Assert.Equal(baseline.AddMinutes(10), persisted.SourceOccurredAtUtc);
        Assert.Equal(2, await verificationDb.ProcessedIntegrationEvents.CountAsync());
    }

    private static MesOperationTaskManuallyDispatchedIntegrationEvent CreateEvent(
        string eventId, DateTimeOffset occurredAtUtc, string resourceId) =>
        new(eventId, MesIntegrationEventTypes.OperationTaskManuallyDispatched, 1, occurredAtUtc,
            MesIntegrationEventSources.BusinessMes, $"corr-{eventId}", "cause-1", "org-1", "env-1",
            "user:dispatcher", $"mes:dispatch:{eventId}", new OperationTaskManuallyDispatchedPayload(
                "WO-1", "OP-1", 10, resourceId, "WC-1", occurredAtUtc,
                occurredAtUtc.AddHours(1), occurredAtUtc));

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
