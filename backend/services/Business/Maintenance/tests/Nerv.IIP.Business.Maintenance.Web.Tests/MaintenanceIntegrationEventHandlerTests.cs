using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceIntegrationEventHandlerTests
{
    [Fact]
    public async Task Alarm_consumer_creates_one_work_order_per_source_alarm_id()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new CommandOnlySender(dbContext);
        var handler = new OpenWorkOrderWhenAlarmRaisedHandler(sender, dbContext, deadLetterStore);
        var alarm = CreateAlarmRaisedEvent();

        await handler.HandleAsync(alarm, CancellationToken.None);
        await handler.HandleAsync(alarm, CancellationToken.None);

        var workOrders = await dbContext.MaintenanceWorkOrders.ToArrayAsync();
        Assert.Single(workOrders);
        Assert.Equal("alarm-001", workOrders[0].SourceAlarmId);
        Assert.True(workOrders[0].AssetUnavailable);
        Assert.Equal(1, sender.CreateWorkOrderCommandCount);
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
        Assert.Empty(await deadLetterStore.ListAsync(OpenWorkOrderWhenAlarmRaisedHandler.ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Alarm_consumer_dead_letters_unsupported_event_version_without_creating_work_order()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new MaintenanceIntegrationEventDeadLetterStore(dbContext);
        var handler = new OpenWorkOrderWhenAlarmRaisedHandler(new CommandOnlySender(dbContext), dbContext, deadLetterStore);

        await handler.HandleAsync(CreateAlarmRaisedEvent(eventVersion: 2), CancellationToken.None);

        Assert.Empty(await dbContext.MaintenanceWorkOrders.ToArrayAsync());
        var deadLetter = Assert.Single(await dbContext.IntegrationEventDeadLetters.ToArrayAsync());
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
        Assert.Equal(IntegrationEventDeadLetterStatus.Pending, deadLetter.Status);
    }

    [Fact]
    public async Task Alarm_cleared_consumer_marks_matching_open_work_order_without_completing_it()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new CommandOnlySender(dbContext);
        var raisedHandler = new OpenWorkOrderWhenAlarmRaisedHandler(sender, dbContext, deadLetterStore);
        var clearedHandler = new MarkWorkOrderAlarmClearedHandler(sender, dbContext, deadLetterStore);
        var clearedAtUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

        await raisedHandler.HandleAsync(CreateAlarmRaisedEvent(), CancellationToken.None);
        await clearedHandler.HandleAsync(CreateAlarmClearedEvent(clearedAtUtc), CancellationToken.None);
        await clearedHandler.HandleAsync(CreateAlarmClearedEvent(clearedAtUtc), CancellationToken.None);

        var workOrder = await dbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.True(workOrder.AlarmCleared);
        Assert.Equal(clearedAtUtc, workOrder.AlarmClearedAtUtc);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, workOrder.Status);
        Assert.Equal(1, sender.ClearAlarmCommandCount);
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"maintenance-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static AlarmRaisedIntegrationEvent CreateAlarmRaisedEvent(int eventVersion = 1)
    {
        return new AlarmRaisedIntegrationEvent(
            "evt-alarm-001",
            "industrialTelemetry.AlarmRaised",
            eventVersion,
            DateTimeOffset.UtcNow,
            "industrialTelemetry",
            "corr-alarm-001",
            "alarm-event-001",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            "industrialTelemetry:alarm-raised:org-001:env-dev:alarm-001",
            new AlarmRaisedPayload(
                "alarm-event-001",
                "DEV-CNC-01",
                "OVER_TEMP",
                "critical",
                DateTimeOffset.UtcNow,
                "alarm-001"));
    }

    private static AlarmClearedIntegrationEvent CreateAlarmClearedEvent(DateTimeOffset clearedAtUtc, int eventVersion = 1)
    {
        var raisedAtUtc = clearedAtUtc.AddHours(-1);
        return new AlarmClearedIntegrationEvent(
            "evt-alarm-clear-001",
            "industrialTelemetry.AlarmCleared",
            eventVersion,
            clearedAtUtc,
            "industrialTelemetry",
            "corr-alarm-001",
            "alarm-event-001",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            "industrialTelemetry:alarm-cleared:org-001:env-dev:alarm-001",
            new AlarmClearedPayload(
                "alarm-event-001",
                "DEV-CNC-01",
                "OVER_TEMP",
                "critical",
                raisedAtUtc,
                clearedAtUtc,
                "alarm-001"));
    }

    private sealed class CommandOnlySender(ApplicationDbContext dbContext) : ISender
    {
        public int CreateWorkOrderCommandCount { get; private set; }
        public int ClearAlarmCommandCount { get; private set; }

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateMaintenanceWorkOrderCommand command)
            {
                CreateWorkOrderCommandCount++;
                var handler = new CreateMaintenanceWorkOrderCommandHandler(dbContext);
                var id = await handler.Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)id;
            }

            throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            if (request is MarkMaintenanceWorkOrderAlarmClearedCommand clearCommand)
            {
                return SendClearAsync(clearCommand, cancellationToken);
            }

            throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.");
        }

        private async Task SendClearAsync(MarkMaintenanceWorkOrderAlarmClearedCommand command, CancellationToken cancellationToken)
        {
            ClearAlarmCommandCount++;
            var handler = new MarkMaintenanceWorkOrderAlarmClearedCommandHandler(dbContext);
            await handler.Handle(command, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Only typed commands are supported by this test sender.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Streams are not supported by this test sender.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Streams are not supported by this test sender.");
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
