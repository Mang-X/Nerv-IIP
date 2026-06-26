using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Contracts.EquipmentRuntime;
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
    public async Task Alarm_consumer_skips_released_event_with_same_idempotency_key()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new CommandOnlySender(dbContext);
        var handler = new OpenWorkOrderWhenAlarmRaisedHandler(sender, dbContext, deadLetterStore);
        var alarm = CreateAlarmRaisedEvent();
        var releasedAlarm = alarm with { EventId = "evt-alarm-001-released" };

        await handler.HandleAsync(alarm, CancellationToken.None);
        await handler.HandleAsync(releasedAlarm, CancellationToken.None);

        Assert.Single(await dbContext.MaintenanceWorkOrders.ToArrayAsync());
        Assert.Equal(1, sender.CreateWorkOrderCommandCount);
        var processed = Assert.Single(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(alarm.EventId, processed.EventId);
        Assert.Equal(alarm.IdempotencyKey, processed.IdempotencyKey);
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

    [Fact]
    public async Task Stable_rule_alarm_events_open_one_work_order_and_clear_runtime_window()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new CommandOnlySender(dbContext);
        var raisedHandler = new OpenWorkOrderWhenAlarmRaisedHandler(sender, dbContext, deadLetterStore);
        var clearedHandler = new MarkWorkOrderAlarmClearedHandler(sender, dbContext, deadLetterStore);
        var raisedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var clearedAtUtc = raisedAtUtc.AddHours(1);

        await raisedHandler.HandleAsync(CreateAlarmRaisedEvent("evt-alarm-537-1", "TEMP_RULE", raisedAtUtc), CancellationToken.None);
        await raisedHandler.HandleAsync(CreateAlarmRaisedEvent("evt-alarm-537-2", "TEMP_RULE", raisedAtUtc.AddMinutes(1)), CancellationToken.None);
        await clearedHandler.HandleAsync(CreateAlarmClearedEvent("evt-alarm-clear-537", "TEMP_RULE", raisedAtUtc, clearedAtUtc), CancellationToken.None);

        var workOrder = await dbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.Equal("TEMP_RULE", workOrder.SourceAlarmId);
        Assert.True(workOrder.AssetUnavailable);
        Assert.True(workOrder.AlarmCleared);
        Assert.Equal(clearedAtUtc, workOrder.AlarmClearedAtUtc);
        dbContext.Entry(workOrder).Property(x => x.AssetUnavailableFromUtc).CurrentValue = raisedAtUtc;
        await dbContext.SaveChangesAsync();

        var availability = await new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext).Handle(
            new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest(
                "org-001",
                "env-dev",
                raisedAtUtc,
                raisedAtUtc.AddHours(4),
                ["DEV-CNC-01"],
                null)),
            CancellationToken.None);
        var activeAlarm = Assert.Single(availability.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
        Assert.Equal(raisedAtUtc, activeAlarm.StartUtc);
        Assert.Equal(clearedAtUtc, activeAlarm.EndUtc);

        var runtime = await new MaintenanceUnavailableWindowRuntimeHoursProvider(sender).CalculateFallbackAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            raisedAtUtc,
            raisedAtUtc.AddHours(4),
            CancellationToken.None);
        Assert.Equal(3m, runtime.RuntimeHours);
        Assert.Equal(AssetRuntimeSources.Fallback, runtime.RuntimeSource);
    }

    [Fact]
    public async Task Alarm_clear_command_marks_all_matching_open_work_orders_when_duplicate_alarm_facts_exist()
    {
        await using var dbContext = CreateDbContext();
        var clearedAtUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var first = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        var second = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        dbContext.MaintenanceWorkOrders.AddRange(first, second);
        await dbContext.SaveChangesAsync();

        await new MarkMaintenanceWorkOrderAlarmClearedCommandHandler(dbContext).Handle(
            new MarkMaintenanceWorkOrderAlarmClearedCommand("org-001", "env-dev", "alarm-001", clearedAtUtc),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var workOrders = await dbContext.MaintenanceWorkOrders.OrderBy(x => x.OpenedAtUtc).ToArrayAsync();
        Assert.All(workOrders, workOrder =>
        {
            Assert.True(workOrder.AlarmCleared);
            Assert.Equal(clearedAtUtc, workOrder.AlarmClearedAtUtc);
        });
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
        return CreateAlarmRaisedEvent("evt-alarm-001", "alarm-001", DateTimeOffset.UtcNow, eventVersion);
    }

    private static AlarmRaisedIntegrationEvent CreateAlarmRaisedEvent(
        string eventId,
        string externalAlarmId,
        DateTimeOffset raisedAtUtc,
        int eventVersion = 1)
    {
        return new AlarmRaisedIntegrationEvent(
            eventId,
            "industrialTelemetry.AlarmRaised",
            eventVersion,
            raisedAtUtc,
            "industrialTelemetry",
            "corr-alarm-001",
            "alarm-event-001",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            $"industrialTelemetry:alarm-raised:org-001:env-dev:DEV-CNC-01:OVER_TEMP:{externalAlarmId}:{eventId}",
            new AlarmRaisedPayload(
                "alarm-event-001",
                "DEV-CNC-01",
                "OVER_TEMP",
                "critical",
                raisedAtUtc,
                externalAlarmId));
    }

    private static AlarmClearedIntegrationEvent CreateAlarmClearedEvent(DateTimeOffset clearedAtUtc, int eventVersion = 1)
    {
        var raisedAtUtc = clearedAtUtc.AddHours(-1);
        return CreateAlarmClearedEvent("evt-alarm-clear-001", "alarm-001", raisedAtUtc, clearedAtUtc, eventVersion);
    }

    private static AlarmClearedIntegrationEvent CreateAlarmClearedEvent(
        string eventId,
        string externalAlarmId,
        DateTimeOffset raisedAtUtc,
        DateTimeOffset clearedAtUtc,
        int eventVersion = 1)
    {
        return new AlarmClearedIntegrationEvent(
            eventId,
            "industrialTelemetry.AlarmCleared",
            eventVersion,
            clearedAtUtc,
            "industrialTelemetry",
            "corr-alarm-001",
            "alarm-event-001",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            $"industrialTelemetry:alarm-cleared:org-001:env-dev:DEV-CNC-01:OVER_TEMP:{externalAlarmId}:{eventId}",
            new AlarmClearedPayload(
                "alarm-event-001",
                "DEV-CNC-01",
                "OVER_TEMP",
                "critical",
                raisedAtUtc,
                clearedAtUtc,
                externalAlarmId));
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

            if (request is QueryMaintenanceAvailabilityWindowsQuery query)
            {
                var handler = new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext);
                var response = await handler.Handle(query, cancellationToken);
                return (TResponse)(object)response;
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
