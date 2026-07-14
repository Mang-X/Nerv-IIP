using MediatR;
using Microsoft.EntityFrameworkCore;
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
